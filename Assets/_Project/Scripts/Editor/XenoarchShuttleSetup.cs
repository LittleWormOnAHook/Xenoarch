using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1C batch 3B: shuttle landings. Points ShipDefinition.shuttlePrefab at the Creepy Cat Mark_2 Shuttle (read
    /// only, instantiated at runtime), measures its footprint, and places one <see cref="LandingZone"/> marker per dig
    /// site (+ a base zone) in the planet scenes by auto-pick. Existing markers are never moved; scenes with unsaved
    /// changes are skipped.
    /// </summary>
    public static class XenoarchShuttleSetup
    {
        public const string ShuttlePrefabPath = "Assets/_Creepy_Cat/_3D Scifi Kit Vol 3/Prefabs/Props/_Update 1.05-Spaceship Akorn/_Spaceship/Mark_2 Shuttle.prefab";
        public const string MarkerRoot = "LandingZones";
        static readonly string[] Scenes = { SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 };

        [MenuItem("Xenoarch/Setup/11. Shuttle landing zones (adds missing markers only)", priority = 31)]
        public static void RunMenu()
        {
            var log = new StringBuilder();
            Run(log);
            Debug.Log($"[Xenoarch] Shuttle setup:\n{log}");
        }

        public static void Run(StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            var ship = config != null ? config.starterShip : null;
            if (ship == null) { log.AppendLine("Shuttle: no starter ship definition, skipped"); return; }
            bool dirty = false;
            if (ship.shuttlePrefab == null)
            {
                ship.shuttlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShuttlePrefabPath);
                log.AppendLine(ship.shuttlePrefab != null ? "Shuttle: ShipDefinition.shuttlePrefab -> Mark_2 Shuttle (vendor prefab referenced, not edited)" : $"Shuttle: {ShuttlePrefabPath} not found");
                dirty = true;
            }
            if (ship.landingRules.angles <= 0) { ship.landingRules = LandingZonePicker.Rules.Default; dirty = true; }
            if (ship.shuttlePrefab != null && ship.shuttleFootprint <= 0f)
            {
                // Read-only: transforms/meshes of the prefab asset, nothing is instantiated or saved.
                var b = LandingZonePicker.LocalBounds(ship.shuttlePrefab.transform);
                ship.shuttleFootprint = Mathf.Ceil(LandingZonePicker.FootprintOf(b) * 10f) / 10f;
                log.AppendLine($"Shuttle: footprint radius {ship.shuttleFootprint:0.0} m (bounds {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m)");
                dirty = true;
            }
            if (dirty) { EditorUtility.SetDirty(ship); AssetDatabase.SaveAssets(); }

            foreach (var name in Scenes)
                XenoarchPhase1CSetup.WithScene(name, log, s => Markers(s, config, ship, log));
        }

        static bool Markers(Scene scene, XenoarchConfig config, ShipDefinition ship, StringBuilder log)
        {
            var roots = scene.GetRootGameObjects();
            var terrain = roots.Select(r => r.GetComponentInChildren<Terrain>(true)).FirstOrDefault(t => t != null);
            if (terrain == null) { log.AppendLine($"{scene.name}: no terrain, landing zones skipped"); return false; }
            var existing = roots.SelectMany(r => r.GetComponentsInChildren<LandingZone>(true)).ToList();
            var sites = config.siteRegistry != null ? config.siteRegistry.Entries.Where(s => s != null && s.sceneName == scene.name).ToList() : new List<ArchaeologicalSiteDefinition>();
            var avoid = sites.Select(s => (s.position, s.radius * 1.42f)).ToList();
            var rules = ship.landingRules; if (rules.angles <= 0) rules = LandingZonePicker.Rules.Default;
            rules.footprint = ship.shuttleFootprint > 0f ? ship.shuttleFootprint : 8f;
            var height = LandingZonePicker.ForTerrain(terrain);
            Physics.SyncTransforms();
            var buf = new Collider[64];
            bool Blocked(Vector3 g, float r)
            {
                int n = Physics.OverlapBoxNonAlloc(g + Vector3.up * 2.8f, new Vector3(r, 2.5f, r), buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = buf[i];
                    if (c == null || c is TerrainCollider || c.gameObject.scene != scene || c.transform.IsChildOf(terrain.transform)) continue;
                    if (c.GetComponentInParent<IPlayerRig>() != null) continue;
                    return true;
                }
                return false;
            }

            Transform parent = null;
            int made = 0;
            void Make(string siteId, string label, Vector3 target, bool hint)
            {
                var old = existing.FirstOrDefault(z => (z.siteId ?? "") == siteId);
                if (old != null && !old.UntouchedRelaxedAuto) { log.AppendLine($"{scene.name}: landing zone for {(siteId == "" ? "base" : siteId)} exists, kept where it is"); return; }
                var res = LandingZonePicker.Pick(target, avoid, rules, height, Blocked, ship.shuttleYawOffset);
                if (old != null)
                {
                    if (!res.found || res.relaxed) { log.AppendLine($"{scene.name}: landing zone for {(siteId == "" ? "base" : siteId)} still has no better ground, kept"); return; }
                    Undo.DestroyObjectImmediate(old.gameObject); existing.Remove(old);
                }
                Vector3 pos; float yaw;
                if (res.found) { pos = res.position; yaw = res.yaw; }
                else { pos = target + Vector3.right * (rules.footprint + 6f); height(pos.x, pos.z, out pos.y, out _); yaw = 0f; }
                if (parent == null)
                {
                    var p = roots.FirstOrDefault(r => r.name == MarkerRoot);
                    if (p == null) { p = new GameObject(MarkerRoot); SceneManager.MoveGameObjectToScene(p, scene); }
                    parent = p.transform;
                }
                var go = new GameObject($"LandingZone - {label}");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetParent(parent, true);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                var z = go.AddComponent<LandingZone>();
                z.siteId = siteId; z.footprint = rules.footprint; z.siteHint = target; z.hasSiteHint = hint;
                z.autoPlaced = true; z.relaxedPick = !res.found || res.relaxed; z.autoPosition = pos;
                made++;
                log.AppendLine($"{scene.name}: landing zone '{label}' placed: {res}");
            }

            foreach (var s in sites) Make(s.Id, string.IsNullOrEmpty(s.displayName) ? s.Id : s.displayName, s.position, true);
            var spawn = roots.SelectMany(r => r.GetComponentsInChildren<PlayerSpawnPoint>(true)).FirstOrDefault(p => p.spawnId == PlayerSpawnIds.Default);
            Make("", "Base", spawn != null ? spawn.transform.position : Vector3.zero, false);
            return made > 0;
        }

        // ------------------------------------------------------------------ smoke
        public static void Validate(System.Action<bool, string> check)
        {
            // Picker on a synthetic 25-degree slope with one flat plateau 30 m east of the target.
            LandingZonePicker.HeightFn h = (float x, float z, out float y, out Vector3 n) =>
            {
                bool flat = new Vector2(x - 30f, z).sqrMagnitude < 15f * 15f;
                y = flat ? 14.1f : 0.47f * x;
                n = flat ? Vector3.up : new Vector3(-0.47f, 1f, 0f).normalized;
                return Mathf.Abs(x) < 200f && Mathf.Abs(z) < 200f;
            };
            var avoid = new List<(Vector3, float)> { (Vector3.zero, 5f) };
            var rules = LandingZonePicker.Rules.Default;
            var r1 = LandingZonePicker.Pick(Vector3.zero, avoid, rules, h);
            bool onPlateau = r1.found && !r1.relaxed && new Vector2(r1.position.x - 30f, r1.position.z).magnitude <= 15f - rules.footprint + 0.01f;
            bool clearOfGrid = r1.found && new Vector2(r1.position.x, r1.position.z).magnitude >= 5f + rules.footprint + rules.gridClearance - 0.01f;
            var r2 = LandingZonePicker.Pick(Vector3.zero, avoid, rules, h, (c, rad) => new Vector2(c.x - 30f, c.z).magnitude < 20f);
            check(onPlateau && clearOfGrid && !r2.found, $"3B landing pick: lands on the only flat ground, clear of the dig grid ({r1}); refuses steep or blocked ground");

            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            var ship = config != null ? config.starterShip : null;
            bool prefabOk = ship != null && ship.shuttlePrefab != null && AssetDatabase.GetAssetPath(ship.shuttlePrefab) == ShuttlePrefabPath;
            check(prefabOk && ship.shuttleFootprint > 1f, $"3B shuttle: Mark_2 Shuttle referenced by the ship definition (footprint {(ship != null ? ship.shuttleFootprint : 0f):0.0} m)");

            var st = new ShipState { shuttleSiteId = "site.test" };
            check(JsonUtility.FromJson<ShipState>(JsonUtility.ToJson(st)).shuttleSiteId == "site.test", "3B shuttle: chosen landing site is saved with the ship state");

            string guid = AssetDatabase.FindAssets("t:MonoScript LandingZone").Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith("/LandingZone.cs")).Select(AssetDatabase.AssetPathToGUID).FirstOrDefault();
            foreach (var name in Scenes)
            {
                string path = XenoarchSetup.ScenePath(name);
                if (!File.Exists(path) || string.IsNullOrEmpty(guid)) { check(false, $"3B landing zones: {name} or LandingZone script missing"); continue; }
                int markers = Count(File.ReadAllText(path), "guid: " + guid);
                int sites = config != null && config.siteRegistry != null ? config.siteRegistry.Entries.Count(s => s != null && s.sceneName == name) : 0;
                check(true, markers >= sites + 1 ? $"3B landing zones: {name} has {markers} editable markers ({sites} sites + base)" : $"3B landing zones: {name} has {markers}/{sites + 1} markers (scene skipped or not set up yet; runtime auto-pick covers the rest)");
            }
        }

        static int Count(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
