using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 2 M1 focused code-level check (Edit Mode only: no Play Mode, no scenes, no real save files; sessions use
    /// in-memory storage, file deletion is tested in a throwaway Temp folder). Covers progression classes, the eligibility
    /// gate, story-critical and star-map protection, runtime recovery, current-version save/load of the new state,
    /// New Game slot clearing, the frozen procedural pool v1 and independent RNG streams.
    /// Menu Xenoarch/Validate/Run Phase 2 M1 Check, or trigger file Temp/xenoarch_m1check.trigger. Report: Logs/xenoarch_m1_check.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchM1Check
    {
        public const string Trigger = "Temp/xenoarch_m1check.trigger";
        public const string ReportPath = "Logs/xenoarch_m1_check.txt";
        const string Tablet = "artifact.moon_metallic_tablet", NavCore = "artifact.mars_navigation_core", Lattice = "artifact.mars_sensor_lattice", Lump = "artifact.unknown_lump";
        static int _pass, _fail;
        static readonly StringBuilder Report = new StringBuilder();
        static double _next;

        static XenoarchM1Check() { EditorApplication.update += Poll; }

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

        [MenuItem("Xenoarch/Validate/Run Phase 2 M1 Check", priority = 101)]
        public static bool Run()
        {
            _pass = _fail = 0; Report.Clear();
            Report.AppendLine($"Xenoarch Phase 2 M1 check {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}, Unity {Application.unityVersion}");
            try { RunAll(); }
            catch (System.Exception e) { Check(false, "Unexpected exception: " + e); }
            Report.AppendLine($"SUMMARY: {_pass} passed, {_fail} failed");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, Report.ToString());
            Debug.Log($"[Xenoarch] Phase 2 M1 check: {_pass} passed, {_fail} failed ({ReportPath})");
            return _fail == 0;
        }

        static ArtifactInstance Give(GameSession s, string defId)
        {
            var d = s.Artifacts.Definition(defId);
            var a = s.Artifacts.Recover(d, 0.8f, "site.m1check", "body.m1check");
            if (a != null && !s.Inventory.AddInstance(defId, a.instanceId)) Report.AppendLine($"  (note: backpack full for {defId})");
            return a;
        }

        static int Copies(GameSession s, string instanceId)
        {
            int n = s.Inventory.Slots.Count(x => x.instanceId == instanceId);
            foreach (var h in s.Holds.Values) n += h.Slots.Count(x => x.instanceId == instanceId);
            return n;
        }

        static void RunAll()
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            Check(config != null && config.itemRegistry != null, "Config + item registry");
            if (config == null || config.itemRegistry == null) return;
            var reg = config.itemRegistry;
            var arts = reg.Entries.OfType<ArtifactDefinition>().ToList();
            ArtifactDefinition Def(string id) => arts.FirstOrDefault(a => a.Id == id);

            // ---------------------------------------------------------------- classifications
            Section("Progression classifications (data-driven, separate from rarity)");
            Check(Def(Tablet)?.progression == ArtifactProgression.StoryCritical, $"Tablet is Story Critical (rarity {Def(Tablet)?.rarity})");
            Check(Def(NavCore)?.progression == ArtifactProgression.StoryCritical, $"Navigation core is Story Critical (rarity {Def(NavCore)?.rarity})");
            Check(Def(Lattice)?.progression == ArtifactProgression.ResearchRequired, $"Sensor lattice is Research Required (rarity {Def(Lattice)?.rarity})");
            Check(Def(Lump)?.progression == ArtifactProgression.Ordinary, "Unknown lump is Ordinary");
            Check(Def(NavCore)?.releaseWhen.Any(r => r.factKey == Facts.Installed(NavCore)) == true && Def(NavCore).releaseWhen.Count == 1, "Nav core releases only on physical installation (tech.installed)");
            Check(Def(Tablet)?.releaseWhen.Any(r => r.factKey == Facts.Civilization("civ.vael")) == true && Def(Tablet).releaseWhen.Any(r => r.factKey == Facts.Translation(Tablet) && Mathf.Approximately(r.threshold, 0.35f)), "Tablet releases on civ.identified:civ.vael + translation 35%");
            var pieces = arts.Where(a => a.fragmentOf == ArtifactPuzzleService.StarMapGroup).ToList();
            Check(pieces.Count == 29, $"Star-map pieces in the registry: {pieces.Count} (expected 29)");
            int other = arts.Count(a => a.progression != ArtifactProgression.Ordinary && a.Id != Tablet && a.Id != NavCore && a.Id != Lattice);
            Check(other == 0, $"Every other artifact is Ordinary ({other} not)");

            // ---------------------------------------------------------------- procedural pool + streams
            Section("Procedural artifact pool v1 and RNG streams");
            var runtime = GameSession.RuntimeArtifactPool(reg);
            var legacy = ProceduralArtifactPools.LegacyRegistryFilter(reg);
            Check(runtime.SequenceEqual(ProceduralArtifactPools.V1), $"Runtime pool is frozen v1 [{string.Join(", ", runtime)}]");
            Check(legacy.SequenceEqual(ProceduralArtifactPools.V1), $"Old registry filter result equals v1 [{string.Join(", ", legacy)}]");
            Check(ProceduralArtifactPools.V1.All(id => reg.TryGetItem(id, out _)), "Every v1 id exists in the registry");
            var r1 = ProceduralGalaxy.Stream("body.x", "materials", 1337); var r2 = ProceduralGalaxy.Stream("body.x", "materials", 1337); var r3 = ProceduralGalaxy.Stream("body.x", "poi", 1337);
            int a1 = r1.Next(), a2 = r2.Next(), a3 = r3.Next();
            Check(a1 == a2, "Same (id, stream, seed) gives the same sequence");
            Check(a1 != a3, "Different stream names are independent");
            var wpool = new[] { new WeightedArtifactEntry("a", 3, "t"), new WeightedArtifactEntry("b", 1, "t") };
            var ra = new System.Random(7); var rb = new System.Random(7);
            bool same = true; for (int i = 0; i < 50; i++) same &= ProceduralArtifactPools.PickWeighted(wpool, ra) == ProceduralArtifactPools.PickWeighted(wpool, rb);
            Check(same, "Weighted pool pick (v2 support) is deterministic");

            // ---------------------------------------------------------------- New Game slot clearing
            Section("New Game clears development save slots");
            var slots = new List<string> { "auto", config.quickSaveSlot }; for (int i = 1; i <= 6; i++) slots.Add("slot" + i);
            var uiSlots = new List<string>(); foreach (var (slot, _) in DMI.Xenoarch.UI.SaveSlots.All()) uiSlots.Add(slot);
            Check(uiSlots.SequenceEqual(slots), $"Menu slot list = autosave, quick save, slot1-6 [{string.Join(", ", uiSlots)}]");
            var mem = new MemorySaveStorage(); var msvc = new SaveService(mem);
            foreach (var sl in slots) mem.Write(sl, "{}"); mem.Write("other", "{}");
            Check(msvc.DeleteSlots(uiSlots) == 8 && slots.All(sl => !mem.Exists(sl)) && mem.Exists("other"), "All 8 dev slots deleted, an unrelated slot kept (memory storage)");
            string tmp = Path.Combine("Temp", "xenoarch_m1_saves");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            var fs = new FileSaveStorage(tmp);
            foreach (var sl in slots) { fs.Write(sl, "{\"a\":1}"); fs.Write(sl, "{\"a\":2}"); } // second write leaves .json.bak
            File.WriteAllText(Path.Combine(tmp, "notes.txt"), "keep"); fs.Write("unrelated", "{}");
            bool baks = slots.All(sl => File.Exists(Path.Combine(tmp, sl + ".json.bak")));
            int deleted = new SaveService(fs).DeleteSlots(uiSlots);
            bool gone = slots.All(sl => !File.Exists(Path.Combine(tmp, sl + ".json")) && !File.Exists(Path.Combine(tmp, sl + ".json.bak")));
            bool kept = File.Exists(Path.Combine(tmp, "notes.txt")) && File.Exists(Path.Combine(tmp, "unrelated.json"));
            Check(baks && deleted == 8 && gone && kept, $"File storage: 8 slots + their .bak deleted, unrelated files kept (bak={baks}, deleted={deleted}, gone={gone}, kept={kept})");
            Directory.Delete(tmp, true);

            // ---------------------------------------------------------------- story-critical protection
            Section("Story-critical protection (eligibility gate in the services)");
            var s = new GameSession(config, reg, new MemorySaveStorage());
            s.StartNewGame();
            var tablet = Give(s, Tablet);
            var g = s.Eligibility;
            Check(s.Artifacts.Check(tablet, ArtifactAction.Sell).IsBlocked, "Tablet: sell blocked: " + s.Artifacts.Check(tablet, ArtifactAction.Sell).Message);
            bool sold = s.Artifacts.TrySell(tablet, true, () => s.Inventory.RemoveInstance(tablet.instanceId), out _);
            Check(!sold && s.Inventory.ContainsInstance(tablet.instanceId) && tablet.location == ArtifactLocation.Held, "Tablet: service refuses the sale even with warning accepted; item stays");
            foreach (var act in new[] { ArtifactAction.Discard, ArtifactAction.Destroy, ArtifactAction.Dismantle, ArtifactAction.Donate, ArtifactAction.Loan, ArtifactAction.ExchangeList, ArtifactAction.ExchangeEscrow, ArtifactAction.MoveToInaccessibleStorage })
                Check(g.Check(tablet, act).IsBlocked, $"Tablet: {act} blocked");
            foreach (var act in new[] { ArtifactAction.MoveToShipStorage, ArtifactAction.MoveToBackpack, ArtifactAction.MoveToRoverCargo, ArtifactAction.ResearchOperation, ArtifactAction.Install })
                Check(g.Check(tablet, act).Result == EligibilityResult.Allowed, $"Tablet: {act} allowed (accessible)");
            var stack = s.Inventory.Slots.First(x => x.instanceId == tablet.instanceId).Clone();
            Check(ShipHold.Transfer(s.Inventory, s.Hold(StorageRole.Artifacts), stack) == 1 && s.Hold(StorageRole.Artifacts).ContainsInstance(tablet.instanceId), "Tablet: backpack -> artifact crate transfer works");
            stack = s.Hold(StorageRole.Artifacts).Slots.First(x => x.instanceId == tablet.instanceId).Clone();
            Check(ShipHold.Transfer(s.Hold(StorageRole.Artifacts), s.Inventory, stack) == 1 && s.Inventory.ContainsInstance(tablet.instanceId), "Tablet: crate -> backpack transfer works");
            Check(s.Inventory.Remove(Tablet, 1) == 0 && s.Inventory.ContainsInstance(tablet.instanceId), "Tablet: bulk removal by id (crafting/refining) skips it");
            s.Discovery.Publish(Facts.Civilization("civ.vael")); s.Discovery.Publish(Facts.Translation(Tablet), 0.35f);
            Check(!g.IsStoryProtected(tablet), "Tablet: story protection released after civ id + 35% translation");
            var tv = g.Check(tablet, ArtifactAction.Sell);
            Check(tv.IsBlocked && tv.Message.Contains("different pieces"), "Tablet: still blocked as a needed star-map piece: " + tv.Message);

            var lump = Give(s, Lump);
            s.Artifacts.Complete(lump, ResearchOp.Cleaning);
            Check(s.Artifacts.Check(lump, ArtifactAction.Sell).Result == EligibilityResult.Allowed, "Ordinary lump: sell allowed");
            Check(s.Artifacts.TrySell(lump, false, () => s.Inventory.RemoveInstance(lump.instanceId), out _) && lump.location == ArtifactLocation.Sold, "Ordinary lump: sold through the service");
            Check(s.Discovery.Has(Facts.Research(Lump, ResearchOp.Cleaning)) && s.Discovery.Has(Facts.Recovered(Lump)), "Knowledge stays after the sale (research + recovered facts)");
            Check(s.Artifacts.Check(lump, ArtifactAction.Sell).IsBlocked, "Sold item cannot be sold again");
            var lump2 = Give(s, Lump);
            s.Artifacts.HasReagents = null; s.Artifacts.PayReagents = null;
            Check(s.Artifacts.Start(lump2, ResearchOp.Cleaning, out var why), "Lab job started on a lump " + why);
            Check(s.Artifacts.Check(lump2, ArtifactAction.Sell).IsBlocked, "Item in the running lab job cannot be sold");
            s.Artifacts.Cancel();
            Check(!s.Artifacts.Check(lump2, ArtifactAction.Sell).IsBlocked, "After cancelling the job it can be sold");

            // ---------------------------------------------------------------- star map
            Section("Unique star-map piece safeguards");
            var plain = pieces.Where(p => p.Id != Tablet && p.Id != NavCore && p.Id != Lattice).Select(p => p.Id).ToList();
            var owned = new List<ArtifactInstance> { tablet };
            for (int i = 0; i < 7; i++) owned.Add(Give(s, plain[i]));
            var p1 = owned[1];
            Check(g.Check(p1, ArtifactAction.Sell).IsBlocked, "8 distinct pieces owned: selling one is blocked (" + g.Check(p1, ArtifactAction.Sell).Message + ")");
            var extra = Give(s, plain[7]);
            Check(!g.Check(p1, ArtifactAction.Sell).IsBlocked, "9 distinct pieces owned: a surplus piece may be sold");
            var dup = Give(s, plain[2]);
            Check(!g.Check(dup, ArtifactAction.Sell).IsBlocked, "Duplicate of an owned piece type may be sold");
            Check(s.Puzzles.Place(ArtifactPuzzleService.StarMapGroup, 0, p1.instanceId, out var pr), "Place a piece on the board " + pr);
            Check(g.Check(p1, ArtifactAction.Sell).IsBlocked && g.Check(p1, ArtifactAction.Sell).Message.Contains("board"), "Placed piece is secured: " + g.Check(p1, ArtifactAction.Sell).Message);
            for (int i = 1; i < 8; i++) s.Puzzles.Place(ArtifactPuzzleService.StarMapGroup, i, i == 1 ? tablet.instanceId : owned[i].instanceId, out _);
            Check(s.Puzzles.IsSolved(ArtifactPuzzleService.StarMapGroup) && s.Discovery.Has(ArtifactPuzzleService.SolvedFact(ArtifactPuzzleService.StarMapGroup)), "8 distinct pieces placed: puzzle solved");
            Check(!g.Check(p1, ArtifactAction.Sell).IsBlocked && !g.Check(tablet, ArtifactAction.Sell).IsBlocked, "After solving: pieces and the released tablet may be sold");
            var lattice = Give(s, Lattice);
            var lv = g.Check(lattice, ArtifactAction.Sell);
            Check(lv.IsWarning, "Sensor lattice (unresearched): sell gives a warning: " + lv.Message);
            Check(!s.Artifacts.TrySell(lattice, false, () => s.Inventory.RemoveInstance(lattice.instanceId), out _) && s.Inventory.ContainsInstance(lattice.instanceId), "Lattice: not sold without confirmation");
            Check(s.Artifacts.TrySell(lattice, true, () => s.Inventory.RemoveInstance(lattice.instanceId), out _), "Lattice: sold after confirmation");
            var nav = Give(s, NavCore);
            Check(g.Check(nav, ArtifactAction.Sell).IsBlocked, "Nav core: blocked after the puzzle (not installed yet)");
            s.Artifacts.Complete(nav, ResearchOp.TechnologyAnalysis);
            Check(s.Discovery.Has(Facts.Research(NavCore, ResearchOp.TechnologyAnalysis)) && g.Check(nav, ArtifactAction.Sell).IsBlocked, "Nav core: research alone does not release it");
            Check(s.Artifacts.Install(nav, out var ir) && s.Inventory.RemoveInstance(nav.instanceId), "Nav core: installed " + ir);
            Check(!g.IsStoryProtected(nav) && s.Discovery.Has(Facts.Installed(NavCore)) && nav.location == ArtifactLocation.Installed, "Nav core: released by installation");

            // ---------------------------------------------------------------- recovery
            Section("Runtime recovery");
            var r = new GameSession(config, reg, new MemorySaveStorage());
            r.StartNewGame();
            var rt = Give(r, Tablet); var rn = Give(r, NavCore); var rp = Give(r, plain[0]);
            Check(r.Recovery.Ensure("check") == 0, "Nothing to do when everything is in place");
            r.Inventory.RemoveInstance(rt.instanceId); // simulated failed transfer
            Check(r.Recovery.Ensure("check") == 1 && r.Inventory.ContainsInstance(rt.instanceId) && Copies(r, rt.instanceId) == 1, "Missing tablet restored with the same instance id, one copy");
            Check(r.Recovery.Ensure("check") == 0 && r.Recovery.State.log.Count == 1, "Recovery is idempotent (second run does nothing; 1 ledger entry)");
            Check(r.Puzzles.Place(ArtifactPuzzleService.StarMapGroup, 3, rp.instanceId, out _), "Piece placed");
            r.Inventory.RemoveInstance(rp.instanceId);
            Check(r.Puzzles.SlotOf(ArtifactPuzzleService.StarMapGroup, rp.instanceId) == 3, "Board keeps a placed piece that went missing (puzzle state preserved)");
            r.Recovery.Ensure("check");
            Check(r.Inventory.ContainsInstance(rp.instanceId) && r.Puzzles.SlotOf(ArtifactPuzzleService.StarMapGroup, rp.instanceId) == 3, "Missing placed piece recovered and still on the board");
            // Full everywhere -> held by the lab
            string res = reg.Entries.FirstOrDefault(e => e != null && e.category == ItemCategory.Resource && !e.uniqueInstance)?.Id;
            Check(res != null, "Found a resource item to fill containers: " + res);
            r.Inventory.RemoveInstance(rn.instanceId);
            int filler = 0;
            if (res != null) { filler += 100000 - r.Inventory.Add(res, 100000); foreach (var h in r.Holds.Values) filler += 100000 - h.Add(res, 100000); }
            r.Recovery.Ensure("check");
            Check(r.Recovery.IsPending(rn.instanceId) && r.OwnsInstance(rn.instanceId) && Copies(r, rn.instanceId) == 0, "No room anywhere: the lab holds the nav core for pickup (still owned)");
            if (res != null) { r.Inventory.Remove(res, 100000); foreach (var h in r.Holds.Values) h.Remove(res, 100000); }
            Check(r.Recovery.CollectPending() == 1 && r.Inventory.ContainsInstance(rn.instanceId) && !r.Recovery.IsPending(rn.instanceId), "Collect at the lab once there is room");
            rt.location = ArtifactLocation.Sold; r.Inventory.RemoveInstance(rt.instanceId); // unguarded path (should not exist)
            r.Recovery.Ensure("check");
            Check(rt.location == ArtifactLocation.Held && r.Inventory.ContainsInstance(rt.instanceId), "Story item sold by an unguarded path is bought back, same id");
            r.Inventory.RemoveInstance(rn.instanceId); r.Artifacts.State.instances.Remove(rn); // record destroyed
            r.Recovery.Ensure("check");
            string rid = ArtifactRecoveryService.ReplacementPrefix + NavCore;
            Check(r.Artifacts.Get(rid) != null && r.Inventory.ContainsInstance(rid), "Destroyed nav core record: one controlled replacement " + rid);
            r.Inventory.RemoveInstance(rid); r.Artifacts.State.instances.Remove(r.Artifacts.Get(rid));
            r.Recovery.Ensure("check");
            Check(r.Artifacts.Get(rid) == null && r.Recovery.State.replacementsIssued.Count == 1, "Replacement is issued only once per definition");
            var rl = Give(r, Lump);
            r.Artifacts.HasReagents = null; r.Artifacts.PayReagents = null;
            r.Artifacts.Start(rl, ResearchOp.Cleaning, out _);
            rl.location = ArtifactLocation.Sold; r.Inventory.RemoveInstance(rl.instanceId);
            r.Recovery.Ensure("check");
            Check(!r.Artifacts.Busy && r.Recovery.State.log.Any(e => e.reason == "research-interrupted"), "Lab job on a lost item is cancelled (interrupted research)");

            // ---------------------------------------------------------------- save / load
            Section("Current-version save/load of the new state");
            var rn2 = Give(r, NavCore); // a fresh (non-unique-checked) nav instance to test pickup persistence
            r.Inventory.RemoveInstance(rn2.instanceId); r.Recovery.State.pendingAtLab.Add(rn2.instanceId);
            int ledger = r.Recovery.State.log.Count;
            Check(r.Saves.Save("m1"), "Save");
            int facts = r.Discovery.State.facts.Count; // State.facts is filled on capture
            float tabletTr = r.Discovery.Value(Facts.Translation(Tablet));
            r.StartNewGame();
            Check(r.Recovery.State.log.Count == 0 && r.Recovery.State.pendingAtLab.Count == 0 && r.Puzzles.Filled(ArtifactPuzzleService.StarMapGroup) == 0, "New Game resets the ledger, lab pickups and the board");
            for (int i = 0; i < 2; i++) { Check(r.Saves.TryRead("m1", out var f, out var err), $"Read (pass {i + 1}) {err}"); r.Saves.Apply(f); }
            Check(r.Recovery.State.log.Count == ledger && r.Recovery.State.replacementsIssued.Count == 1, $"Ledger restored ({r.Recovery.State.log.Count}/{ledger}) and replacement record kept");
            Check(r.Inventory.ContainsInstance(rn2.instanceId) && !r.Recovery.IsPending(rn2.instanceId) && Copies(r, rn2.instanceId) == 1, "Lab pickup survived save/load and was collected once on load");
            Check(r.Puzzles.SlotOf(ArtifactPuzzleService.StarMapGroup, rp.instanceId) == 3 && Copies(r, rp.instanceId) == 1, "Star-map board restored, placed piece not duplicated");
            Check(Copies(r, rt.instanceId) == 1 && r.Artifacts.Check(r.Artifacts.Get(rt.instanceId), ArtifactAction.Sell).IsBlocked, "Tablet restored once and still protected after load");
            Check(facts > 0 && r.Discovery.State.facts.Count == facts && Mathf.Approximately(r.Discovery.Value(Facts.Translation(Tablet)), tabletTr), $"Discovery facts restored ({r.Discovery.State.facts.Count}/{facts}, tablet translation {tabletTr:0.##})");
            Check(r.Recovery.Ensure("check") == 0, "Nothing to recover after a clean load");
        }
    }
}
