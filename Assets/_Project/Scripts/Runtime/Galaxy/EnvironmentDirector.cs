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
        Volume _volume;

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

        /// <summary>Aboard the ship: the orbited body's sky (Earth, Moon, Mars have one), anything else = the ship scene's
        /// own skybox (open space). The body comes from the saved ship state, so the sky follows save/load.</summary>
        public static Material ResolveShipSky(CelestialBodyDefinition body, Material sceneDefault, Material configFallback)
        {
            if (body != null && body.orbitSkybox != null) return body.orbitSkybox;
            return sceneDefault != null ? sceneDefault : configFallback;
        }

        /// <summary>Sky for a scene: aboard the ship see <see cref="ResolveShipSky"/>; elsewhere the environment's sky if set.</summary>
        public static Material ResolveSky(GameSession session, string scene)
        {
            var cfg = session.Config;
            if (scene == SceneNames.ShipStarter)
            {
                CaptureSceneSky(scene, false);
                return ResolveShipSky(session.Exploration?.CurrentBody, _sceneDefaultSky, cfg.defaultSpaceSkybox);
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

        public void Apply(string scene)
        {
            var session = XenoarchRuntime.Session;
            if (session == null || scene == SceneNames.Bootstrap || scene == SceneNames.MainMenu) return;
            // Handheld scanner rides on the player (added at runtime so the player prefab stays untouched).
            foreach (var f in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
                if (f.GetComponent<Exploration.SurfaceScannerTool>() == null) f.gameObject.AddComponent<Exploration.SurfaceScannerTool>();

            ApplySky(session, scene);
            var env = Resolve(session.Config, scene);
            Current = env;
            if (env == null) return;

            foreach (var fpc in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None)) fpc.gravity = env.gravity;

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
