using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using DMI.Xenoarch.Inventory;

namespace DMI.Xenoarch.Valuables
{
    /// <summary>
    /// Random valuable caches on planets: generated per landing region (deterministic), invisible to the orbital
    /// scanner, revealed by a handheld-scanner pulse within its (upgradable) radius. A revealed cache becomes a small
    /// dig spot (gold border); digging down to it rolls the loot table (common .. super rare) into the inventory.
    /// </summary>
    public sealed class ValuableSiteDirector : MonoBehaviour
    {
        public static ValuableSiteDirector Instance { get; private set; }
        public readonly List<ValuableSite> Sites = new List<ValuableSite>();
        readonly Dictionary<string, ExcavationSite> _digs = new Dictionary<string, ExcavationSite>();
        Scene _scene; CelestialBodyDefinition _body; float _nextPoll, _fullToastAt;

        void Awake() => Instance = this;
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() { var s = SceneManager.GetActiveScene(); if (_scene != s) Setup(s); }
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Setup(s); }

        void Setup(Scene scene)
        {
            _scene = scene; Sites.Clear(); _digs.Clear(); _body = null;
            var session = XenoarchRuntime.Session;
            if (session == null || !scene.IsValid()) return;
            var x = session.Exploration;
            var body = x.CurrentBody;
            if (body == null || body.sceneName != scene.name) body = x.Galaxy != null ? x.Galaxy.FindBodyByScene(scene.name) : null;
            if (!ShipTravel.UsesShuttle(body) || body.surfaceMapRect.width < 10f) return;
            Terrain terrain = null;
            foreach (var r in scene.GetRootGameObjects()) { terrain = r.GetComponentInChildren<Terrain>(); if (terrain != null) break; }
            if (terrain == null) return;
            _body = body;
            var avoid = new List<(Vector3, float)>();
            foreach (var s in x.SitesInScene(scene.name)) avoid.Add((s.position, s.radius * 1.42f));
            var zones = new List<Vector3>();
            foreach (var z in FindObjectsByType<LandingZone>()) if (z.gameObject.scene == scene) { avoid.Add((z.transform.position, z.footprint)); zones.Add(z.transform.position); }
            zones.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z)); // deterministic order
            Sites.AddRange(ValuableService.Generate(body.Id, x.Galaxy != null ? x.Galaxy.seed : 0, body.surfaceMapRect, session.Config.valuableSites, LandingZonePicker.ForTerrain(terrain), avoid, zones));
            int revealed = 0;
            foreach (var v in Sites) if (session.Valuables.IsRevealed(v.id)) { MakeDig(v); revealed++; }
            Debug.Log($"[Xenoarch] Valuables: {Sites.Count} hidden cache(s) on {body.displayName}, {revealed} already found");
        }

        public bool TryFind(string id, out ValuableSite site)
        {
            foreach (var v in Sites) if (v.id == id) { site = v; return true; }
            site = default; return false;
        }

        /// <summary>Nearest cache within range that is not collected yet (revealed or not).</summary>
        public bool Nearest(Vector3 from, float range, bool revealedOnly, out ValuableSite best, out float dist)
        {
            best = default; dist = range; bool any = false;
            var vs = XenoarchRuntime.Session?.Valuables;
            if (vs == null) return false;
            foreach (var v in Sites)
            {
                if (vs.IsCollected(v.id) || (revealedOnly && !vs.IsRevealed(v.id))) continue;
                var f = v.position - from; f.y = 0f; float d = f.magnitude;
                if (d <= dist) { dist = d; best = v; any = true; }
            }
            return any;
        }

        public void Reveal(ValuableSite v)
        {
            var vs = XenoarchRuntime.Session?.Valuables;
            if (vs == null || vs.IsRevealed(v.id)) return;
            vs.Reveal(v.id);
            MakeDig(v);
        }

        void MakeDig(ValuableSite v)
        {
            if (_digs.ContainsKey(v.id) || _body == null) return;
            var def = ScriptableObject.CreateInstance<ArchaeologicalSiteDefinition>();
            def.SetIdForTooling(v.id);
            def.name = v.id; def.displayName = "Valuable cache"; def.bodyId = _body.Id; def.sceneName = _scene.name;
            def.position = v.position; def.radius = 0.9f; def.depth = v.depth + 0.5f; def.cellSize = 0.25f; def.seed = v.seed;
            var dig = ExcavationSite.Create(def, ExcavationSite.TerrainHeight(v.position) ?? v.position.y, _scene);
            dig.IsCache = true;
            bool collected = XenoarchRuntime.Session.Valuables.IsCollected(v.id);
            dig.ShowBorder(!collected);
            _digs[v.id] = dig;
            var rig = PlayerRigLocator.Find();
            if (rig != null && rig.Root.GetComponent<DigTool>() == null) rig.Root.gameObject.AddComponent<DigTool>();
            if (rig != null) DMI.Xenoarch.Excavation.DepthProbeTool.Ensure(rig.Root);
        }

        void Update()
        {
            if (_digs.Count == 0 || Time.time < _nextPoll) return;
            _nextPoll = Time.time + 0.25f;
            var session = XenoarchRuntime.Session;
            if (session == null) return;
            foreach (var kv in _digs)
            {
                var dig = kv.Value;
                if (dig == null || dig.Grid == null || session.Valuables.IsCollected(kv.Key) || !TryFind(kv.Key, out var v)) continue;
                int cx = dig.Grid.nx / 2, cz = dig.Grid.nz / 2;
                float dug = 0f;
                for (int dx = -1; dx <= 0; dx++) for (int dz = -1; dz <= 0; dz++) if (dig.Grid.InBounds(cx + dx, cz + dz)) dug = Mathf.Max(dug, dig.Grid.TopDepth(cx + dx, cz + dz));
                if (dug + dig.Grid.cell * 0.5f >= v.depth) { Award(v, dig); break; }
            }
        }

        void Award(ValuableSite v, ExcavationSite dig)
        {
            var session = XenoarchRuntime.Session;
            var table = session.Config.valuableLoot;
            var rng = new System.Random(v.seed);
            var tier = table != null ? table.RollTier(rng) : LootTier.Common;
            var loot = table != null ? table.Roll(tier, rng) : new List<(string, int)>();
            if (session.Inventory.FreeSlots < loot.Count)
            {
                if (Time.time > _fullToastAt)
                {
                    _fullToastAt = Time.time + 6f;
                    var first = loot.Count > 0 ? loot[0] : ("", 0);
                    OverflowPrompt.OfferRoute(new ItemStack(string.IsNullOrEmpty(first.Item1) ? "material.regolith" : first.Item1, Mathf.Max(1, first.Item2)),
                        "Valuable cache found, but the backpack is full. Send the cache to the ship, or stash it in a nearby shuttle or rover.",
                        dest => AwardTo(v, dig, loot, tier, dest.Inventory, dest.Id));
                }
                return;
            }
            var names = new List<string>();
            foreach (var (id, n) in loot)
            {
                int added = n - session.Inventory.Add(id, n); // Add returns what did NOT fit
                string label = session.Items != null && session.Items.TryGetItem(id, out var def) && def != null ? def.DisplayNameOrId : id;
                names.Add($"{label} x{added}");
            }
            session.Valuables.Collect(v.id, tier);
            dig.ShowBorder(false);
            Toast.Show($"{TierName(tier).ToUpperInvariant()} cache: {string.Join(", ", names)}", 7f);
            Debug.Log($"[Xenoarch] Valuables: {v.id} collected ({tier}): {string.Join(", ", names)}");
        }

        void AwardTo(ValuableSite v, ExcavationSite dig, List<(string, int)> loot, LootTier tier, InventoryModel dest, string destId)
        {
            var session = XenoarchRuntime.Session;
            if (session == null || dest == null || string.IsNullOrEmpty(v.id)) return;
            if (session.Valuables.IsCollected(v.id)) return;
            foreach (var (id, n) in loot)
            {
                session.Items.TryGetItem(id, out var def);
                int can = 0;
                if (def != null && def.IsStackable)
                {
                    foreach (var o in dest.Slots)
                        if (!o.IsEmpty && o.itemId == id) can += Mathf.Max(0, def.EffectiveMaxStack - o.count);
                }
                int room = can + dest.FreeSlots * (def != null ? def.EffectiveMaxStack : n);
                if (room < n) { Toast.Show("That storage cannot take the whole cache. Make room and dig it out again."); return; }
            }
            var labels = new List<string>();
            foreach (var (id, n) in loot)
            {
                int left = dest.Add(id, n);
                int added = n - left;
                string label = session.Items != null && session.Items.TryGetItem(id, out var def) && def != null ? def.DisplayNameOrId : id;
                labels.Add($"{label} x{added}");
            }
            session.Valuables.Collect(v.id, tier);
            if (dig != null) dig.ShowBorder(false);
            string where = destId == "ship" ? "ship storage" : destId;
            Toast.Show($"{TierName(tier).ToUpperInvariant()} cache sent to the {where}: {string.Join(", ", labels)}", 7f);
            Debug.Log($"[Xenoarch] Valuables: {v.id} collected to {where} ({tier}): {string.Join(", ", labels)}");
        }

        public static string TierName(LootTier t) => t == LootTier.SuperRare ? "Super rare" : t.ToString();
    }
}
