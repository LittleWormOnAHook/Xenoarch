using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1B polish, additive. Removes the floating setup labels (TextMesh objects named "Label") from all six scenes;
    /// in Ship_Starter puts the ship console (CockpitHelm) on Anthony's P_Cockpit_Tech_02 and a CockpitSeat on both
    /// P_Chair_07 (StandMode ship), and points the ship Volume at the recreated Creepy Cat profile. Components only
    /// (added-component overrides on the vendor prefab instance; a trigger BoxCollider only where the prop has no
    /// collider). Never moves objects, never edits vendor prefabs. A scene with unsaved changes is skipped, not saved.
    /// </summary>
    public static class XenoarchPolishPass
    {
        public const string HelmHost = "P_Cockpit_Tech_02";
        public const string SeatHost = "P_Chair_07";

        [MenuItem("Xenoarch/Setup/9. Polish: remove world labels, cockpit helm (FTL), ship PP (additive)", priority = 29)]
        public static void Run()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Xenoarch] Polish: exit Play Mode first"); return; }
            XenoarchPhase1BSetup.EnsureCreepyCatVolumes();
            foreach (var name in SceneNames.All) Process(name, log);
            Debug.Log($"[Xenoarch] Polish pass:\n{log}");
            System.IO.File.WriteAllText("Logs/xenoarch_polish.txt", log.ToString());
        }

        static void Process(string name, StringBuilder log)
        {
            string path = XenoarchSetup.ScenePath(name);
            if (!System.IO.File.Exists(path)) return;
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = false;
            if (scene.IsValid() && scene.isLoaded)
            {
                if (scene.isDirty) { log.AppendLine($"{name}: has unsaved changes, SKIPPED (save it and re-run)"); return; }
            }
            else { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true; }
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
                int labels = 0;
                foreach (var g in all)
                {
                    if (g == null || g.name != "Label" || g.GetComponent<TextMesh>() == null || PrefabUtility.IsPartOfPrefabInstance(g)) continue;
                    Undo.DestroyObjectImmediate(g); labels++;
                }
                var line = $"{name}: removed {labels} floating label(s)";
                if (name == SceneNames.ShipStarter) line += ShipExtras(scene, all.Where(g => g != null).ToList());
                log.AppendLine(line);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>Active objects named exactly <paramref name="name"/> (or "name (n)"), preferring the StandMode ship.</summary>
        static System.Collections.Generic.List<GameObject> FindProps(System.Collections.Generic.List<GameObject> all, string name)
        {
            bool Match(GameObject g) => g.name == name || g.name.StartsWith(name + " (");
            var found = all.Where(g => g != null && Match(g) && g.activeInHierarchy).ToList();
            var stand = found.Where(g => g.transform.root.name.Contains("StandMode")).ToList();
            return stand.Count > 0 ? stand : found;
        }

        static void EnsureCollider(GameObject g, StringBuilder sb)
        {
            if (g.GetComponentInChildren<Collider>(true) != null) return;
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var box = Undo.AddComponent<BoxCollider>(g);
            box.isTrigger = true; // interaction only, no new blocking
            var t = g.transform; var s = t.lossyScale;
            box.center = t.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, Mathf.Abs(s.x)), b.size.y / Mathf.Max(1e-4f, Mathf.Abs(s.y)), b.size.z / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
            sb.Append($"; trigger collider added on '{g.name}'");
        }

        static string ShipExtras(Scene scene, System.Collections.Generic.List<GameObject> all)
        {
            var sb = new StringBuilder();
            var host = FindProps(all, HelmHost).FirstOrDefault();
            CockpitHelm helm = null;
            if (host == null) sb.Append($"; no '{HelmHost}' found, console NOT added");
            else
            {
                helm = host.GetComponent<CockpitHelm>();
                if (helm == null) { helm = Undo.AddComponent<CockpitHelm>(host); helm.terminalName = "Ship Console"; }
                EnsureCollider(host, sb);
                // Window direction = from the ship's centre towards the cockpit (the nose), flattened.
                var shipRoot = all.Where(g => g.activeInHierarchy && g.transform.parent == null && g.name.StartsWith("Mark_1")).Select(g => g.transform).FirstOrDefault();
                Vector3 dir = Vector3.forward;
                if (shipRoot != null)
                {
                    var rs = shipRoot.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                        dir = host.transform.position - b.center; dir.y = 0f;
                        if (dir.sqrMagnitude < 0.01f) dir = shipRoot.forward;
                    }
                }
                dir.Normalize();
                Undo.RecordObject(helm, "Helm"); helm.windowDirection = dir;
                sb.Append($"; console (CockpitHelm) on '{host.name}' under '{host.transform.root.name}' (window dir {dir:F2})");
            }
            // An older pass may have put the helm on Cockpit_Console: remove that copy so there is one console.
            foreach (var old in all.Where(g => g != null && g.name == "Cockpit_Console" && g.GetComponent<CockpitHelm>() != null))
            { Undo.DestroyObjectImmediate(old.GetComponent<CockpitHelm>()); sb.Append("; old helm on Cockpit_Console removed"); }

            var chairs = FindProps(all, SeatHost);
            int seats = 0;
            foreach (var c in chairs)
            {
                var seat = c.GetComponent<CockpitSeat>();
                if (seat == null) seat = Undo.AddComponent<CockpitSeat>(c);
                EnsureCollider(c, sb);
                Undo.RecordObject(seat, "Seat"); seat.console = helm;
                seats++;
            }
            sb.Append($"; {seats} CockpitSeat(s) on '{SeatHost}'");
            var vol = all.Select(g => g.GetComponent<Volume>()).FirstOrDefault(v => v != null && v.gameObject.name == XenoarchShipSceneBuilder.VolumeName);
            var cc = AssetDatabase.LoadAssetAtPath<VolumeProfile>(XenoarchPhase1BSetup.CcA2018Path);
            if (vol != null && cc != null && vol.sharedProfile != cc) { Undo.RecordObject(vol, "Ship PP"); vol.sharedProfile = cc; sb.Append("; ship Volume -> VP_CC_Profile_A_2018"); }
            return sb.ToString();
        }

        public static void Validate(System.Action<bool, string> check)
        {
            int withLabels = 0;
            foreach (var n in SceneNames.All)
            {
                var p = XenoarchSetup.ScenePath(n);
                if (System.IO.File.Exists(p) && System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(p), @"m_Name: Label\r?\n")) withLabels++;
            }
            check(withLabels == 0, $"1B polish: no floating setup labels in scenes ({withLabels} scene(s) still have some)");
            // Ship_Starter: inspect the open scene (in memory) or open it additively read-only.
            var ship = XenoarchSetup.ScenePath(SceneNames.ShipStarter);
            if (!System.IO.File.Exists(ship)) { check(false, "1B polish: Ship_Starter exists"); return; }
            var scene = SceneManager.GetSceneByPath(ship);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ship, OpenSceneMode.Additive); opened = true; }
            try
            {
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
                var helms = all.Select(g => g.GetComponent<CockpitHelm>()).Where(h => h != null).ToList();
                // Anthony may add more helm consoles / seats himself: require the main one, and every one clickable.
                check(helms.Any(h => h.gameObject.name.StartsWith(HelmHost)) && helms.All(h => h.GetComponentInChildren<Collider>(true) != null),
                    $"1B cockpit: ship console (CockpitHelm) on {HelmHost}, every console has a collider ({string.Join(", ", helms.Select(h => h.gameObject.name))})");
                var seats = all.Select(g => g.GetComponent<CockpitSeat>()).Where(x => x != null).ToList();
                var badSeats = seats.Where(x => x.console == null || x.GetComponentInChildren<Collider>(true) == null).Select(x => x.gameObject.name).ToList();
                check(seats.Count(x => x.gameObject.name.StartsWith(SeatHost)) >= 2 && badSeats.Count == 0,
                    $"1B cockpit: {seats.Count} seat(s) ({string.Join(", ", seats.Select(x => x.gameObject.name).Distinct())}), each facing a console and clickable{(badSeats.Count > 0 ? " - no console/collider: " + string.Join(", ", badSeats) : "")}");
                check(System.IO.File.ReadAllText(ship).Contains("m_SkyboxMaterial: {fileID: 2100000"), "1B sky: Ship_Starter has its own skybox (open-space default)");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
