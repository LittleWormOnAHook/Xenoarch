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
    /// Sonic dig tools data: renames the shovel and brush items/tiers, retires the precision tier from the hotbar defaults
    /// (its item id stays valid and maps to the excavator's Fine focus), and builds the sonic audio catalog
    /// (Resources/Audio/SonicToolAudio.asset: Kenney fallbacks committed, Pro Sound clips as gitignored vendorClips).
    /// Rerunnable. Trigger: Temp/xenoarch_sonic.trigger or Tools > Xenoarch > Setup > Sonic Tools.
    /// </summary>
    [InitializeOnLoad]
    public static class SonicToolsSetup
    {
        public const string Trigger = "Temp/xenoarch_sonic.trigger";
        const string ItemsDir = "Assets/_Project/Data/Items";
        const string ToolDir = "Assets/_Project/Resources/Excavation/DigTools";
        const string AudioDir = "Assets/_Project/Resources/Audio";
        const string Kenney = "Assets/Xenoarch_Free/Audio/Kenney_SciFiSounds/";
        const string VendorRoot = "Assets/Pro Sound Collection v4.26 Audio";
        static double _next;

        static SonicToolsSetup() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(Trigger)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { File.Delete(Trigger); } catch { return; }
            Run();
        }

        [MenuItem("Tools/Xenoarch/Setup/Sonic Tools (names, audio)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            Item("Item_tool_excavation_shovel.asset", "Sonic Excavator",
                "A handheld sonic emitter. Scroll sets the chisel head size, Shift+scroll the reach; Q cycles the head shape, R the power and T the focus (Fine is the old precision mode). Too strong near fragile finds.", sb);
            Item("Item_tool_brush.asset", "Ultrasonic Brush",
                "A fan of ultrasonic waves that lifts dust and loose soil off a find without touching it. Scroll sets the spread.", sb);
            Item("Item_tool_powered_drill.asset", "Sonic Burster",
                "A focused sonic shock cone for ice and rock. Hard bands that stall the excavator crack open. Shift+scroll sets the reach, R the power. Draws more power and heat than the excavator.", sb);
            Item("Item_tool_precision_tool.asset", "Excavator Fine Focus",
                "Merged into the Sonic Excavator's Fine focus. Kept so older saves keep working: it opens the excavator in Fine focus.", sb);
            Tier("DigTool_shovel.asset", "Sonic Excavator", "excavator", null, sb);
            Tier("DigTool_brush.asset", "Ultrasonic Brush", "brush", null, sb);
            Tier("DigTool_drill.asset", "Sonic Burster", "burster", null, sb);
            Tier("DigTool_precision.asset", null, null, false, sb);
            DigTools.Reload();
            Audio(sb);
            PowerCellAndProbe(sb);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xenoarch_sonic.txt", sb.ToString());
            Debug.Log("[Xenoarch] Sonic tools setup:\n" + sb);
        }

        static void Item(string file, string name, string desc, StringBuilder sb)
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemsDir + "/" + file);
            if (it == null) { sb.AppendLine("missing item " + file); return; }
            it.displayName = name; it.description = desc;
            EditorUtility.SetDirty(it);
            sb.AppendLine("item " + file + " -> " + name);
        }

        static void Tier(string file, string name, string shortName, bool? enabled, StringBuilder sb)
        {
            var d = AssetDatabase.LoadAssetAtPath<DigToolDefinition>(ToolDir + "/" + file);
            if (d == null) { sb.AppendLine("missing tier " + file); return; }
            var so = new SerializedObject(d);
            if (name != null) so.FindProperty("spec.name").stringValue = name;
            if (shortName != null) so.FindProperty("spec.shortName").stringValue = shortName;
            if (enabled.HasValue) so.FindProperty("enabledInGame").boolValue = enabled.Value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(d);
            sb.AppendLine("tier " + file + (name != null ? " -> " + name : "") + (enabled.HasValue ? " enabled=" + enabled.Value : ""));
        }

        /// <summary>Power Cell item + recipe, Depth Probe Mk IV (hologram) tier + recipe. Additive and rerunnable.</summary>
        static void PowerCellAndProbe(StringBuilder sb)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null) { sb.AppendLine("no XenoarchConfig: power cell / probe skipped"); return; }
            XenoarchSetup.EnsureFolder(XenoarchSetup.Root + "/Data/Crafting");
            const string itemPath = ItemsDir + "/Item_consumable_power_cell.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
            bool made = item == null;
            if (made) { item = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(item, itemPath); }
            item.SetIdForTooling(SonicTools.PowerCellId);
            item.displayName = "Power Cell"; item.category = ItemCategory.Consumable; item.maxStack = 250; item.uniqueInstance = false;
            item.mass = 0.3f; if (made) item.baseValue = 45;
            item.description = "A sealed energy cell. Press X with a sonic tool out to recharge the suit by 60%.";
            if (item.icon == null) item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(XenoarchCraftingSetup.IconFolder + "/crystal-cluster.png");
            EditorUtility.SetDirty(item);
            var reg = config.itemRegistry;
            var entries = reg.Entries.Where(e => e != null).ToList();
            if (!entries.Any(e => e.Id == item.Id)) { entries.Add(item); reg.SetEntries(entries); EditorUtility.SetDirty(reg); }

            string defPath = XenoarchSetup.ResourcesFolder + "/" + DepthProbeDefinition.ResourceFolder + "/DepthProbe.asset";
            var def = AssetDatabase.LoadAssetAtPath<DepthProbeDefinition>(defPath);
            if (def != null && def.tiers.Count < 4)
            {
                def.tiers.Add(new DepthProbeTier { label = "Mk IV Holo", tracks = 3, range = 4.5f, maxDepth = 4.5f, bandsOnly = false, noise = 0.02f, hologram = true });
                EditorUtility.SetDirty(def);
            }
            sb.AppendLine($"power cell item {(made ? "created" : "updated")}; depth probe tiers: {(def != null ? def.tiers.Count.ToString() : "definition missing")}");

            var recipes = new (string id, string name, string desc, CraftCategory cat, (string, int)[] ins, string outId, int outN, string up, int lvl, float secs)[]
            {
                ("craft.power_cell", "Power Cell x2", "Two sealed energy cells for field recharging the sonic tools (X).", CraftCategory.Consumables,
                    new[] { (XenoarchCraftingSetup.Copper, 1), (XenoarchCraftingSetup.Nickel, 1), (XenoarchCraftingSetup.Wiring, 1) }, SonicTools.PowerCellId, 2, null, 0, 3f),
                ("craft.depth_probe_3", "Depth Probe Mk IV (Hologram)", "Ground-penetrating hologram scan: a 3D wire view of the scanned volume with a pulsing marker per buried object. 4.5 m across and down.", CraftCategory.Upgrades,
                    new[] { (XenoarchCraftingSetup.SensorArr, 2), (XenoarchCraftingSetup.Circuit, 3), (XenoarchCraftingSetup.Platinum, 2) }, null, 1, DepthProbeDefinition.UpgradeId, 3, 10f),
            };
            var crafts = config.craftRecipes ?? new System.Collections.Generic.List<CraftRecipe>();
            int added = 0;
            foreach (var d in recipes)
            {
                string rp = XenoarchSetup.Root + "/Data/Crafting/Craft_" + d.id.Substring("craft.".Length) + ".asset";
                var r = AssetDatabase.LoadAssetAtPath<CraftRecipe>(rp);
                if (r == null) { r = ScriptableObject.CreateInstance<CraftRecipe>(); AssetDatabase.CreateAsset(r, rp); }
                r.SetIdForTooling(d.id); r.displayName = d.name; r.description = d.desc; r.category = d.cat;
                r.ingredients = d.ins.Select(i => new ItemAmount(i.Item1, i.Item2)).ToList();
                r.outputItemId = d.outId ?? ""; r.outputAmount = d.outN; r.upgradeId = d.up ?? ""; r.upgradeLevel = d.lvl;
                r.craftSeconds = d.secs; r.station = CraftStation.Ship;
                EditorUtility.SetDirty(r);
                if (!crafts.Contains(r)) { crafts.Add(r); added++; }
            }
            config.craftRecipes = crafts;
            EditorUtility.SetDirty(config);
            // Mk upgrade tracks (levels 1-3 = Mk II-IV); Mk IV needs its blueprint archived.
            const string P = XenoarchCraftingSetup.Plate, W = XenoarchCraftingSetup.Wiring, C = XenoarchCraftingSetup.Circuit, S = XenoarchCraftingSetup.SensorArr,
                G = XenoarchCraftingSetup.Gold, Pt = XenoarchCraftingSetup.Platinum;
            var mk = new (string key, string name, string up, (string, int)[][] ins, string[] bp)[]
            {
                ("excavator", "Sonic Excavator", SonicTools.UpgradeExcavator, new[] { new[] { (C, 2), (G, 1) }, new[] { (S, 1), (C, 2), (Pt, 1) }, new[] { (S, 2), (C, 3), (Pt, 2) } }, new[] { null, null, "blueprint.sonic.excavator_mk4" }),
                ("brush", "Ultrasonic Brush", SonicTools.UpgradeBrush, new[] { new[] { (W, 2), (C, 1) }, new[] { (C, 2), (G, 1) }, new[] { (S, 1), (C, 2), (Pt, 1) } }, new[] { null, null, "blueprint.sonic.brush_mk4" }),
                ("burster", "Sonic Burster", SonicTools.UpgradeBurster, new[] { new[] { (P, 2), (C, 2) }, new[] { (P, 2), (C, 2), (G, 1) }, new[] { (S, 1), (C, 2), (Pt, 2) } }, new[] { null, null, "blueprint.sonic.burster_mk4" }),
            };
            string[] roman = { "II", "III", "IV" };
            string[] gain = { "Bigger head and reach, 10% less energy and heat.", "Bigger head and reach, 20% less energy and heat.", "Top tier: biggest head and reach, 30% less energy and heat." };
            foreach (var t in mk)
                for (int lvl = 1; lvl <= 3; lvl++)
                {
                    string id = $"craft.sonic_{t.key}_{lvl}";
                    string rp = XenoarchSetup.Root + "/Data/Crafting/Craft_" + id.Substring("craft.".Length) + ".asset";
                    var r = AssetDatabase.LoadAssetAtPath<CraftRecipe>(rp);
                    if (r == null) { r = ScriptableObject.CreateInstance<CraftRecipe>(); AssetDatabase.CreateAsset(r, rp); }
                    r.SetIdForTooling(id); r.displayName = $"{t.name} Mk {roman[lvl - 1]}"; r.description = gain[lvl - 1] + (t.bp[lvl - 1] != null ? " Needs the archived blueprint." : "");
                    r.category = CraftCategory.Upgrades;
                    r.ingredients = t.ins[lvl - 1].Select(i => new ItemAmount(i.Item1, i.Item2)).ToList();
                    r.outputItemId = ""; r.outputAmount = 1; r.upgradeId = t.up; r.upgradeLevel = lvl;
                    r.craftSeconds = 6f + lvl * 2f; r.station = CraftStation.Ship; r.unlockFact = t.bp[lvl - 1] ?? "";
                    EditorUtility.SetDirty(r);
                    if (!crafts.Contains(r)) { crafts.Add(r); added++; }
                }
            config.craftRecipes = crafts;
            EditorUtility.SetDirty(config);
            sb.AppendLine($"recipes added to config: {added}");

            // Blueprint items and their place in the cache loot (Rare / Super rare).
            foreach (var b in SonicBlueprints.All)
            {
                string bpath = ItemsDir + "/Item_" + b.itemId.Substring("item.".Length).Replace('.', '_') + ".asset";
                var bi = AssetDatabase.LoadAssetAtPath<ItemDefinition>(bpath);
                bool bnew = bi == null;
                if (bnew) { bi = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(bi, bpath); }
                bi.SetIdForTooling(b.itemId);
                bi.displayName = "Blueprint: " + b.name; bi.category = ItemCategory.DataRecord; bi.maxStack = 1; bi.uniqueInstance = false; bi.mass = 0.05f;
                if (bnew) bi.baseValue = 400;
                bi.description = "Alien-pattern schematics. Board the ship and it is archived into the Discovery Journal, unlocking the upgrade recipe.";
                if (bi.icon == null) bi.icon = AssetDatabase.LoadAssetAtPath<Sprite>(XenoarchCraftingSetup.IconFolder + "/radar-dish.png");
                EditorUtility.SetDirty(bi);
                var ents = reg.Entries.Where(e => e != null).ToList();
                if (!ents.Any(e => e.Id == bi.Id)) { ents.Add(bi); reg.SetEntries(ents); EditorUtility.SetDirty(reg); }
            }
            var loot = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Valuables.LootTableDefinition>(XenoarchSetup.Root + "/Data/Valuables/CacheLoot.asset");
            int lootAdded = 0;
            if (loot != null)
                foreach (var tier in loot.tiers)
                {
                    if (tier == null || (tier.tier != DMI.Xenoarch.Valuables.LootTier.Rare && tier.tier != DMI.Xenoarch.Valuables.LootTier.SuperRare)) continue;
                    foreach (var b in SonicBlueprints.All)
                        if (!tier.items.Any(e => e != null && e.itemId == b.itemId)) { tier.items.Add(new DMI.Xenoarch.Valuables.LootTableDefinition.LootEntry { itemId = b.itemId, min = 1, max = 1 }); lootAdded++; }
                }
            if (loot != null) EditorUtility.SetDirty(loot);
            sb.AppendLine($"blueprint items ready; added {lootAdded} loot entries to CacheLoot (loot table {(loot != null ? "found" : "missing")})");
        }

        static AudioClip Vendor(string name)
        {
            if (string.IsNullOrEmpty(name) || !AssetDatabase.IsValidFolder(VendorRoot)) return null;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:AudioClip", new[] { VendorRoot }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            }
            return null;
        }

        static AudioClip Free(string file) => file == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(Kenney + file);

        static SonicToolAudioCatalog.Entry E(SonicSfx id, string vendor, string[] extras, string kenney, float vol, StringBuilder sb)
        {
            var e = new SonicToolAudioCatalog.Entry { id = id, clip = Free(kenney), vendorClip = Vendor(vendor), volume = vol };
            var list = new System.Collections.Generic.List<AudioClip>();
            if (extras != null) foreach (var x in extras) { var c = Vendor(x); if (c != null) list.Add(c); }
            e.vendorExtras = list.ToArray();
            sb.AppendLine($"sfx {id}: vendor={(e.vendorClip != null ? vendor : "none")} (+{list.Count}) fallback={(e.clip != null ? kenney : "generated/none")}");
            return e;
        }

        static void Audio(StringBuilder sb)
        {
            if (!AssetDatabase.IsValidFolder(AudioDir)) { Directory.CreateDirectory(AudioDir); AssetDatabase.Refresh(); }
            string path = AudioDir + "/SonicToolAudio.asset";
            var cat = AssetDatabase.LoadAssetAtPath<SonicToolAudioCatalog>(path);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SonicToolAudioCatalog>(); AssetDatabase.CreateAsset(cat, path); }
            cat.entries = new[]
            {
                E(SonicSfx.Equip, "sci-fi_device_item_power_up_flash_01", new[] { "sci-fi_device_item_power_up_flash_02", "sci-fi_device_item_power_up_flash_03" }, "forceField_000.ogg", 0.6f, sb),
                E(SonicSfx.BeamLoop, "sci-fi_forcefield_hum_loop_01", new[] { "sci-fi_forcefield_hum_loop_02" }, null, 0.45f, sb),
                E(SonicSfx.FanLoop, "sci-fi_hologram_electric_buzz_01", null, null, 0.4f, sb),
                E(SonicSfx.Change, "sci-fi_forcefield_holo_power_down_01", null, "forceField_001.ogg", 0.5f, sb),
                E(SonicSfx.Overheat, "sci-fi_flash_item_light_power_zap_down_large_01", null, "forceField_004.ogg", 0.7f, sb),
                E(SonicSfx.Empty, "sci-fi_electric_pulse_power_down_01", new[] { "sci-fi_electric_pulse_power_down_02" }, "forceField_003.ogg", 0.6f, sb),
                E(SonicSfx.Jar, null, null, "impactMetal_000.ogg", 0.6f, sb),
            };
            EditorUtility.SetDirty(cat);
        }
    }
}
