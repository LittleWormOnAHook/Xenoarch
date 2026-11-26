using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// The local star in every play scene: a "[Sun]" object with a visible sun disc (Xenoarch/SunDisc, drawn far out
    /// along the sun light so walls hide it) and a URP data-driven lens flare (LensFlareComponentSRP, occluded by depth).
    /// Size and brightness are physical: the disc's angular diameter is 2·atan(R★ / d) (Earth / Moon 0.533°, Mars 0.35°,
    /// Jupiter 0.10°, Proxima b 0.68° of a dim red star) and the flare / light scale with the flux L / d² (perceptually
    /// compressed, sqrt). Distance comes from the body data (moons / stations use their planet's orbit), star radius and
    /// luminosity from <see cref="StarPhysics"/>. Surface scenes keep their own directional light (direction untouched,
    /// intensity scaled); the ship gets a runtime sun light shining in through the front-window side.
    /// Lives on the runtime object; rebuilt on every scene load and after an FTL arrival (<see cref="Refresh"/>).
    /// </summary>
    public sealed class SunDirector : MonoBehaviour
    {
        public static SunDirector Instance { get; private set; }
        /// <summary>Hide the sun disc + flare (e.g. inside the FTL tunnel).</summary>
        public static bool Hidden;

        [Tooltip("Light intensity at Earth-equivalent flux on airless / thin-air surfaces (the Moon scene was authored at 1.6).")]
        public float surfaceLightAtEarthFlux = 1.6f;
        [Tooltip("Ship sun light intensity at Earth-equivalent flux.")] public float shipLightAtEarthFlux = 1.1f;
        [Tooltip("Scale the scene's directional light intensity by the flux (surfaces only).")] public bool scaleSceneLight = true;
        [Tooltip("Lens flare intensity at Earth-equivalent flux.")] public float flareIntensity = 1f;
        [Tooltip("Sun disc HDR brightness (bloom picks it up). Surface brightness of a star does not change with distance.")] public float discBrightness = 24f;
        [Tooltip("Smallest drawn angular diameter (deg) so a distant sun stays a visible point; brightness is reduced to keep the same total light.")]
        public float minDiscDeg = 0.08f;
        [Tooltip("Ship: sun direction relative to the helm window direction (yaw, pitch in degrees).")] public Vector2 shipSunOffsetDeg = new Vector2(38f, 14f);

        public StarPhysics.SunView View { get; private set; }
        public Light SunLight => _light;

        GameObject _sun;
        Transform _disc;
        Material _discMat;
        Light _light;
        bool _ownsLight;
        LensFlareComponentSRP _flare;
        static LensFlareDataSRP _flareData;
        static Mesh _quad;

        static readonly int SunDirId = Shader.PropertyToID("_XenoSunDir");
        static readonly int SunColorId = Shader.PropertyToID("_XenoSunColor");
        static readonly int SunSizeId = Shader.PropertyToID("_XenoSunAngularRadius");

        void Awake() { Instance = this; }
        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        void Start() { Refresh(); }

        void OnSceneLoaded(Scene s, LoadSceneMode mode) { if (mode == LoadSceneMode.Single) { Hidden = false; Refresh(); } }

        /// <summary>Re-resolve the location (body / system) and rebuild the sun for the active scene.</summary>
        public static void Refresh() { if (Instance != null) Instance.Build(); }

        void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (_sun != null) Destroy(_sun);
            if (_ownsLight && _light != null) Destroy(_light.gameObject);
            _sun = null; _light = null; _ownsLight = false; _flare = null; _disc = null;
            var session = XenoarchRuntime.Session;
            if (session == null || scene.name == SceneNames.Bootstrap || scene.name == SceneNames.MainMenu) return;

            var cfg = session.Config;
            var galaxy = cfg != null ? cfg.galaxy : null;
            bool ship = scene.name == SceneNames.ShipStarter;
            CelestialBodyDefinition body; StarSystemDefinition sys;
            if (ship) { body = session.Exploration?.CurrentBody; sys = session.Exploration?.CurrentSystem; }
            else { body = galaxy != null ? galaxy.FindBodyByScene(scene.name) : null; sys = body != null && galaxy != null ? galaxy.SystemOf(body.Id) : null; }
            if (sys == null && galaxy != null) sys = galaxy.FindSystem("sys.sol");
            var v = StarPhysics.For(galaxy, body, sys);
            View = v;

            // The light: the scene's own sun (direction untouched) or, aboard the ship, a runtime one.
            _light = FindSceneSun(scene);
            float k = Mathf.Sqrt(v.flux);
            if (_light == null)
            {
                var lgo = new GameObject("[Sun Light]");
                SceneManager.MoveGameObjectToScene(lgo, scene);
                _light = lgo.AddComponent<Light>();
                _light.type = LightType.Directional;
                _light.shadows = LightShadows.Soft;
                _light.color = v.color;
                _light.intensity = shipLightAtEarthFlux * k;
                lgo.transform.rotation = Quaternion.LookRotation(-ShipSunDirection(), Vector3.up);
                _ownsLight = true;
                if (RenderSettings.sun == null) RenderSettings.sun = _light;
            }
            else if (scaleSceneLight && body != null && (body.kind == CelestialKind.Planet || body.kind == CelestialKind.Moon || body.kind == CelestialKind.DwarfPlanet))
            {
                _light.intensity = surfaceLightAtEarthFlux * k;
                if (sys != null && sys.Id != "sys.sol") _light.color *= v.color;
            }

            _sun = new GameObject("[Sun]");
            SceneManager.MoveGameObjectToScene(_sun, scene);
            _sun.transform.rotation = _light.transform.rotation;

            // Visible disc
            var sh = Shader.Find("Xenoarch/SunDisc");
            if (sh != null)
            {
                var dgo = new GameObject("Sun Disc");
                dgo.transform.SetParent(_sun.transform, false);
                dgo.AddComponent<MeshFilter>().sharedMesh = Quad();
                var mr = dgo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (_discMat == null) { _discMat = new Material(sh) { name = "Sun Disc (runtime)" }; DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(_discMat); }
                float drawDeg = Mathf.Max(minDiscDeg, v.angularDiameterDeg);
                float areaK = (v.angularDiameterDeg * v.angularDiameterDeg) / (drawDeg * drawDeg); // keep total light for a clamped point
                _discMat.SetColor("_Color", v.color * discBrightness * areaK);
                _discMat.SetFloat("_Glow", Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(k)));
                mr.sharedMaterial = _discMat;
                _disc = dgo.transform;
            }

            // Lens flare (URP data-driven, depth occluded so the hull / terrain hides it)
            _flare = _sun.AddComponent<LensFlareComponentSRP>();
            _flare.lensFlareData = FlareData();
            _flare.intensity = flareIntensity * Mathf.Clamp(k, 0.05f, 3f);
            _flare.scale = Mathf.Clamp(Mathf.Sqrt(v.angularDiameterDeg / 0.533f), 0.35f, 2.5f);
            _flare.useOcclusion = true;
            _flare.occlusionRadius = 0.5f;
            _flare.sampleCount = 24;
            _flare.allowOffScreen = false;
            _flare.attenuationByLightShape = false;
            _flare.lightOverride = _light;

            Debug.Log($"[Xenoarch] Sun: {(sys != null ? sys.displayName : "?")} at {v.distanceAU:0.###} AU, R={v.radiusSolar:0.###} Rsun, " +
                      $"angular {v.angularDiameterDeg:0.###} deg, flux {v.flux:0.###} x Earth ({(ship ? "ship light" : "scene light")} {_light.intensity:0.##})");
            LateUpdate();
        }

        Vector3 ShipSunDirection()
        {
            Vector3 w = Vector3.forward;
            var helm = FindAnyObjectByType<Ship.CockpitHelm>();
            if (helm != null && helm.windowDirection.sqrMagnitude > 0.01f) w = helm.windowDirection;
            w.y = 0f; if (w.sqrMagnitude < 0.01f) w = Vector3.forward; w.Normalize();
            return Quaternion.LookRotation(w, Vector3.up) * Quaternion.Euler(-shipSunOffsetDeg.y, shipSunOffsetDeg.x, 0f) * Vector3.forward;
        }

        static Light FindSceneSun(Scene scene)
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional && RenderSettings.sun.gameObject.scene == scene && RenderSettings.sun.isActiveAndEnabled)
                return RenderSettings.sun;
            Light best = null;
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional && l.isActiveAndEnabled && l.gameObject.scene == scene && (best == null || l.intensity > best.intensity)) best = l;
            return best;
        }

        void LateUpdate()
        {
            if (_sun == null || _light == null) return;
            var cam = Camera.main;
            Vector3 toSun = -_light.transform.forward;
            _sun.transform.rotation = _light.transform.rotation;
            Shader.SetGlobalVector(SunDirId, toSun);
            Shader.SetGlobalColor(SunColorId, View.color);
            Shader.SetGlobalFloat(SunSizeId, View.angularDiameterDeg * 0.5f * Mathf.Deg2Rad);
            bool show = !Hidden && cam != null;
            if (_flare != null && _flare.enabled != show) _flare.enabled = show;
            if (_disc == null) return;
            if (_disc.gameObject.activeSelf != show) _disc.gameObject.SetActive(show);
            if (!show) return;
            float dist = Mathf.Min(cam.farClipPlane * 0.92f, 9000f);
            Vector3 eye = cam.transform.position;
            _sun.transform.position = eye + toSun * dist;
            _disc.position = eye + toSun * dist;
            _disc.rotation = Quaternion.LookRotation(-toSun, Mathf.Abs(toSun.y) > 0.99f ? Vector3.forward : Vector3.up);
            float drawDeg = Mathf.Max(minDiscDeg, View.angularDiameterDeg);
            float diameter = 2f * dist * Mathf.Tan(drawDeg * 0.5f * Mathf.Deg2Rad);
            _disc.localScale = Vector3.one * diameter * 10f; // quad holds the disc (1/10) plus corona glow
        }

        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "Sun Quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateBounds();
            _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return _quad;
        }

        /// <summary>Runtime lens flare: core glow, wide halo, faint ring, anamorphic streak and a few ghosts toward the
        /// screen centre. Procedural shapes only (no textures).</summary>
        static LensFlareDataSRP FlareData()
        {
            if (_flareData != null) return _flareData;
            _flareData = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            _flareData.name = "Xenoarch Sun Flare (runtime)";
            _flareData.elements = new[]
            {
                Circle(0f, 0.55f, 0.9f, 0.85f, 0.5f, Color.white),                     // core glow
                Circle(0f, 2.6f, 0.18f, 1f, 0.15f, new Color(1f, 0.92f, 0.8f)),        // wide halo
                Streak(0f, new Vector2(9f, 0.035f), 0.35f, new Color(0.75f, 0.85f, 1f)), // anamorphic streak
                Ring(0f, 2.1f, 0.05f, new Color(0.6f, 0.75f, 1f)),                      // faint ring
                Poly(0.55f, 0.22f, 0.10f, new Color(0.5f, 0.8f, 1f)),                  // ghosts
                Poly(0.95f, 0.12f, 0.12f, new Color(1f, 0.7f, 0.4f)),
                Poly(1.35f, 0.42f, 0.06f, new Color(0.6f, 1f, 0.7f)),
                Circle(1.75f, 0.30f, 0.07f, 0.9f, 0.6f, new Color(0.7f, 0.55f, 1f)),
                Poly(-0.35f, 0.16f, 0.06f, new Color(1f, 0.85f, 0.6f)),
            };
            return _flareData;
        }

        static LensFlareDataElementSRP Base(float pos, Color tint, float intensity)
        {
            var e = new LensFlareDataElementSRP();
            e.visible = true; e.position = pos; e.tint = tint; e.localIntensity = intensity;
            e.blendMode = SRPLensFlareBlendMode.Additive; e.modulateByLightColor = true;
            e.preserveAspectRatio = false; e.autoRotate = false; e.allowMultipleElement = false;
            return e;
        }

        static LensFlareDataElementSRP Circle(float pos, float size, float intensity, float edgeOffset, float fallOff, Color tint)
        {
            var e = Base(pos, tint, intensity);
            e.flareType = SRPLensFlareType.Circle; e.uniformScale = size; e.sizeXY = Vector2.one;
            e.edgeOffset = edgeOffset; e.fallOff = fallOff;
            return e;
        }

        static LensFlareDataElementSRP Streak(float pos, Vector2 size, float intensity, Color tint)
        {
            var e = Base(pos, tint, intensity);
            e.flareType = SRPLensFlareType.Circle; e.uniformScale = 1f; e.sizeXY = size;
            e.edgeOffset = 1f; e.fallOff = 0.35f;
            return e;
        }

        static LensFlareDataElementSRP Ring(float pos, float size, float intensity, Color tint)
        {
            var e = Base(pos, tint, intensity);
            e.flareType = SRPLensFlareType.Ring; e.uniformScale = size; e.sizeXY = Vector2.one;
            e.ringThickness = 0.06f; e.edgeOffset = 0.4f; e.fallOff = 1f;
            return e;
        }

        static LensFlareDataElementSRP Poly(float pos, float size, float intensity, Color tint)
        {
            var e = Base(pos, tint, intensity);
            e.flareType = SRPLensFlareType.Polygon; e.uniformScale = size; e.sizeXY = Vector2.one;
            e.sideCount = 6; e.sdfRoundness = 0.35f; e.edgeOffset = 0.25f; e.fallOff = 1f;
            return e;
        }
    }
}
