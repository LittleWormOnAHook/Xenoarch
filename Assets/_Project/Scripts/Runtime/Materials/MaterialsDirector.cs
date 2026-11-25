using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Materials
{
    /// <summary>
    /// On each surface scene load: spawns the material nodes for that body (deterministic positions from the scene and
    /// material ids, 12-90 m around the origin, clear of dig sites, snapped to the ground) and hides harvested ones until
    /// they respawn.
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
            int made = 0;
            foreach (var item in session.Config.itemRegistry.Entries)
            {
                if (!(item is MaterialDefinition m) || m.refined || !m.bodies.Contains(body.Id)) continue;
                for (int i = 0; i < m.nodesPerScene; i++)
                {
                    string id = $"node.{scene.name}.{m.Id}.{i}";
                    if (!Place(id, sites, out var pos)) continue;
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

        static bool Place(string id, IEnumerable<DMI.Xenoarch.Galaxy.ArchaeologicalSiteDefinition> sites, out Vector3 pos)
        {
            var rng = new System.Random(StableHash(id));
            for (int tries = 0; tries < 12; tries++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Lerp(MinRadius, MaxRadius, (float)rng.NextDouble());
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                bool clear = true;
                foreach (var s in sites) { var d = s.position - p; d.y = 0f; if (d.magnitude < s.radius + 4f) { clear = false; break; } }
                if (!clear) continue;
                var h = ExcavationSite.TerrainHeight(p);
                if (h.HasValue) p.y = h.Value;
                else if (Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                pos = p;
                return true;
            }
            pos = default;
            return false;
        }

        public static int StableHash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }
    }
}
