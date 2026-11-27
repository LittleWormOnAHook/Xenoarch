using System.IO;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// t243u / t244u: backpack is 29 slots, old 24-slot saves grow, hotbar stacks do not use backpack space,
    /// New Test All still fills. Edit Mode only. Menu Xenoarch/Validate/Run Inventory Check, or trigger
    /// Temp/xenoarch_invcheck.trigger. Report: Logs/xenoarch_inv_check.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchInventoryCheck
    {
        public const string Trigger = "Temp/xenoarch_invcheck.trigger";
        public const string ReportPath = "Logs/xenoarch_inv_check.txt";
        static int _pass, _fail;
        static readonly StringBuilder Report = new StringBuilder();
        static double _next;

        static XenoarchInventoryCheck() { EditorApplication.update += Poll; }

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

        [MenuItem("Xenoarch/Validate/Run Inventory Check", priority = 103)]
        public static bool Run()
        {
            _pass = _fail = 0; Report.Clear();
            Report.AppendLine($"Xenoarch inventory check {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}, Unity {Application.unityVersion}");
            try { RunAll(); }
            catch (System.Exception e) { Check(false, "Unexpected exception: " + e); }
            Report.AppendLine($"SUMMARY: {_pass} passed, {_fail} failed");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, Report.ToString());
            Debug.Log($"[Xenoarch] Inventory check: {_pass} passed, {_fail} failed ({ReportPath})");
            return _fail == 0;
        }

        static void RunAll()
        {
            var config = Resources.Load<XenoarchConfig>(XenoarchConfig.ResourcePath);
            if (config == null || config.itemRegistry == null)
            {
                Check(false, "XenoarchConfig and item registry are available");
                return;
            }
            Check(true, "Config and item registry are available");
            Check(config.inventoryCapacity == 29, $"Config inventory capacity is 29 (got {config.inventoryCapacity})");

            Section("New Game capacity and hotbar shelving");
            var s = new GameSession(config, config.itemRegistry, new MemorySaveStorage());
            s.StartNewGame();
            Check(s.Inventory.Capacity == 29, $"New Game backpack has 29 slots (got {s.Inventory.Capacity})");
            s.Hotbar.ShelveFrom(s.Inventory);
            string shovel = DigTools.All != null && DigTools.All.Length > 0 ? DigTools.All[0].itemId : null;
            if (string.IsNullOrEmpty(shovel)) Check(false, "Default dig tool id is available");
            else
            {
                Check(s.Inventory.Count(shovel) > 0, "New Game owns the default shovel (dev loadout or hotbar)");
                int inPack = 0;
                foreach (var st in s.Inventory.Slots) if (!st.IsEmpty && st.itemId == shovel) inPack++;
                bool onBar = false;
                for (int i = 1; i < Hotbar.Size; i++) if (s.Hotbar.StackAt(i) != null && s.Hotbar.StackAt(i).itemId == shovel) onBar = true;
                Check(onBar, "The shovel stack is stored on the hotbar");
                Check(inPack == 0, "The shovel no longer occupies a backpack slot");
            }

            Section("Old 24-slot saves grow to 29");
            var old = new InventorySaveData { capacity = 24 };
            for (int i = 0; i < 24; i++) old.slots.Add(new ItemStack());
            string res = FirstResource(config);
            if (res != null) { old.slots[0].itemId = res; old.slots[0].count = 3; }
            s.Inventory.Restore(old);
            Check(s.Inventory.Capacity == 29, $"Restoring a 24-slot save keeps 29 slots (got {s.Inventory.Capacity})");
            Check(s.Inventory.Slots[28].IsEmpty, "The five new slots are empty");
            if (res != null) Check(s.Inventory.Count(res) == 3, "The saved stack is still in the backpack");

            Section("Grown inventories survive save and load");
            s.Inventory.SetCapacity(40);
            if (res != null) s.Inventory.Add(res, 1);
            Check(s.Saves.Save("invcap"), "Save a 40-slot backpack");
            var s2 = new GameSession(config, config.itemRegistry, s.Saves.Storage);
            Check(s2.Saves.TryRead("invcap", out var file, out var err), "Read saved slot " + err);
            s2.Saves.Apply(file);
            Check(s2.Inventory.Capacity == 40, $"Loaded backpack keeps 40 slots (got {s2.Inventory.Capacity})");
            if (res != null) Check(s2.Inventory.Count(res) >= 3, "Loaded resource stack is intact");

            Section("New Test All");
            var t = new GameSession(config, config.itemRegistry, new MemorySaveStorage());
            t.StartNewGame();
            t.ApplyTestAllLoadout();
            Check(t.Inventory.Capacity >= 29, $"Test All backpack is at least 29 slots (got {t.Inventory.Capacity})");
            if (!string.IsNullOrEmpty(shovel)) Check(t.Inventory.Count(shovel) > 0, "Test All still grants the shovel");
            if (res != null)
            {
                config.itemRegistry.TryGetItem(res, out var def);
                Check(def != null && t.Inventory.Count(res) >= def.EffectiveMaxStack, "Test All still grants a full resource stack");
            }
            int toolsMissing = 0;
            foreach (var d in config.itemRegistry.Entries)
                if (d != null && (d.category == ItemCategory.Tool || d.category == ItemCategory.Equipment) && t.Inventory.Count(d.Id) <= 0) toolsMissing++;
            Check(toolsMissing == 0, "Test All grants every tool and equipment item (" + toolsMissing + " missing)");
        }

        static string FirstResource(XenoarchConfig config)
        {
            if (config.itemRegistry?.Entries == null) return null;
            foreach (var e in config.itemRegistry.Entries)
                if (e != null && e.category == ItemCategory.Resource && !e.uniqueInstance) return e.Id;
            return null;
        }
    }
}
