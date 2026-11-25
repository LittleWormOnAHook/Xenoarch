using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Cheap Edit Mode smoke check of the Phase 1A foundation (no Play Mode, no scene loading):
    /// item registry, inventory rules, save round trip + no duplication, save versioning, build settings, world ids.
    /// Logs "[Xenoarch] SMOKE PASS/FAIL" lines and a summary to the Console/Editor.log.
    /// </summary>
    public static class XenoarchSmokeCheck
    {
        static int _pass, _fail;
        static readonly System.Text.StringBuilder Report = new System.Text.StringBuilder();
        /// <summary>Plain-text copy of the last run (Logs/ is not versioned).</summary>
        public const string ReportPath = "Logs/xenoarch_smoke.txt";

        [MenuItem("Xenoarch/Validate/Run Foundation Smoke Check", priority = 100)]
        public static bool Run()
        {
            _pass = _fail = 0;
            Report.Clear();
            Report.AppendLine($"Xenoarch foundation smoke check {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}, Unity {Application.unityVersion}");
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            Check(config != null, "XenoarchConfig exists in Resources");
            Check(config != null && config.itemRegistry != null, "Config references ItemRegistry");
            if (config == null || config.itemRegistry == null) return Summary();
            var reg = config.itemRegistry;

            var problems = reg.Validate();
            Check(problems.Count == 0, "Item registry ids valid and unique" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
            Check(reg.Entries.Count >= 7, $"Item registry has starter items ({reg.Entries.Count})");
            var ps = Resources.Load<UnityEngine.UIElements.PanelSettings>(DMI.Xenoarch.UI.XenoarchUI.PanelSettingsResource);
            Check(ps != null && ps.themeStyleSheet != null, "UITK PanelSettings + theme in Resources");
            foreach (var u in new[] { DMI.Xenoarch.UI.InteractionPromptHud.Uxml, DMI.Xenoarch.UI.MainMenuPlaceholder.Uxml, DMI.Xenoarch.Scenes.SceneLoader.FadeUxml })
                Check(Resources.Load<UnityEngine.UIElements.VisualTreeAsset>(u) != null, $"UXML {u} loads");
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(XenoarchPlayerBuilder.PlayerPrefabPath);
            Check(player != null && player.GetComponent<DMI.Xenoarch.Player.IPlayerRig>() != null && player.GetComponent<CharacterController>() != null, "XA_Player prefab: CharacterController + IPlayerRig");
            var armsDrv = player != null ? player.GetComponentInChildren<DMI.Xenoarch.Player.FirstPersonArms>(true) : null;
            var armsAnim = armsDrv != null ? armsDrv.GetComponentInChildren<Animator>(true) : null;
            Check(armsAnim != null && armsAnim.runtimeAnimatorController != null, "XA_Player arms Animator has controller");
            float cov = XenoarchPlayerBuilder.ArmsBindingCoverage(out var covDetail);
            Check(cov >= 0.9f, $"Arms clip bindings resolve on arms rig ({covDetail})");
            XenoarchBodyBuilder.Validate(Check);
            XenoarchPhase1BSetup.Validate(Check);
            XenoarchShipSceneBuilder.Validate(Check);
            string prefabGuid = AssetDatabase.AssetPathToGUID(XenoarchPlayerBuilder.PlayerPrefabPath);
            foreach (var n in new[] { SceneNames.ShipStarter, SceneNames.TradeHubSol, SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
            {
                var p = XenoarchSetup.ScenePath(n);
                string txt = File.Exists(p) ? File.ReadAllText(p) : "";
                Check(prefabGuid.Length > 0 && txt.Contains(prefabGuid), $"{n} uses XA_Player");
            }
            int withIcon = 0; foreach (var e in reg.Entries) if (e != null && e.icon != null) withIcon++;
            Check(withIcon >= XenoarchSetup.PlaceholderIcons.Count, $"Starter items have icons ({withIcon}/{reg.Entries.Count})");

            // ---- inventory
            var session = new GameSession(config, reg, new MemorySaveStorage());
            var inv = session.Inventory;
            Check(inv.Add("item.consumable.sample_container", 25) == 0, "Stackable add 25 fits (stack 20 -> 2 slots)");
            Check(inv.Count("item.consumable.sample_container") == 25, "Stack count = 25");
            Check(inv.Add("item.unknown.thing", 1) == 1, "Unknown item rejected");
            Check(inv.Remove("item.consumable.sample_container", 10) == 10 && inv.Count("item.consumable.sample_container") == 15, "Remove 10 -> 15");
            Check(inv.AddInstance("item.test.unique_fragment", "inst_a"), "Unique instance added");
            Check(!inv.AddInstance("item.test.unique_fragment", "inst_a"), "Duplicate unique instance rejected");
            Check(inv.Add("item.tool.brush", 1) == 0, "Tool added");
            inv.Clear();

            // ---- save round trip
            session.StartNewGame();
            int startKit = 0; foreach (var s in session.Inventory.Slots) if (!s.IsEmpty) startKit++;
            Check(startKit > 0 && session.Location.sceneName == SceneNames.ShipStarter, $"New game: starting kit ({startKit} slots) in {session.Location.sceneName}");
            session.Inventory.AddInstance("item.test.unique_fragment", "inst_save");
            session.World.MarkConsumed("wo.test.pickup");
            Check(!session.World.MarkConsumed("wo.test.pickup"), "World object cannot be consumed twice");
            session.Location.sceneName = SceneNames.MoonExcavation01; session.Location.hasPose = true; session.Location.position = new Vector3(1, 2, 3);
            int samples = session.Inventory.Count("item.consumable.sample_container");
            Check(session.Saves.Save("smoke"), "Save to slot 'smoke'");

            session.StartNewGame(); // diverge
            session.Inventory.Add("item.consumable.sample_container", 3);
            for (int i = 0; i < 2; i++) // load twice: must not duplicate
            {
                Check(session.Saves.TryRead("smoke", out var file, out var err), $"Read save (pass {i + 1}) {err}");
                session.Saves.Apply(file);
            }
            Check(session.Inventory.Count("item.consumable.sample_container") == samples, $"Stack count restored exactly ({samples})");
            Check(session.Inventory.ContainsInstance("item.test.unique_fragment") == false && session.Inventory.ContainsInstance("inst_save"), "Unique instance restored with same id");
            int fragments = session.Inventory.Count("item.test.unique_fragment");
            Check(fragments == 1, $"Unique instance not duplicated ({fragments})");
            Check(session.World.IsConsumed("wo.test.pickup"), "Consumed world object stays consumed");
            Check(session.Location.sceneName == SceneNames.MoonExcavation01 && session.Location.position == new Vector3(1, 2, 3), "Player location restored");

            // ---- versioning
            var future = new SaveFile { version = SaveService.CurrentVersion + 1 };
            Check(!session.Saves.TryMigrate(future, out _), "Newer save version refused");
            var old = new SaveFile { version = SaveService.CurrentVersion - 1 };
            Check(!session.Saves.TryMigrate(old, out _), "Old version without migration refused");
            session.Saves.AddMigration(new NoOpMigration(SaveService.CurrentVersion - 1));
            Check(session.Saves.TryMigrate(old, out _) && old.version == SaveService.CurrentVersion, "Old version migrated forward");

            // ---- project setup
            var scenes = EditorBuildSettings.scenes;
            Check(scenes.Length == SceneNames.All.Length, $"Build Settings has {scenes.Length}/{SceneNames.All.Length} scenes");
            Check(scenes.Length > 0 && scenes[0].path.EndsWith("/Bootstrap.unity"), "Bootstrap is build index 0");
            foreach (var n in SceneNames.All) Check(File.Exists(XenoarchSetup.ScenePath(n)), $"Scene file {n}");
            CheckWorldIds();

            return Summary();
        }

        static void CheckWorldIds()
        {
            var seen = new Dictionary<string, string>();
            var rx = new Regex(@"\bid: (wo\.[A-Za-z0-9._-]+)");
            bool ok = true;
            foreach (var n in SceneNames.All)
            {
                var p = XenoarchSetup.ScenePath(n);
                if (!File.Exists(p)) continue;
                foreach (Match m in rx.Matches(File.ReadAllText(p)))
                {
                    var id = m.Groups[1].Value;
                    if (seen.TryGetValue(id, out var other)) { ok = false; Debug.LogError($"[Xenoarch] Duplicate WorldObjectId {id} in {n} and {other}"); }
                    else seen[id] = n;
                }
            }
            Check(ok, $"WorldObjectIds unique across scenes ({seen.Count})");
        }

        sealed class NoOpMigration : ISaveMigration
        {
            public int FromVersion { get; }
            public NoOpMigration(int from) { FromVersion = from; }
            public void Migrate(SaveFile file) { }
        }

        static void Check(bool ok, string what)
        {
            if (ok) { _pass++; Debug.Log("[Xenoarch] SMOKE PASS " + what); }
            else { _fail++; Debug.LogError("[Xenoarch] SMOKE FAIL " + what); }
            Report.AppendLine((ok ? "PASS " : "FAIL ") + what);
        }

        static bool Summary()
        {
            var msg = $"[Xenoarch] SMOKE SUMMARY {_pass} pass / {_fail} fail";
            if (_fail == 0) Debug.Log(msg); else Debug.LogError(msg);
            Report.AppendLine(msg);
            try { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, Report.ToString()); }
            catch (System.Exception e) { Debug.LogWarning("[Xenoarch] Could not write smoke report: " + e.Message); }
            return _fail == 0;
        }
    }
}
