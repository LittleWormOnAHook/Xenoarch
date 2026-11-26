using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Materials
{
    /// <summary>
    /// On each surface scene load: spawns the material nodes for that body (deterministic positions from the scene and
    /// material ids; nodesPerScene x XenoarchConfig.resourceNodeMultiplier per material, part of them around the landing
    /// zones and the rest spread over the body's surface map; clear of dig sites and zones, on walkable ground) and hides
    /// harvested ones until they respawn.
    /// </summary>
    public sealed class MaterialsDirector : MonoBehaviour
    {
        public const float MinRadius = 12f, MaxRadius = 90f;
        readonly List<ResourceNode> _nodes = new List<ResourceNode>();
        float _nextCheck;

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => Build(SceneManager.GetActiveScene());
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single || s == SceneManager.GetActiveScene()) Build(s); }

        void Build(Scene scene)
        {
            _nodes.RemoveAll(n => n == null);
            var session = XenoarchRuntime.Session;
            if (session == null || !scene.IsValid() || session.Config.galaxy == null) return;
            var body = session.Config.galaxy.FindBodyByScene(scene.name);
            if (body == null) return;
            foreach (var n in _nodes) if (n != null && n.gameObject.scene == scene) return; // already built
            var sites = session.Exploration.SitesInScene(scene.name);
            var ctx = new PlaceCtx { sites = sites, region = body.surfaceMapRect, nearShare = session.Config.resourceNodesNearZones };
            foreach (var r in scene.GetRootGameObjects()) { ctx.terrain = r.GetComponentInChildren<Terrain>(); if (ctx.terrain != null) break; }
            foreach (var z in FindObjectsByType<Exploration.LandingZone>()) if (z.gameObject.scene == scene) ctx.zones.Add(z);
            ctx.zones.Sort((a, b) => string.CompareOrdinal(a.siteId ?? "", b.siteId ?? ""));
            int made = 0;
            float mult = Mathf.Max(0f, session.Config.resourceNodeMultiplier);
            foreach (var item in session.Config.itemRegistry.Entries)
            {
                if (!(item is MaterialDefinition m) || m.refined || !m.bodies.Contains(body.Id)) continue;
                int count = Mathf.RoundToInt(m.nodesPerScene * mult);
                for (int i = 0; i < count; i++)
                {
                    string id = $"node.{scene.name}.{m.Id}.{i}";
                    if (!Place(id, i, ctx, out var pos)) continue;
                    ctx.placed.Add(pos);
                    var rng = new System.Random(StableHash(id) ^ 0x5bd1);
                    int units = rng.Next(m.unitsPerNode.x, Mathf.Max(m.unitsPerNode.x, m.unitsPerNode.y) + 1);
                    var node = ResourceNode.Spawn(m, id, units, pos, (float)rng.NextDouble() * 360f, scene);
                    node.SetAvailable(session.Materials.NodeAvailable(id, session.PlaySeconds, m.respawnSeconds));
                    _nodes.Add(node); made++;
                }
            }
            if (made > 0) Debug.Log($"[Xenoarch] Materials: {made} resource node(s) in {scene.name}");
        }

        void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 5f;
            var s = XenoarchRuntime.Session;
            if (s == null) return;
            foreach (var n in _nodes)
                if (n != null && n.Def != null && s.Materials.NodeAvailable(n.NodeId, s.PlaySeconds, n.Def.respawnSeconds)) n.SetAvailable(true);
        }

        sealed class PlaceCtx
        {
            public IEnumerable<DMI.Xenoarch.Galaxy.ArchaeologicalSiteDefinition> sites;
            public Rect region; public float nearShare; public Terrain terrain;
            public readonly List<Exploration.LandingZone> zones = new List<Exploration.LandingZone>();
            public readonly List<Vector3> placed = new List<Vector3>();
        }

        const float NodeSpacing = 8f, MaxNodeSlope = 32f, ZoneRingMin = 14f, ZoneRingMax = 120f;

        static bool Place(string id, int index, PlaceCtx c, out Vector3 pos)
        {
            var rng = new System.Random(StableHash(id));
            bool wide = c.region.width >= 50f;
            bool near = c.zones.Count > 0 && (!wide || rng.NextDouble() < c.nearShare);
            var anchor = c.zones.Count > 0 ? c.zones[index % c.zones.Count].transform.position : Vector3.zero;
            for (int tries = 0; tries < 24; tries++)
            {
                Vector3 p;
                if (near || !wide)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = near ? Mathf.Lerp(ZoneRingMin, ZoneRingMax, (float)rng.NextDouble()) : Mathf.Lerp(MinRadius, MaxRadius, (float)rng.NextDouble());
                    p = (near ? anchor : Vector3.zero) + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }
                else
                {
                    float m = Mathf.Min(15f, c.region.width * 0.05f);
                    p = new Vector3(Mathf.Lerp(c.region.xMin + m, c.region.xMax - m, (float)rng.NextDouble()), 0f,
                                    Mathf.Lerp(c.region.yMin + m, c.region.yMax - m, (float)rng.NextDouble()));
                }
                p.y = 0f;
                bool clear = true;
                foreach (var s in c.sites) { var d = s.position - p; d.y = 0f; if (d.magnitude < s.radius + 4f) { clear = false; break; } }
                if (clear) foreach (var z in c.zones) { var d = z.transform.position - p; d.y = 0f; if (d.magnitude < z.footprint + 4f) { clear = false; break; } }
                if (clear) foreach (var q in c.placed) { var d = q - p; d.y = 0f; if (d.sqrMagnitude < NodeSpacing * NodeSpacing) { clear = false; break; } }
                if (!clear) continue;
                if (c.terrain != null && c.terrain.terrainData != null)
                {
                    var tp = c.terrain.transform.position; var size = c.terrain.terrainData.size;
                    float u = (p.x - tp.x) / size.x, v = (p.z - tp.z) / size.z;
                    if (u < 0.01f || v < 0.01f || u > 0.99f || v > 0.99f) continue;
                    if (Vector3.Angle(c.terrain.terrainData.GetInterpolatedNormal(u, v), Vector3.up) > MaxNodeSlope) continue;
                }
                var h = ExcavationSite.TerrainHeight(p);
                if (h.HasValue) p.y = h.Value;
                else if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                else continue;
                pos = p;
                return true;
            }
            pos = default;
            return false;
        }

        public static int StableHash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }
    }
}
