using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// Scatters decorative rocks over a surface scene at runtime (deterministic per galaxy seed + body), under a new
    /// "[Xenoarch Rocks]" root that is never saved. Keeps clear of dig sites, landing zones, the base zone, the walking
    /// corridors between them, valuable caches, resource nodes, player spawns and any existing non-terrain collider,
    /// so placed scene objects are never touched. Data: Resources/World/RockScatterProfile.
    /// ZEN-25: size classes (Large / Medium / Small / Tiny; tiny ones have no collider and cull early) and slope / cliff
    /// clusters: steep ground is sampled, and a group of mixed sizes is put at the foot of a cliff (or on a slope), spread
    /// along it, seated and tilted to the ground normal and partly buried. GPU instancing is on in the rock materials.
    /// </summary>
    public sealed class RockScatterDirector : MonoBehaviour
    {
        public const string RootName = "[Xenoarch Rocks]";
        RockScatterProfile _profile;
        readonly Dictionary<(Material, Color), Material> _tinted = new Dictionary<(Material, Color), Material>();

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => StartCoroutine(BuildLater(SceneManager.GetActiveScene()));
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) StartCoroutine(BuildLater(s)); }

        IEnumerator BuildLater(Scene scene)
        {
            // Let landing zones, caches and resource nodes build first so the rocks can avoid them.
            yield return null; yield return null; yield return null;
            if (scene.IsValid() && scene.isLoaded) Build(scene);
        }

        /// <summary>Per-build state shared by the placement helpers.</summary>
        sealed class Ctx
        {
            public RockScatterProfile p; public RockScatterProfile.BodyRocks cfg; public System.Random rng;
            public Terrain terrain; public TerrainData td; public Vector3 tp, size;
            public List<Vector3> discs; public List<(Vector2, Vector2)> paths; public readonly List<Vector4> placed = new List<Vector4>();
            public Transform root; public List<RockScatterProfile.SizeClass> classes; public float classW, kindW, lightW;
            public int[] perClass;
        }

        void Build(Scene scene)
        {
            var session = XenoarchRuntime.Session;
            if (session == null || session.Config.galaxy == null) return;
            var body = session.Config.galaxy.FindBodyByScene(scene.name);
            if (body == null || body.surfaceMapRect.width < 50f) return;
            foreach (var r in scene.GetRootGameObjects()) if (r.name == RootName) return; // already built
            Terrain terrain = null;
            foreach (var r in scene.GetRootGameObjects()) { terrain = r.GetComponentInChildren<Terrain>(); if (terrain != null) break; }
            if (terrain == null || terrain.terrainData == null) return;
            if (_profile == null) _profile = Resources.Load<RockScatterProfile>(RockScatterProfile.ResourcePath);
            var p = _profile;
            if (p == null || p.rocks.Count == 0) return;
            var cfg = p.For(body.Id, body.mapColor);
            if (cfg.count <= 0 && cfg.slopeClusters <= 0) return;

            // Keep-out discs (x, z, radius) and corridors.
            var discs = new List<Vector3>();
            foreach (var s in session.Exploration.SitesInScene(scene.name)) discs.Add(new Vector3(s.position.x, s.position.z, s.radius * 1.42f + p.siteClearance));
            Vector3? basePos = null; var zonePos = new List<Vector3>();
            foreach (var z in FindObjectsByType<LandingZone>(FindObjectsInactive.Include))
            {
                if (z.gameObject.scene != scene) continue;
                var zp = z.transform.position;
                bool isBase = string.IsNullOrEmpty(z.siteId);
                discs.Add(new Vector3(zp.x, zp.z, z.footprint + (isBase ? p.baseClearance : p.zoneClearance)));
                if (isBase) basePos = zp; else zonePos.Add(zp);
            }
            if (body.hasBaseLanding) { basePos ??= body.baseLanding; discs.Add(new Vector3(body.baseLanding.x, body.baseLanding.z, 8f + p.baseClearance)); }
            foreach (var sp in FindObjectsByType<Player.PlayerSpawnPoint>(FindObjectsInactive.Include))
                if (sp.gameObject.scene == scene) discs.Add(new Vector3(sp.transform.position.x, sp.transform.position.z, 10f));
            var vd = Valuables.ValuableSiteDirector.Instance;
            if (vd != null) foreach (var v in vd.Sites) discs.Add(new Vector3(v.position.x, v.position.z, p.cacheClearance));
            foreach (var n in FindObjectsByType<Materials.ResourceNode>(FindObjectsInactive.Include))
                if (n.gameObject.scene == scene) discs.Add(new Vector3(n.transform.position.x, n.transform.position.z, p.nodeClearance));
            var paths = new List<(Vector2, Vector2)>();
            if (basePos.HasValue) foreach (var z in zonePos) paths.Add((new Vector2(basePos.Value.x, basePos.Value.z), new Vector2(z.x, z.z)));

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            int seed = (session.Config.galaxy.seed * 486187739) ^ Materials.MaterialsDirector.StableHash("rocks." + body.Id);
            var c = new Ctx
            {
                p = p, cfg = cfg, rng = new System.Random(seed), terrain = terrain, td = terrain.terrainData, tp = terrain.transform.position,
                size = terrain.terrainData.size, discs = discs, paths = paths, root = root.transform,
                classes = p.sizeClasses != null && p.sizeClasses.Count > 0 ? p.sizeClasses : RockScatterProfile.DefaultClasses(),
            };
            c.perClass = new int[c.classes.Count];
            foreach (var k in c.classes) c.classW += Mathf.Max(0f, k.weight);
            foreach (var k in p.rocks) if (k.prefab != null) { c.kindW += k.weight; if (!k.largeOnly) c.lightW += k.weight; }
            if (c.kindW <= 0f || c.classW <= 0f) return;
            var rect = body.surfaceMapRect;
            float margin = Mathf.Min(20f, rect.width * 0.04f);
            var rng = c.rng;
            float Rx() => Mathf.Lerp(rect.xMin + margin, rect.xMax - margin, (float)rng.NextDouble());
            float Rz() => Mathf.Lerp(rect.yMin + margin, rect.yMax - margin, (float)rng.NextDouble());

            // 1) Slope / cliff clusters.
            int clusters = 0, clusterRocks = 0;
            for (int t = 0; t < cfg.slopeClusters * 30 && clusters < cfg.slopeClusters; t++)
            {
                float x = Rx(), z = Rz();
                if (!Normal(c, x, z, out var nrm)) continue;
                float steep = Vector3.Angle(nrm, Vector3.up);
                if (steep < cfg.cliffSlope * 0.6f) continue;                   // needs a real slope
                var down = new Vector2(nrm.x, nrm.z); if (down.sqrMagnitude < 1e-6f) continue; down.Normalize();
                float cx = x, cz = z;
                if (steep > cfg.clusterMaxSlope * 0.8f)
                {
                    // Cliff: walk downhill to its foot.
                    bool foot = false;
                    for (int s = 0; s < 16; s++)
                    {
                        cx += down.x * 1.5f; cz += down.y * 1.5f;
                        if (!Normal(c, cx, cz, out var n2)) break;
                        if (Vector3.Angle(n2, Vector3.up) <= cfg.clusterMaxSlope * 0.6f) { foot = true; break; }
                    }
                    if (!foot) continue;
                }
                if (!Clear(c, cx, cz, 2f, 0f)) continue;
                var along = new Vector2(-down.y, down.x);
                int n = rng.Next(Mathf.Max(1, cfg.clusterRocks.x), Mathf.Max(cfg.clusterRocks.x, cfg.clusterRocks.y) + 1);
                int got = 0;
                for (int i = 0; i < n * 3 && got < n; i++)
                {
                    float sAlong = Gauss(rng) * 4f, sOut = (float)rng.NextDouble() * 3f;
                    float px = cx + along.x * sAlong + down.x * sOut, pz = cz + along.y * sAlong + down.y * sOut;
                    int cls = got == 0 ? (rng.NextDouble() < 0.35 ? 0 : 1) : PickClass(c, rng); // a big anchor rock, then mixed sizes
                    if (TryPlace(c, px, pz, cls, cfg.clusterMaxSlope, 0.15f)) got++;
                }
                if (got > 0) { clusters++; clusterRocks += got; }
            }

            // 2) Scattered rocks (with small groups around some of the big ones).
            int made = 0, attempts = 0;
            Vector2 groupCentre = default; int groupLeft = 0;
            while (made < cfg.count && attempts < cfg.count * 12)
            {
                attempts++;
                float x, z; int cls;
                if (groupLeft > 0) { float a = (float)rng.NextDouble() * Mathf.PI * 2f, rr = 1.5f + (float)rng.NextDouble() * 4f; x = groupCentre.x + Mathf.Cos(a) * rr; z = groupCentre.y + Mathf.Sin(a) * rr; cls = Mathf.Max(2, PickClass(c, rng)); }
                else { x = Rx(); z = Rz(); cls = PickClass(c, rng); }
                bool grouped = groupLeft > 0;
                if (grouped) groupLeft--;
                if (!TryPlace(c, x, z, cls, cfg.maxSlope, grouped ? 0.2f : 1f)) continue;
                made++;
                if (!grouped && cls <= 1 && rng.NextDouble() < cfg.clusterShare) { groupCentre = new Vector2(x, z); groupLeft = 2 + rng.Next(3); }
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < c.classes.Count; i++) sb.Append(i > 0 ? ", " : "").Append(c.classes[i].name).Append(' ').Append(c.perClass[i]);
            Debug.Log($"[Xenoarch] Rocks: {made + clusterRocks} decorative rock(s) on {body.displayName}: {made} scattered, {clusterRocks} in {clusters} slope/cliff clusters ({sb})");
        }

        static float Gauss(System.Random r) => (float)(System.Math.Sqrt(-2.0 * System.Math.Log(1.0 - r.NextDouble())) * System.Math.Cos(2.0 * System.Math.PI * r.NextDouble())) * 0.6f;

        static bool Normal(Ctx c, float x, float z, out Vector3 nrm)
        {
            float u = (x - c.tp.x) / c.size.x, v = (z - c.tp.z) / c.size.z;
            nrm = Vector3.up;
            if (u < 0.01f || v < 0.01f || u > 0.99f || v > 0.99f) return false;
            nrm = c.td.GetInterpolatedNormal(u, v);
            return true;
        }

        static int PickClass(Ctx c, System.Random rng)
        {
            float r = (float)rng.NextDouble() * c.classW;
            for (int i = 0; i < c.classes.Count; i++) { r -= Mathf.Max(0f, c.classes[i].weight); if (r <= 0f) return i; }
            return c.classes.Count - 1;
        }

        bool TryPlace(Ctx c, float x, float z, int cls, float maxSlope, float spacingScale)
        {
            var p = c.p; var k = c.classes[Mathf.Clamp(cls, 0, c.classes.Count - 1)];
            var kind = Pick(p.rocks, k.allowLargeModels ? c.kindW : c.lightW, k.allowLargeModels, c.rng);
            if (kind == null) return false;
            float sz = Mathf.Lerp(k.size.x, k.size.y, Mathf.Pow((float)c.rng.NextDouble(), 1.4f)) * c.cfg.sizeMultiplier;
            float rad = sz * 0.6f;
            if (!Clear(c, x, z, rad, p.rockSpacing * spacingScale * Mathf.Clamp(sz / 1.5f, 0.15f, 1f))) return false;
            if (!Normal(c, x, z, out var nrm)) return false;
            if (Vector3.Angle(nrm, Vector3.up) > maxSlope) return false;
            float y = c.terrain.SampleHeight(new Vector3(x, 0f, z)) + c.tp.y;
            if (Blocked(new Vector3(x, y + sz * 0.5f, z), rad, c.terrain)) return false;
            var rng = c.rng;
            // Seat on the ground: tilt most of the way to the normal, random yaw, a little wobble, partly buried.
            var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, nrm, 0.85f)) * Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 16f - 8f);
            float sink = k.sink + (float)rng.NextDouble() * 0.1f;
            var go = Instantiate(kind.prefab, new Vector3(x, y - sz * sink, z), rot, c.root);
            go.transform.localScale = Vector3.one * sz * (0.85f + (float)rng.NextDouble() * 0.3f);
            Tint(go, c.cfg.tint);
            if (!k.collider) foreach (var col in go.GetComponentsInChildren<Collider>()) DestroyImmediate(col);
            var lod = go.GetComponent<LODGroup>();
            if (lod != null) { var l = lod.GetLODs(); if (l.Length > 0) { l[l.Length - 1].screenRelativeTransitionHeight = Mathf.Min(k.cullScreenSize, l.Length > 1 ? l[l.Length - 2].screenRelativeTransitionHeight * 0.9f : 1f); lod.SetLODs(l); lod.RecalculateBounds(); } }
            c.placed.Add(new Vector4(x, z, rad));
            c.perClass[Mathf.Clamp(cls, 0, c.perClass.Length - 1)]++;
            return true;
        }

        static RockScatterProfile.RockKind Pick(List<RockScatterProfile.RockKind> kinds, float total, bool allowLarge, System.Random rng)
        {
            float r = (float)rng.NextDouble() * total;
            foreach (var k in kinds) { if (k.prefab == null || (k.largeOnly && !allowLarge)) continue; r -= k.weight; if (r <= 0f) return k; }
            foreach (var k in kinds) if (k.prefab != null && (!k.largeOnly || allowLarge)) return k;
            return null;
        }

        static bool Clear(Ctx c, float x, float z, float rad, float spacing)
        {
            foreach (var d in c.discs) { float dx = d.x - x, dz = d.y - z, r = d.z + rad; if (dx * dx + dz * dz < r * r) return false; }
            var q = new Vector2(x, z);
            foreach (var (a, b) in c.paths) if (DistToSegment(q, a, b) < c.p.pathHalfWidth + rad) return false;
            foreach (var o in c.placed) { float dx = o.x - x, dz = o.y - z, r = o.z + rad + spacing * 0.5f; if (dx * dx + dz * dz < r * r) return false; }
            return true;
        }

        static float DistToSegment(Vector2 q, Vector2 a, Vector2 b)
        {
            var ab = b - a; float len = ab.sqrMagnitude;
            float t = len < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / len);
            return Vector2.Distance(q, a + ab * t);
        }

        static readonly Collider[] _hits = new Collider[16];
        static bool Blocked(Vector3 c, float r, Terrain terrain)
        {
            int n = Physics.OverlapSphereNonAlloc(c, r + 0.5f, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h == null || h is TerrainCollider) continue;
                if (h.GetComponentInParent<Terrain>() == terrain) continue;
                if (h.transform.root.name == RootName) continue;   // other rocks: spacing already handled
                return true;
            }
            return false;
        }

        void Tint(GameObject go, Color tint)
        {
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    if (!_tinted.TryGetValue((m, tint), out var t))
                    {
                        t = new Material(m) { name = m.name + " (tinted)", enableInstancing = true };
                        if (t.HasProperty("_BaseColor")) t.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                        _tinted[(m, tint)] = t;
                    }
                    mats[i] = t;
                }
                mr.sharedMaterials = mats;
            }
        }
    }
}