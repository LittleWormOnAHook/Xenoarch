using DMI.Xenoarch.Core;
using DMI.Xenoarch.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Applies the <see cref="EnvironmentProfile"/> of the active scene (ship interior, body surface, station) on every
    /// scene load: player gravity, optional ambient/fog, optional global URP Volume. Lives on the runtime object.
    /// </summary>
    public sealed class EnvironmentDirector : MonoBehaviour
    {
        public EnvironmentProfile Current { get; private set; }
        Scene _ambientScaledScene; // scene instance already darkened (RenderSettings reset on each load)
        Volume _volume, _baseVolume;
        static VolumeProfile _fallbackPost;

        // The skybox authored in the loaded scene (its open-space / default sky), captured before we swap anything.
        static string _defaultSkyScene;
        static Material _sceneDefaultSky;
        public static Material SceneDefaultSky => _sceneDefaultSky;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _defaultSkyScene = null; _sceneDefaultSky = null; }

        static void CaptureSceneSky(string scene, bool force)
        {
            if (!force && _defaultSkyScene == scene) return;
            _defaultSkyScene = scene;
            _sceneDefaultSky = RenderSettings.skybox;
        }

        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        void Start() { var n = SceneManager.GetActiveScene().name; CaptureSceneSky(n, false); Apply(n); }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            CaptureSceneSky(s.name, true); // fresh scene = its own authored sky
            Apply(s.name);
        }

        public static EnvironmentProfile Resolve(XenoarchConfig cfg, string scene)
        {
            if (cfg == null) return null;
            if (scene == SceneNames.ShipStarter && cfg.starterShip != null && cfg.starterShip.interiorEnvironment != null)
                return cfg.starterShip.interiorEnvironment;
            var body = cfg.galaxy != null ? cfg.galaxy.FindBodyByScene(scene) : null;
            if (body != null && body.environment != null) return body.environment;
            return cfg.defaultEnvironment;
        }

        /// <summary>Aboard the ship: the orbited body's sky (Earth, Moon, Mars have one); anything else = open space: the
        /// system's own space sky, else the config default (the Milky Way), else the ship scene's authored sky. The body
        /// comes from the saved ship state, so the sky follows save/load.</summary>
        public static Material ResolveShipSky(CelestialBodyDefinition body, Material sceneDefault, Material configFallback)
            => ResolveShipSky(body, null, sceneDefault, configFallback, false);

        /// <param name="preferOrbitViews">Bodies with an <see cref="OrbitViewDefinition"/> skip their painted orbit sky
        /// (the planet is drawn as a sphere outside the window instead).</param>
        public static Material ResolveShipSky(CelestialBodyDefinition body, StarSystemDefinition system, Material sceneDefault, Material configFallback, bool preferOrbitViews)
        {
            if (body != null && body.orbitSkybox != null && !(preferOrbitViews && body.orbitView != null)) return body.orbitSkybox;
            if (system != null && system.spaceSkybox != null) return system.spaceSkybox;
            return configFallback != null ? configFallback : sceneDefault;
        }

        /// <summary>Sky for a scene: aboard the ship see <see cref="ResolveShipSky"/>; elsewhere the environment's sky if set.</summary>
        public static Material ResolveSky(GameSession session, string scene)
        {
            var cfg = session.Config;
            if (scene == SceneNames.ShipStarter)
            {
                CaptureSceneSky(scene, false);
                return ResolveShipSky(session.Exploration?.CurrentBody, session.Exploration?.CurrentSystem, _sceneDefaultSky, cfg.defaultSpaceSkybox, cfg.preferOrbitViews);
            }
            var env = Resolve(cfg, scene);
            return env != null ? env.skybox : null;
        }

        public static void ApplySky(GameSession session, string scene)
        {
            var sky = ResolveSky(session, scene);
            if (sky == null || RenderSettings.skybox == sky) return;
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Base post stack for scenes that have no global Volume of their own (the ship keeps its authored one).</summary>
        void ApplyBasePost(XenoarchConfig cfg, Scene scene)
        {
            bool sceneHasVolume = false;
            foreach (var v in FindObjectsByType<Volume>())
                if (v != _volume && v != _baseVolume && v.isGlobal && v.enabled && v.gameObject.scene == scene) { sceneHasVolume = true; break; }
            bool on = cfg.basePostEnabled && !sceneHasVolume;
            if (on && _baseVolume == null)
            {
                var go = new GameObject("[Base Post Volume]");
                go.transform.SetParent(transform, false);
                _baseVolume = go.AddComponent<Volume>();
                _baseVolume.isGlobal = true;
                _baseVolume.priority = -20f; // below the environment volume and anything in the scene
            }
            if (_baseVolume != null)
            {
                _baseVolume.sharedProfile = cfg.basePostProfile != null ? cfg.basePostProfile : (_fallbackPost != null ? _fallbackPost : (_fallbackPost = XenoarchPostStack.BuildDefault()));
                _baseVolume.enabled = on;
            }
            if (on) XenoarchPostStack.EnableCameraPost();
        }

        public void Apply(string scene)
        {
            var session = XenoarchRuntime.Session;
            if (session == null || scene == SceneNames.Bootstrap || scene == SceneNames.MainMenu) return;
            // Handheld scanner rides on the player (added at runtime so the player prefab stays untouched).
            foreach (var f in FindObjectsByType<FirstPersonController>())
                if (f.GetComponent<Exploration.SurfaceScannerTool>() == null) f.gameObject.AddComponent<Exploration.SurfaceScannerTool>();

            ApplySky(session, scene);
            // Darker space aboard the ship: scale the scene's own ambient (once per scene load; the scene asset is untouched).
            var active = SceneManager.GetActiveScene();
            if (scene == SceneNames.ShipStarter && _ambientScaledScene != active)
            {
                _ambientScaledScene = active;
                float k = session.Config.shipAmbientScale;
                RenderSettings.ambientLight *= k; RenderSettings.ambientSkyColor *= k; RenderSettings.ambientEquatorColor *= k; RenderSettings.ambientGroundColor *= k;
            }
            ApplyBasePost(session.Config, active);
            var env = Resolve(session.Config, scene);
            Current = env;
            if (env == null) return;

            foreach (var fpc in FindObjectsByType<FirstPersonController>()) fpc.gravity = env.gravity;

            if (env.applyLighting)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = env.ambientColor;
                RenderSettings.fog = env.fog;
                if (env.fog) { RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogColor = env.fogColor; RenderSettings.fogDensity = env.fogDensity; }
            }

            if (env.volumeProfile != null)
            {
                if (_volume == null)
                {
                    var go = new GameObject("[Environment Volume]");
                    go.transform.SetParent(transform, false);
                    _volume = go.AddComponent<Volume>();
                    _volume.isGlobal = true;
                    _volume.priority = -10f; // scene-authored volumes win
                }
                _volume.sharedProfile = env.volumeProfile;
                _volume.enabled = true;
            }
            else if (_volume != null) _volume.enabled = false;

            Debug.Log($"[Xenoarch] Environment '{env.displayName}' applied to {scene} (g={env.gravity:0.##})");
        }
    }
}
