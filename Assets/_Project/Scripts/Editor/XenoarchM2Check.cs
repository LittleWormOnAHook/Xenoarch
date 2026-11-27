using System.IO;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Objectives;
using DMI.Xenoarch.Save;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 2 M2 polish: focus, save/load, hide-optional fallback, destination id, New Game / New Test All.
    /// Edit Mode only (in-memory saves, no Play Mode, no scenes). Menu Xenoarch/Validate/Run Phase 2 M2 Check,
    /// or trigger file Temp/xenoarch_m2check.trigger. Report: Logs/xenoarch_m2_check.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchM2Check
    {
        public const string Trigger = "Temp/xenoarch_m2check.trigger";
        public const string ReportPath = "Logs/xenoarch_m2_check.txt";
        static int _pass, _fail;
        static readonly StringBuilder Report = new StringBuilder();
        static double _next;

        static XenoarchM2Check() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Trigger)) return;
            try { File.Delete(Trigger); } catch { return; }
            Run();
        }

        static void Check(bool ok, string what)
        {
            if (ok) _pass++; else _fail++;
            Report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {what}");
        }

        static void Section(string title) => Report.AppendLine($"--- {title}");

        [MenuItem("Xenoarch/Validate/Run Phase 2 M2 Check", priority = 102)]
        public static bool Run()
        {
            _pass = _fail = 0; Report.Clear();
            Report.AppendLine($"Xenoarch Phase 2 M2 check {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}, Unity {Application.unityVersion}");
            try { RunAll(); }
            catch (System.Exception e) { Check(false, "Unexpected exception: " + e); }
            Report.AppendLine($"SUMMARY: {_pass} passed, {_fail} failed");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, Report.ToString());
            Debug.Log($"[Xenoarch] Phase 2 M2 check: {_pass} passed, {_fail} failed ({ReportPath})");
            return _fail == 0;
        }

        static GameSession Session(XenoarchConfig config)
        {
            var s = new GameSession(config, config.itemRegistry, new MemorySaveStorage());
            s.StartNewGame();
            s.Objectives?.Evaluate(false);
            return s;
        }

        static void OpenOptionalSides(GameSession s)
        {
            s.Discovery.Publish(Facts.Recovered("artifact.moon_metallic_tablet"));
            s.Discovery.Publish(Facts.Civilization("civ.vael"));
            s.Discovery.Publish(Facts.Translation("artifact.moon_metallic_tablet"), 0.35f);
            s.Discovery.Publish(Facts.Site("site.mars.canyon_relay"), 2f);
            s.Objectives.Evaluate(false);
        }

        static string Order(GameSession s)
        {
            var b = new StringBuilder();
            var main = s.Objectives.Main();
            if (main != null) b.Append(main.id);
            foreach (var side in s.Objectives.Sides()) b.Append('|').Append(side.id);
            return b.ToString();
        }

        static void RunAll()
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            Check(config != null && config.itemRegistry != null && config.discoveryChains != null && config.discoveryChains.Count > 0,
                "Config, item registry and discovery chains are available");
            if (config == null || config.itemRegistry == null) return;

            // ---------------------------------------------------------------- new game / default focus
            Section("New Game default focus");
            var s = Session(config);
            var main = s.Objectives.Main();
            Check(main != null && main.id == ObjectiveService.WaystationsChain, "Main expedition is The Waystations (chain.moon_mars)");
            Check(s.Objectives.FocusedId == main.id, "Default focus is the main expedition");
            Check(s.Objectives.Sides().Count == 0, "No side objectives at New Game");
            Check(s.Objectives.IsFocused(main.id) && !s.Objectives.IsFocused("missing"), "IsFocused matches the main id only");

            // ---------------------------------------------------------------- destination
            Section("Destination from the current main line");
            s.Discovery.Publish(Facts.Recovered("artifact.moon_metallic_tablet"));
            s.Discovery.Publish(Facts.Civilization("civ.vael"));
            s.Discovery.Publish(Facts.Translation("artifact.moon_metallic_tablet"), 0.35f);
            s.Objectives.Evaluate(false);
            var dest = s.Objectives.Destination();
            Check(dest != null && dest.siteId == "site.mars.canyon_relay",
                "After the tablet is translated, the focused destination is the Mars canyon relay (" + (dest != null ? dest.siteId : "null") + ")");
            var parsed = ObjectiveService.DestinationFromFact("site.revealed:site.mars.canyon_relay");
            Check(parsed != null && parsed.siteId == "site.mars.canyon_relay", "DestinationFromFact reads site.revealed keys");
            var body = ObjectiveService.DestinationFromFact("body.visited:body.sol.mars");
            Check(body != null && body.bodyId == "body.sol.mars", "DestinationFromFact reads body.visited keys");

            // ---------------------------------------------------------------- sides, focus, no reorder, no fact writes
            Section("Focus, order, and discovery isolation");
            s.Discovery.Publish(Facts.Site("site.mars.canyon_relay"), 2f);
            s.Objectives.Evaluate(false);
            var sides = s.Objectives.Sides();
            Check(sides.Count >= 1 && sides.Count <= ObjectiveService.MaxHudSides, $"Up to two sides listed ({sides.Count})");
            Check(s.Objectives.Main() != null && s.Objectives.Main().id == ObjectiveService.WaystationsChain, "Main stays the Waystations chain after sides appear");
            string before = Order(s);
            Check(before.StartsWith(ObjectiveService.WaystationsChain + "|") || before == ObjectiveService.WaystationsChain, "Main id is first in the HUD order");
            string sideId = sides[0].id;
            float tabletTr = s.Discovery.Value(Facts.Translation("artifact.moon_metallic_tablet"));
            bool hasTablet = s.Discovery.Has(Facts.Recovered("artifact.moon_metallic_tablet"));
            s.Objectives.SetFocus(sideId);
            Check(s.Objectives.FocusedId == sideId, "Focus moves to the first side");
            Check(s.Objectives.Main().id == ObjectiveService.WaystationsChain, "Main id is unchanged after focusing a side");
            Check(Order(s) == before, "HUD order is unchanged after a focus change (" + Order(s) + ")");
            Check(s.Discovery.Has(Facts.Recovered("artifact.moon_metallic_tablet")) == hasTablet
                && Mathf.Approximately(s.Discovery.Value(Facts.Translation("artifact.moon_metallic_tablet")), tabletTr),
                "SetFocus does not change discovery facts");
            s.Objectives.SetFocus("objective.does.not.exist");
            Check(s.Objectives.FocusedId == main.id, "Unknown focus id falls back to the main expedition");

            // ---------------------------------------------------------------- hide optional
            Section("Hide optional fallback");
            s.Objectives.SetFocus(sideId);
            Check(s.Objectives.FocusedId == sideId, "Side can be focused again before hiding");
            s.Objectives.HideOptional = true;
            Check(s.Objectives.FocusedId == s.Objectives.Main().id, "Hiding optional sides returns focus to main");
            Check(s.Objectives.Sides().Count == 0, "Hidden optional sides are not listed");
            s.Objectives.HideOptional = false;
            Check(s.Objectives.Sides().Count >= 1, "Showing optional sides lists them again");

            // ---------------------------------------------------------------- complete focused side
            Section("Completing a focused side restores main");
            s.Objectives.SetFocus(sideId);
            Check(s.Objectives.FocusedId == sideId, "Side focused before completion");
            // sensor side: recover the lattice; chamber side: open the chamber
            if (sideId.EndsWith(":sensor")) s.Discovery.Publish(Facts.Recovered("artifact.mars_sensor_lattice"));
            else if (sideId.EndsWith(":chamber")) s.Discovery.Publish(Facts.ChamberOpened("chamber.mars.canyon_relay"));
            else s.Discovery.Publish(Facts.Recovered("artifact.mars_sensor_lattice"));
            s.Objectives.Evaluate(true);
            Check(s.Objectives.FocusedId == s.Objectives.Main().id, "Completing the focused side returns focus to main (now " + s.Objectives.FocusedId + ")");

            // ---------------------------------------------------------------- save / load
            Section("Save and load preserves focus");
            var remaining = s.Objectives.Sides();
            Check(remaining.Count >= 1, "A second side is still available to save");
            if (remaining.Count >= 1)
            {
                string savedId = remaining[0].id;
                s.Objectives.SetFocus(savedId);
                Check(s.Saves.Save("m2focus"), "Save");
                s.StartNewGame();
                s.Objectives.Evaluate(false);
                Check(s.Objectives.FocusedId == s.Objectives.Main().id, "New Game resets focus to main");
                Check(s.Saves.TryRead("m2focus", out var file, out var err), "Read saved slot " + err);
                s.Saves.Apply(file);
                s.Objectives.Evaluate(false);
                Check(s.Objectives.FocusedId == savedId, "Focus is restored after load (" + s.Objectives.FocusedId + ")");
            }

            // ---------------------------------------------------------------- old save (missing focusedId)
            Section("Old saves without focusedId");
            var oldJson = JsonUtility.FromJson<ObjectiveState>("{\"hideOptional\":false,\"notified\":[]}");
            Check(oldJson != null && string.IsNullOrEmpty(oldJson.focusedId), "JSON without focusedId deserializes with an empty focus");
            var sOld = Session(config);
            OpenOptionalSides(sOld);
            Check(sOld.Saves.Save("m2old"), "Save a current slot to wrap an old section");
            Check(sOld.Saves.TryRead("m2old", out var oldFile, out var oldErr), "Read wrapper " + oldErr);
            oldFile.Set(ObjectiveService.Key, "{\"hideOptional\":false,\"notified\":[]}");
            sOld.StartNewGame();
            sOld.Saves.Apply(oldFile);
            sOld.Objectives.Evaluate(false);
            Check(sOld.Objectives.FocusedId == sOld.Objectives.Main().id, "Old save with no focusedId falls back to main without error");

            // ---------------------------------------------------------------- New Test All
            Section("New Test All");
            var t = Session(config);
            t.ApplyTestAllLoadout();
            t.Objectives.Evaluate(false);
            Check(t.Objectives.Main() != null && t.Objectives.Main().id == ObjectiveService.WaystationsChain, "Test All keeps the Waystations main objective");
            Check(!t.Objectives.Main().complete, "Test All does not complete the Waystations chain");
            Check(!t.Discovery.Has(Facts.Recovered("artifact.moon_metallic_tablet")), "Test All does not recover the story tablet");
            Check(t.Objectives.FocusedId == t.Objectives.Main().id, "Test All focus defaults to main");
        }
    }
}