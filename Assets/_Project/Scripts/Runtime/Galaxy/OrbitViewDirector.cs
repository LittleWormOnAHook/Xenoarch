using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Aboard the ship: shows the orbited planet outside the window when the body (or, with no body, the system on
    /// arrival) has an <see cref="OrbitViewDefinition"/> and no painted orbit skybox. A lat-long UV sphere with the
    /// PlanetPack02-derived URP materials, sized to the definition's apparent diameter, lit by the sun light. ZEN-17:
    /// each arrival gets a seeded, saved variation (see <see cref="OrbitViewMotionSettings"/>) and the planet turns and
    /// drifts slowly while parked. Rebuilt on scene load and FTL arrival.
    /// </summary>
    public sealed class OrbitViewDirector : MonoBehaviour
    {
        public static OrbitViewDirector Instance { get; private set; }
        GameObject _root;
        Transform _planet;
        OrbitViewDefinition _def;
        static Mesh _sphere;

        void Awake() { Instance = this; }
        void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        void Start() { Build(); }
        void OnSceneLoaded(Scene s, LoadSceneMode mode) { if (mode == LoadSceneMode.Single) Build(); }

        public static void Refresh() { if (Instance != null) Instance.Build(); }

        public static OrbitViewDefinition Resolve(CelestialBodyDefinition body, StarSystemDefinition sys, bool preferOrbitViews = true)
        {
            if (body != null) return body.orbitSkybox == null || preferOrbitViews ? body.orbitView : null;
            return sys != null ? sys.arrivalOrbitView : null;
        }

        // ZEN-17 placement (per arrival, seeded) and motion state
        Vector3 _eye, _window;
        Vector2 _baseOffset, _sunJitter;
        float _dist, _spin, _tilt, _face, _phase, _sunPhase;
        OrbitViewMotionSettings _m;

        /// <summary>Extra sun yaw / pitch (deg) on top of SunDirector.shipSunOffsetDeg: arrival jitter + drift. Ship only.</summary>
        public static Vector2 SunOffsetExtraDeg { get; private set; }
        public static bool HasSunOffset { get; private set; }

        void Build()
        {
            if (_root != null) Destroy(_root);
            _root = null; _planet = null; _def = null; HasSunOffset = false; SunOffsetExtraDeg = Vector2.zero;
            var scene = SceneManager.GetActiveScene();
            var session = XenoarchRuntime.Session;
            if (session == null || scene.name != SceneNames.ShipStarter || session.Exploration == null) return;
            var x = session.Exploration;
            var body = x.CurrentBody;
            var def = Resolve(body, x.CurrentSystem, session.Config == null || session.Config.preferOrbitViews);
            if (def == null || def.planetMaterial == null) return;
            _def = def;
            _m = session.Config != null && session.Config.orbitViewMotion != null ? session.Config.orbitViewMotion : new OrbitViewMotionSettings();
            bool vary = _m.enabled;

            // Seed: saved per arrival; older saves (0) fall back to the body / system id so it is still stable.
            int seed = x.Ship.arrivalSeed != 0 ? x.Ship.arrivalSeed : StableHash((body != null ? body.Id : x.Ship.currentSystemId) ?? "orbit");
            var rng = new System.Random(seed);
            float R(float range) => vary ? ((float)rng.NextDouble() * 2f - 1f) * range : 0f;
            var jitter = new Vector2(R(_m.placementJitterDeg.x), R(_m.placementJitterDeg.y));
            float sizeK = 1f + R(_m.sizeJitter);
            _sunJitter = new Vector2(R(_m.sunJitterDeg.x), R(_m.sunJitterDeg.y));
            _face = vary ? (float)rng.NextDouble() * _m.faceRandomDeg : 0f;
            _tilt = def.tiltDeg + R(_m.tiltJitterDeg);
            _phase = vary ? (float)rng.NextDouble() * Mathf.PI * 2f : 0f;
            _sunPhase = _phase + 1.3f;
            _baseOffset = def.windowOffsetDeg + jitter;

            // Spin from the body's real rotation period, sped up; else the view's own spin.
            _spin = def.spinDegPerSec;
            if (vary && body != null && Mathf.Abs(body.rotationPeriodHours) > 0.01f)
            {
                float s = 360f / (Mathf.Abs(body.rotationPeriodHours) * 3600f) * _m.spinSpeedup;
                _spin = Mathf.Clamp(s, _m.spinClampDegPerSec.x, _m.spinClampDegPerSec.y) * Mathf.Sign(body.rotationPeriodHours);
            }

            _window = Vector3.forward; _eye = Vector3.zero;
            var helm = FindAnyObjectByType<Ship.CockpitHelm>();
            if (helm != null) { _eye = helm.transform.position; if (helm.windowDirection.sqrMagnitude > 0.01f) _window = helm.windowDirection; }
            _window.y = 0f; if (_window.sqrMagnitude < 0.01f) _window = Vector3.forward; _window.Normalize();

            var cam = Camera.main;
            float far = cam != null ? cam.farClipPlane : 1000f;
            float diameter = Mathf.Clamp(def.apparentDiameterDeg * sizeK, 2f, 120f);
            float radius0 = Mathf.Sin(diameter * 0.5f * Mathf.Deg2Rad);
            // centre distance so the far side of the atmosphere stays inside the far clip plane
            _dist = Mathf.Min(6000f, far * 0.85f / (1f + radius0 * def.atmosphereScale));
            float radius = _dist * radius0;

            _root = new GameObject("[Orbit View]");
            SceneManager.MoveGameObjectToScene(_root, scene);
            _planet = MakeSphere("Planet", def.planetMaterial, radius);
            if (def.atmosphereMaterial != null) MakeSphere("Atmosphere", def.atmosphereMaterial, radius * def.atmosphereScale);
            Place(Elapsed(session));
            Debug.Log($"[Xenoarch] Orbit view: {def.name} ({diameter:0.#}° at {_dist:0} m, offset {_baseOffset.x:0.#}/{_baseOffset.y:0.#}°, face {_face:0}°, spin {_spin:0.###}°/s, seed {seed})");
        }

        float Elapsed(GameSession s)
        {
            float t = s.PlaySeconds - s.Exploration.Ship.arrivalPlaySeconds;
            return t >= 0f ? t : s.PlaySeconds;
        }

        /// <summary>Position, tilt and spin at <paramref name="t"/> seconds since arrival (saved clock, so a reload matches).</summary>
        void Place(float t)
        {
            if (_root == null || _def == null) return;
            float w = _m != null && _m.enabled && _m.driftPeriodSec > 1f ? Mathf.PI * 2f / _m.driftPeriodSec : 0f;
            var drift = w > 0f ? new Vector2(Mathf.Sin(t * w + _phase) * _m.driftDeg.x, Mathf.Sin(t * w * 0.7f + _phase * 1.7f) * _m.driftDeg.y) : Vector2.zero;
            var off = _baseOffset + drift;
            if (_m != null)
            {
                // Keep it framed: never further from the window direction than the authored offset + a margin.
                float limit = Mathf.Min(_m.maxOffsetDeg, _def.windowOffsetDeg.magnitude + _m.maxExtraOffsetDeg);
                if (off.magnitude > limit) off = off.normalized * limit;
            }
            Vector3 dir = Quaternion.LookRotation(_window, Vector3.up) * Quaternion.Euler(-off.y, off.x, 0f) * Vector3.forward;
            _root.transform.position = _eye + dir * _dist;
            _root.transform.rotation = Quaternion.Euler(0f, 0f, _tilt);
            float spinAngle = _face - _spin * t;
            foreach (Transform c in _root.transform) c.localRotation = Quaternion.Euler(0f, spinAngle, 0f);

            if (_m != null && _m.enabled)
            {
                var sunDrift = w > 0f ? new Vector2(Mathf.Sin(t * w + _sunPhase) * _m.sunDriftDeg.x, Mathf.Sin(t * w * 0.7f + _sunPhase) * _m.sunDriftDeg.y) : Vector2.zero;
                SunOffsetExtraDeg = _sunJitter + sunDrift; HasSunOffset = true;
            }
        }

        Transform MakeSphere(string name, Material m, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.localScale = Vector3.one * radius;
            go.AddComponent<MeshFilter>().sharedMesh = Sphere();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        void Update()
        {
            var s = XenoarchRuntime.Session;
            if (_root == null || _def == null || s == null) return;
            Place(Elapsed(s));
        }

        static int StableHash(string str)
        {
            unchecked { int h = 23; foreach (char ch in str) h = h * 31 + ch; return h == 0 ? 1 : h; }
        }

        /// <summary>Unit UV sphere with lat-long UVs (u = longitude, v = latitude) and tangents, matching PlanetPack02 maps.</summary>
        static Mesh Sphere()
        {
            if (_sphere != null) return _sphere;
            const int lon = 128, lat = 64;
            var v = new Vector3[(lon + 1) * (lat + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var tri = new int[lon * lat * 6];
            for (int j = 0; j <= lat; j++)
            {
                float vv = j / (float)lat, th = vv * Mathf.PI; // 0 = south pole
                for (int i = 0; i <= lon; i++)
                {
                    float uu = i / (float)lon, ph = uu * Mathf.PI * 2f;
                    var p = new Vector3(-Mathf.Sin(th) * Mathf.Cos(ph), -Mathf.Cos(th), -Mathf.Sin(th) * Mathf.Sin(ph));
                    int k = j * (lon + 1) + i;
                    v[k] = p; n[k] = p; uv[k] = new Vector2(uu, vv);
                }
            }
            int t = 0;
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + lon + 1;
                    tri[t++] = a; tri[t++] = b; tri[t++] = a + 1;
                    tri[t++] = a + 1; tri[t++] = b; tri[t++] = b + 1;
                }
            _sphere = new Mesh { name = "Orbit View Sphere", indexFormat = IndexFormat.UInt32 };
            _sphere.vertices = v; _sphere.normals = n; _sphere.uv = uv; _sphere.triangles = tri;
            _sphere.RecalculateTangents();
            _sphere.RecalculateBounds();
            return _sphere;
        }
    }
}
