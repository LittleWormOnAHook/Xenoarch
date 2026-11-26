using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Crafting;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Materials;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// ZEN-16 Depth Probe data: the item (item.tool.depth_probe), its tier definition (Resources/Excavation/DepthProbe),
    /// the craft recipe and the Mk II / Mk III upgrades (upgrade "scanner.depth_probe"). Additive and rerunnable: keeps
    /// hand-tuned tier values, only appends missing registry entries / recipes. Also runs at the end of Batch E crafting.
    /// </summary>
    public static class XenoarchDepthProbeSetup
    {
        public const string Trigger = "Temp/xenoarch_depthprobe.trigger";
        const string DefFolder = XenoarchSetup.ResourcesFolder + "/" + DepthProbeDefinition.ResourceFolder;
        const string DefPath = DefFolder + "/DepthProbe.asset";
        const string ItemPath = XenoarchSetup.ItemsFolder + "/Item_tool_depth_probe.asset";
        const string CraftFolder = XenoarchSetup.Root + "/Data/Crafting";

        [MenuItem("Tools/Xenoarch/Setup/Depth Probe (item, tiers, recipes)")]
        public static void Menu() => RunFromTrigger();

        public static void RunFromTrigger()
        {
            var sb = new StringBuilder();
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null) sb.AppendLine("No XenoarchConfig at " + XenoarchSetup.ConfigPath);
            else Run(config, sb);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xenoarch_depthprobe.txt", sb.ToString());
            Debug.Log("[Xenoarch] Depth Probe setup:\n" + sb);
        }

        public static void Run(XenoarchConfig config, StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(DefFolder); XenoarchSetup.EnsureFolder(CraftFolder);
            var def = Load<DepthProbeDefinition>(DefPath, out bool newDef);
            if (newDef) { def.SetIdForTooling("probe.depth"); EditorUtility.SetDirty(def); }

            var item = Load<ItemDefinition>(ItemPath, out bool newItem);
            item.SetIdForTooling(def.itemId);
            item.displayName = def.displayName; item.category = ItemCategory.Tool; item.maxStack = 1; item.uniqueInstance = false;
            item.mass = 1.2f; if (newItem) item.baseValue = 350;
            item.description = "Point it at the floor of a pit and pulse (LMB) or hold LMB: shows roughly how far below the nearest buried object lies and which way it is offset, never what it is. Upgrades track up to 3 objects, deeper and more precisely.";
            if (item.icon == null) item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(XenoarchCraftingSetup.IconFolder + "/radar-dish.png");
            EditorUtility.SetDirty(item);
            var reg = config.itemRegistry;
            var entries = reg.Entries.Where(e => e != null).ToList();
            if (!entries.Any(e => e.Id == item.Id)) { entries.Add(item); reg.SetEntries(entries); EditorUtility.SetDirty(reg); }

            const string P = XenoarchCraftingSetup.Plate, W = XenoarchCraftingSetup.Wiring, C = XenoarchCraftingSetup.Circuit, S = XenoarchCraftingSetup.SensorArr,
                G = XenoarchCraftingSetup.Gold, Pt = XenoarchCraftingSetup.Platinum;
            var cr = new (string id, string name, string desc, CraftCategory cat, (string, int)[] ins, string outId, string up, int lvl, float secs)[]
            {
                ("craft.depth_probe", "Depth Probe", "Third scanner: reads how deep the nearest buried object lies below the floor of your pit.", CraftCategory.Tools, new[] { (P, 1), (W, 2), (C, 1) }, def.itemId, null, 0, 6f),
                ("craft.depth_probe_1", "Depth Probe Mk II", "Tracks 2 objects at once, reads depth in metres (+/-8 cm), 2.5 m across, 2 m down.", CraftCategory.Upgrades, new[] { (C, 2), (G, 1) }, null, DepthProbeDefinition.UpgradeId, 1, 6f),
                ("craft.depth_probe_2", "Depth Probe Mk III", "Tracks 3 objects at once, +/-3 cm, 3.5 m across, 3 m down.", CraftCategory.Upgrades, new[] { (S, 1), (C, 2), (Pt, 1) }, null, DepthProbeDefinition.UpgradeId, 2, 8f),
            };
            int added = 0;
            var crafts = config.craftRecipes ?? new System.Collections.Generic.List<CraftRecipe>();
            foreach (var d in cr)
            {
                var r = Load<CraftRecipe>($"{CraftFolder}/Craft_{d.id.Substring("craft.".Length)}.asset", out _);
                r.SetIdForTooling(d.id); r.displayName = d.name; r.description = d.desc; r.category = d.cat;
                r.ingredients = d.ins.Select(i => new ItemAmount(i.Item1, i.Item2)).ToList();
                r.outputItemId = d.outId ?? ""; r.outputAmount = 1; r.upgradeId = d.up ?? ""; r.upgradeLevel = d.lvl;
                r.craftSeconds = d.secs; r.station = CraftStation.Ship;
                EditorUtility.SetDirty(r);
                if (!crafts.Contains(r)) { crafts.Add(r); added++; }
            }
            config.craftRecipes = crafts;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            log.AppendLine($"Depth Probe: item {(newItem ? "created" : "updated")}, definition {(newDef ? "created" : "kept")} ({def.tiers.Count} tiers), {added} recipe(s) added to config ({crafts.Count} total)");
        }

        static T Load<T>(string path, out bool created) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            created = a == null;
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }
    }
}
