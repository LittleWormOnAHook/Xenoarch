using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Aboard the ship: shows the orbited planet outside the window when the body (or, with no body, the system on
    /// arrival) has an <see cref="OrbitViewDefinition"/> and no painted orbit skybox. A lat-long UV sphere with the
    /// PlanetPack02-derived URP materials, sized to the definition's apparent diameter, lit by the sun light. Static
    /// for now (the ship does not move); real orbits / travel can move it later. Rebuilt on scene load and FTL arrival.
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

        void Build()
        {
            if (_root != null) Destroy(_root);
            _root = null; _planet = null; _def = null;
            var scene = SceneManager.GetActiveScene();
            var session = XenoarchRuntime.Session;
            if (session == null || scene.name != SceneNames.ShipStarter || session.Exploration == null) return;
            var def = Resolve(session.Exploration.CurrentBody, session.Exploration.CurrentSystem, session.Config == null || session.Config.preferOrbitViews);
            if (def == null || def.planetMaterial == null) return;
            _def = def;

            Vector3 w = Vector3.forward, eye = Vector3.zero;
            var helm = FindAnyObjectByType<Ship.CockpitHelm>();
            if (helm != null) { eye = helm.transform.position; if (helm.windowDirection.sqrMagnitude > 0.01f) w = helm.windowDirection; }
            w.y = 0f; if (w.sqrMagnitude < 0.01f) w = Vector3.forward; w.Normalize();
            Vector3 dir = Quaternion.LookRotation(w, Vector3.up) * Quaternion.Euler(-def.windowOffsetDeg.y, def.windowOffsetDeg.x, 0f) * Vector3.forward;

            var cam = Camera.main;
            float far = cam != null ? cam.farClipPlane : 1000f;
            float radius0 = Mathf.Sin(def.apparentDiameterDeg * 0.5f * Mathf.Deg2Rad);
            // centre distance so the far side of the atmosphere stays inside the far clip plane
            float dist = Mathf.Min(6000f, far * 0.85f / (1f + radius0 * def.atmosphereScale));
            float radius = dist * radius0;

            _root = new GameObject("[Orbit View]");
            SceneManager.MoveGameObjectToScene(_root, scene);
            _root.transform.position = eye + dir * dist;
            _root.transform.rotation = Quaternion.Euler(0f, 0f, def.tiltDeg);

            _planet = MakeSphere("Planet", def.planetMaterial, radius);
            if (def.atmosphereMaterial != null) MakeSphere("Atmosphere", def.atmosphereMaterial, radius * def.atmosphereScale);
            Debug.Log($"[Xenoarch] Orbit view: {def.name} ({def.apparentDiameterDeg:0}° at {dist:0} m)");
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
            if (_root == null || _def == null || _def.spinDegPerSec == 0f) return;
            foreach (Transform t in _root.transform) t.Rotate(0f, -_def.spinDegPerSec * Time.deltaTime, 0f, Space.Self);
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
