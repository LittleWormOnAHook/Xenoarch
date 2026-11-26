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
            if (cfg.count <= 0) return;

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
            var rng = new System.Random(seed);
            var placed = new List<Vector4>(); // x, z, radius
            var td = terrain.terrainData; var tp = terrain.transform.position; var size = td.size;
            var rect = body.surfaceMapRect;
            float margin = Mathf.Min(20f, rect.width * 0.04f);
            float totalW = 0f; foreach (var k in p.rocks) if (k.prefab != null) totalW += k.weight;
            if (totalW <= 0f) return;
            int made = 0, attempts = 0;
            Vector2 clusterCentre = default; int clusterLeft = 0;
            while (made < cfg.count && attempts < cfg.count * 12)
            {
                attempts++;
                float x, z;
                if (clusterLeft > 0) { float a = (float)rng.NextDouble() * Mathf.PI * 2f, rr = 2f + (float)rng.NextDouble() * 5f; x = clusterCentre.x + Mathf.Cos(a) * rr; z = clusterCentre.y + Mathf.Sin(a) * rr; }
                else { x = Mathf.Lerp(rect.xMin + margin, rect.xMax - margin, (float)rng.NextDouble()); z = Mathf.Lerp(rect.yMin + margin, rect.yMax - margin, (float)rng.NextDouble()); }
                var kind = Pick(p.rocks, totalW, rng);
                float sz = Mathf.Lerp(kind.size.x, kind.size.y, Mathf.Pow((float)rng.NextDouble(), 1.8f)) * cfg.sizeMultiplier; // mostly small
                if (clusterLeft > 0) { sz *= 0.6f; clusterLeft--; }
                float rad = sz * 0.6f;
                if (!Clear(x, z, rad, discs, paths, placed, p)) continue;
                float u = (x - tp.x) / size.x, v = (z - tp.z) / size.z;
                if (u < 0.01f || v < 0.01f || u > 0.99f || v > 0.99f) continue;
                var nrm = td.GetInterpolatedNormal(u, v);
                if (Vector3.Angle(nrm, Vector3.up) > cfg.maxSlope) continue;
                float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + tp.y;
                var probe = new Vector3(x, y + sz * 0.5f, z);
                if (Blocked(probe, rad, terrain)) continue;
                var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, nrm, 0.6f)) * Quaternion.Euler((float)rng.NextDouble() * 14f - 7f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 14f - 7f);
                var go = Instantiate(kind.prefab, new Vector3(x, y - sz * p.sink, z), rot, root.transform);
                go.transform.localScale = Vector3.one * sz * (0.85f + (float)rng.NextDouble() * 0.3f);
                Tint(go, cfg.tint);
                var lod = go.GetComponent<LODGroup>();
                if (lod != null) { var l = lod.GetLODs(); if (l.Length > 0) { l[l.Length - 1].screenRelativeTransitionHeight = p.cullScreenSize; lod.SetLODs(l); lod.RecalculateBounds(); } }
                placed.Add(new Vector4(x, z, rad));
                made++;
                if (clusterLeft == 0 && sz > 1.2f && rng.NextDouble() < cfg.clusterShare) { clusterCentre = new Vector2(x, z); clusterLeft = 2 + rng.Next(3); }
            }
            Debug.Log($"[Xenoarch] Rocks: {made} decorative rock(s) on {body.displayName}");
        }

        static RockScatterProfile.RockKind Pick(List<RockScatterProfile.RockKind> kinds, float total, System.Random rng)
        {
            float r = (float)rng.NextDouble() * total;
            foreach (var k in kinds) { if (k.prefab == null) continue; r -= k.weight; if (r <= 0f) return k; }
            foreach (var k in kinds) if (k.prefab != null) return k;
            return null;
        }

        static bool Clear(float x, float z, float rad, List<Vector3> discs, List<(Vector2, Vector2)> paths, List<Vector4> placed, RockScatterProfile p)
        {
            foreach (var d in discs) { float dx = d.x - x, dz = d.y - z, r = d.z + rad; if (dx * dx + dz * dz < r * r) return false; }
            var q = new Vector2(x, z);
            foreach (var (a, b) in paths) if (DistToSegment(q, a, b) < p.pathHalfWidth + rad) return false;
            foreach (var o in placed) { float dx = o.x - x, dz = o.y - z, r = o.z + rad + p.rockSpacing * 0.5f; if (dx * dx + dz * dz < r * r) return false; }
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
            for (int i = 0; i < n; i++) { var h = _hits[i]; if (h == null || h is TerrainCollider) continue; if (h.GetComponentInParent<Terrain>() == terrain) continue; return true; }
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
                        t = new Material(m) { name = m.name + " (tinted)" };
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
