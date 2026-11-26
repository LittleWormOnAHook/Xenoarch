using System.Collections.Generic;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Crafting;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Materials;
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Batch E data: layered ground (strata) per body, rare-metal ores and the refiner recipes that make ingots,
    /// crafted components, ASH-style crafting recipes (items, tools, ship/scanner upgrades), and the white
    /// game-icons.net icons (CC BY 3.0, see Art/Icons/GameIcons/SOURCES.md). Additive: keeps hand-edited values
    /// on existing assets except the fields it owns, never overwrites an icon that is already set.
    /// </summary>
    public static class XenoarchCraftingSetup
    {
        const string MatFolder = XenoarchPhase1CSetup.MaterialsFolder;
        const string CraftFolder = XenoarchSetup.Root + "/Data/Crafting";
        const string CompFolder = XenoarchSetup.Root + "/Data/Items/Components";
        public const string IconFolder = XenoarchSetup.Root + "/Art/Icons/GameIcons";
        const string Moon = "body.sol.moon", Mars = "body.sol.mars";

        public const string NickelOre = "material.nickel_iron_ore", CopperOre = "material.copper_sulfide", GoldOre = "material.auriferous_quartz", PlatOre = "material.platinoid_ore";
        public const string Nickel = "material.nickel_ingot", Copper = "material.copper_ingot", Gold = "material.gold_ingot", Platinum = "material.platinum_ingot";
        const string Alloy = "material.alloy_ingot", Iron = "material.iron_ore", Ice = "material.water_ice", Reagent = "material.lab_reagent";
        public const string Plate = "item.component.alloy_plate", Wiring = "item.component.wiring", Circuit = "item.component.circuit", SensorArr = "item.component.sensor_array";

        static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "material.regolith", "stone-pile" }, { "material.water_ice", "ice-cube" }, { "material.helium3", "atom" }, { "material.iron_ore", "ore" },
            { "material.mineral_salts", "powder" }, { "material.methane_frost", "snowflake-2" }, { "material.fuel_cell", "fuel-tank" }, { "material.lab_reagent", "test-tubes" },
            { "material.alloy_ingot", "metal-bar" }, { NickelOre, "rock" }, { CopperOre, "minerals" }, { GoldOre, "crystal-cluster" }, { PlatOre, "gems" },
            { Nickel, "metal-bar" }, { Copper, "metal-bar" }, { Gold, "gold-bar" }, { Platinum, "gold-bar" },
            { Plate, "metal-plate" }, { Wiring, "wire-coil" }, { Circuit, "circuitry" }, { SensorArr, "radar-dish" },
        };

        public static void Run(XenoarchConfig config, StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(CraftFolder); XenoarchSetup.EnsureFolder(CompFolder);
            int sprites = ImportIcons();
            var reg = config.itemRegistry;
            var entries = reg.Entries.Where(e => e != null).ToList();
            void Reg(ItemDefinition d) { if (!entries.Any(e => e.Id == d.Id)) entries.Add(d); }

            // ---------------- ores + ingots
            var mats = new (string id, string name, string desc, bool refined, Color tint, int value)[]
            {
                (NickelOre, "Nickel-Iron Ore", "Dense metallic rock from a metre or more down. Smelt it for nickel (and some iron ore back).", false, new Color(0.55f, 0.5f, 0.42f), 8),
                (CopperOre, "Copper Sulfide Ore", "Greenish sulfide ore from the middle layers. Smelt it for copper.", false, new Color(0.35f, 0.6f, 0.45f), 8),
                (GoldOre, "Auriferous Quartz", "Quartz veined with gold, found deep down. Leach it with water for gold.", false, new Color(0.95f, 0.8f, 0.35f), 20),
                (PlatOre, "Platinoid Ore", "Rare deep ore with platinum-group metals. Refine it with lab reagent.", false, new Color(0.8f, 0.82f, 0.88f), 35),
                (Nickel, "Nickel Ingot", "Refined nickel. Alloy plates and ship parts.", true, new Color(0.72f, 0.72f, 0.66f), 30),
                (Copper, "Copper Ingot", "Refined copper. Wiring and electronics.", true, new Color(0.85f, 0.5f, 0.3f), 30),
                (Gold, "Gold Ingot", "Refined gold. Circuit boards, and very valuable.", true, new Color(1f, 0.82f, 0.3f), 120),
                (Platinum, "Platinum Ingot", "Refined platinum. Sensors and high-end upgrades.", true, new Color(0.88f, 0.9f, 0.95f), 200),
            };
            foreach (var d in mats)
            {
                var a = Asset<MaterialDefinition>($"{MatFolder}/Material_{d.id.Substring("material.".Length)}.asset");
                a.SetIdForTooling(d.id); a.displayName = d.name; a.description = d.desc;
                a.category = ItemCategory.Resource; a.uniqueInstance = false; a.maxStack = 50; a.mass = d.refined ? 1f : 0.8f; a.baseValue = d.value;
                a.refined = d.refined; a.fuelValue = 0f; a.reagentValue = 0; a.tint = d.tint;
                a.bodies = new List<string>(); a.nodesPerScene = 0; // only found by digging (strata)
                EditorUtility.SetDirty(a); Reg(a);
            }

            // ---------------- components
            var comps = new (string id, string name, string desc, int value)[]
            {
                (Plate, "Alloy Plate", "Pressed alloy and nickel plate. Hull, hold and tool frames.", 60),
                (Wiring, "Wiring Bundle", "Copper wiring loom.", 45),
                (Circuit, "Circuit Board", "Gold-contact control board.", 160),
                (SensorArr, "Sensor Array", "Platinum sensor head for scanners.", 320),
            };
            foreach (var c in comps)
            {
                var a = Asset<ItemDefinition>($"{CompFolder}/Item_{c.id.Substring("item.component.".Length)}.asset");
                a.SetIdForTooling(c.id); a.displayName = c.name; a.description = c.desc;
                a.category = ItemCategory.Misc; a.uniqueInstance = false; a.maxStack = 20; a.mass = 0.5f; a.baseValue = c.value;
                EditorUtility.SetDirty(a); Reg(a);
            }
            reg.SetEntries(entries); EditorUtility.SetDirty(reg);

            // ---------------- refiner recipes (ores -> ingots)
            var rr = new (string id, string name, (string, int)[] ins, (string, int)[] outs, float secs)[]
            {
                ("recipe.smelt_nickel", "Smelt nickel", new[] { (NickelOre, 3) }, new[] { (Nickel, 1), (Iron, 1) }, 18f),
                ("recipe.smelt_copper", "Smelt copper", new[] { (CopperOre, 3) }, new[] { (Copper, 1) }, 15f),
                ("recipe.leach_gold", "Leach gold", new[] { (GoldOre, 4), (Ice, 1) }, new[] { (Gold, 1) }, 25f),
                ("recipe.refine_platinum", "Refine platinum", new[] { (PlatOre, 4), (Reagent, 1) }, new[] { (Platinum, 1) }, 35f),
            };
            foreach (var d in rr)
            {
                var r = Asset<RefineRecipe>($"{MatFolder}/Recipe_{d.id.Substring("recipe.".Length)}.asset");
                r.SetIdForTooling(d.id); r.displayName = d.name; r.seconds = d.secs;
                r.inputs = d.ins.Select(i => new ItemAmount(i.Item1, i.Item2)).ToList();
                r.outputs = d.outs.Select(o => new ItemAmount(o.Item1, o.Item2)).ToList();
                EditorUtility.SetDirty(r);
                if (!config.refineRecipes.Contains(r)) config.refineRecipes.Add(r);
            }

            // ---------------- strata
            StrataBand B(string label, float a, float b, string id, float vol, float chance = 1f) => new StrataBand { label = label, minDepth = a, maxDepth = b, itemId = id, volumePerUnit = vol, chance = chance };
            var moon = Asset<StrataProfile>($"{MatFolder}/Strata_moon.asset");
            moon.bodyIds = new List<string> { Moon };
            moon.bands = new List<StrataBand>
            {
                B("regolith", 0f, 0.7f, "material.regolith", 0.12f),
                B("iron-bearing basalt", 0.4f, 1.6f, Iron, 0.2f, 0.8f),
                B("nickel-iron layer", 0.9f, 3f, NickelOre, 0.25f, 0.7f),
                B("gold-bearing quartz", 1.4f, 4f, GoldOre, 0.35f, 0.4f),
                B("platinoid seam", 1.8f, 5f, PlatOre, 0.4f, 0.25f),
            };
            var mars = Asset<StrataProfile>($"{MatFolder}/Strata_mars.asset");
            mars.bodyIds = new List<string> { Mars };
            mars.bands = new List<StrataBand>
            {
                B("regolith", 0f, 0.6f, "material.regolith", 0.12f),
                B("perchlorate salts", 0.3f, 1.2f, "material.mineral_salts", 0.18f, 0.8f),
                B("copper sulfide layer", 0.6f, 2.2f, CopperOre, 0.22f, 0.75f),
                B("nickel-iron layer", 1f, 3f, NickelOre, 0.25f, 0.6f),
                B("gold-bearing quartz", 1.5f, 4f, GoldOre, 0.35f, 0.4f),
                B("platinoid seam", 2f, 5f, PlatOre, 0.4f, 0.2f),
            };
            EditorUtility.SetDirty(moon); EditorUtility.SetDirty(mars);
            config.strata = new List<StrataProfile> { moon, mars };

            // ---------------- crafting
            var cr = new (string id, string name, string desc, CraftCategory cat, (string, int)[] ins, string outId, int outN, string up, int lvl, float secs, CraftStation st)[]
            {
                ("craft.alloy_plate", "Alloy Plate", "Press alloy with nickel into a plate.", CraftCategory.Components, new[] { (Alloy, 2), (Nickel, 1) }, Plate, 1, null, 0, 3f, CraftStation.Ship),
                ("craft.wiring", "Wiring Bundle", "Draw copper into a wiring loom.", CraftCategory.Components, new[] { (Copper, 2) }, Wiring, 1, null, 0, 2f, CraftStation.Anywhere),
                ("craft.circuit", "Circuit Board", "Solder a control board with gold contacts.", CraftCategory.Components, new[] { (Wiring, 1), (Gold, 1) }, Circuit, 1, null, 0, 4f, CraftStation.Ship),
                ("craft.sensor_array", "Sensor Array", "Build a platinum sensor head.", CraftCategory.Components, new[] { (Circuit, 1), (Platinum, 1) }, SensorArr, 1, null, 0, 6f, CraftStation.Ship),
                ("craft.sample_containers", "Sample Containers", "Two sealed sample containers from one alloy ingot.", CraftCategory.Consumables, new[] { (Alloy, 1) }, "item.consumable.sample_container", 2, null, 0, 2f, CraftStation.Anywhere),
                ("craft.powered_drill", "Powered Drill", "A drill for rock the shovel can't cut.", CraftCategory.Tools, new[] { (Plate, 2), (Circuit, 1) }, "item.tool.powered_drill", 1, null, 0, 8f, CraftStation.Ship),
                ("craft.hold_1", "Storage Expansion I", "More slots in every ship crate (24 to 36).", CraftCategory.Upgrades, new[] { (Plate, 4) }, null, 0, GameSession.UpgradeHold, 1, 6f, CraftStation.Ship),
                ("craft.hold_2", "Storage Expansion II", "More slots in every ship crate (36 to 48).", CraftCategory.Upgrades, new[] { (Plate, 6), (Nickel, 2), (Circuit, 1) }, null, 0, GameSession.UpgradeHold, 2, 8f, CraftStation.Ship),
                ("craft.hold_3", "Storage Expansion III", "More slots in every ship crate (48 to 72).", CraftCategory.Upgrades, new[] { (Plate, 8), (Circuit, 2), (Platinum, 1) }, null, 0, GameSession.UpgradeHold, 3, 10f, CraftStation.Ship),
                ("craft.scanner_1", "Handheld Scanner Range I", "Handheld scan radius 30 to 45 m.", CraftCategory.Upgrades, new[] { (Wiring, 2), (Circuit, 1) }, null, 0, GameSession.UpgradeHandheldScanner, 1, 5f, CraftStation.Ship),
                ("craft.scanner_2", "Handheld Scanner Range II", "Handheld scan radius 45 to 60 m.", CraftCategory.Upgrades, new[] { (Circuit, 2), (Gold, 1) }, null, 0, GameSession.UpgradeHandheldScanner, 2, 6f, CraftStation.Ship),
                ("craft.scanner_3", "Handheld Scanner Range III", "Handheld scan radius 60 to 80 m.", CraftCategory.Upgrades, new[] { (SensorArr, 1), (Circuit, 2) }, null, 0, GameSession.UpgradeHandheldScanner, 3, 8f, CraftStation.Ship),
                ("craft.orbital_1", "Orbital Scanner Range I", "Orbital scan radius 400 to 700 m.", CraftCategory.Upgrades, new[] { (SensorArr, 1), (Plate, 2) }, null, 0, GameSession.UpgradeOrbitalScanner, 1, 8f, CraftStation.Ship),
                ("craft.orbital_2", "Orbital Scanner Range II", "Orbital scan radius 700 to 1000 m.", CraftCategory.Upgrades, new[] { (SensorArr, 2), (Platinum, 1) }, null, 0, GameSession.UpgradeOrbitalScanner, 2, 10f, CraftStation.Ship),
                ("craft.orbital_3", "Orbital Scanner Range III", "Orbital scan radius 1000 to 1500 m.", CraftCategory.Upgrades, new[] { (SensorArr, 3), (Platinum, 2) }, null, 0, GameSession.UpgradeOrbitalScanner, 3, 12f, CraftStation.Ship),
            };
            var crafts = new List<CraftRecipe>();
            foreach (var d in cr)
            {
                var r = Asset<CraftRecipe>($"{CraftFolder}/Craft_{d.id.Substring("craft.".Length)}.asset");
                r.SetIdForTooling(d.id); r.displayName = d.name; r.description = d.desc; r.category = d.cat;
                r.ingredients = d.ins.Select(i => new ItemAmount(i.Item1, i.Item2)).ToList();
                r.outputItemId = d.outId ?? ""; r.outputAmount = Mathf.Max(1, d.outN); r.upgradeId = d.up ?? ""; r.upgradeLevel = d.lvl;
                r.craftSeconds = d.secs; r.station = d.st;
                EditorUtility.SetDirty(r); crafts.Add(r);
            }
            config.craftRecipes = crafts;
            EditorUtility.SetDirty(config);
            XenoarchDepthProbeSetup.Run(config, log); // ZEN-16: keep the Depth Probe item + recipes after a rebuild

            // ---------------- icons (only where none is set)
            int assigned = 0;
            foreach (var item in reg.Entries.Where(e => e != null))
            {
                if (item.icon != null) continue;
                string file = Icons.TryGetValue(item.Id, out var f) ? f : item.category == ItemCategory.Valuable ? "diamond" : null;
                if (file == null) continue;
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/{file}.png");
                if (sp == null) continue;
                item.icon = sp; EditorUtility.SetDirty(item); assigned++;
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"Batch E: {mats.Length} ores/ingots, {comps.Length} components, {rr.Length} refiner recipes, strata for Moon ({moon.bands.Count} bands) and Mars ({mars.bands.Count}), {crafts.Count} craft recipes, {sprites} icon sprites, {assigned} icons assigned");
        }

        static int ImportIcons()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || !ti.alphaIsTransparency)
                {
                    ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                    ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.maxTextureSize = 128; ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
                n++;
            }
            return n;
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ------------------------------------------------------------------ smoke
        public static void Validate(System.Action<bool, string> check)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.strata == null || config.strata.Count == 0 || config.craftRecipes.Count == 0) { check(true, "Batch E: setup not run yet - skipped"); return; }
            var reg = config.itemRegistry;
            bool Resolves(string id) => reg.TryGetItem(id, out _);
            check(config.strata.All(p => p != null && p.bands.Count >= 4 && p.bands.All(b => Resolves(b.itemId))) && StrataProfile.For(config.strata, Moon) != null && StrataProfile.For(config.strata, Mars) != null,
                $"E strata: layered ground for Moon and Mars, every band's material resolves");
            check(config.craftRecipes.All(r => r != null && r.ingredients.All(i => Resolves(i.itemId)) && (r.IsUpgrade || Resolves(r.outputItemId))),
                $"E crafting: {config.craftRecipes.Count} recipes, all items resolve");
            var noIcon = reg.Entries.Where(e => e is MaterialDefinition || (e != null && e.Id.StartsWith("item.component."))).Where(e => e.icon == null).Select(e => e.Id).ToList();
            check(noIcon.Count == 0, $"E icons: every material and component has an icon{(noIcon.Count > 0 ? " MISSING: " + string.Join(", ", noIcon) : "")}");
            check(System.IO.File.Exists(IconFolder + "/SOURCES.md"), "E icons: SOURCES.md (game-icons.net CC BY 3.0 attribution) present");

            var s = new GameSession(config, reg, new DMI.Xenoarch.Save.MemorySaveStorage());
            s.StartNewGame();
            // strata: dig 40 deep cells at a Moon site -> ores
            var rng = new System.Random(3);
            var cuts = Enumerable.Range(0, 40).Select(i => (y: 4 + i % 10, frac: 1f)).ToList(); // 1.1 .. 3.4 m
            var found = new Dictionary<string, int>();
            for (int k = 0; k < 6; k++)
                foreach (var (id, n) in StrataYield.Dig(s.Materials, StrataProfile.For(config.strata, Moon), "site.test", 0.25f, cuts, rng)) { found.TryGetValue(id, out var g); found[id] = g + n; }
            check(found.ContainsKey(NickelOre) && found.Keys.All(Resolves), $"E strata: digging deep at a Moon site yields ores ({string.Join(", ", found.Select(kv => $"{kv.Value} {kv.Key.Replace("material.", "")}"))})");

            // refiner takes ore from a crate, product lands in the backpack
            var general = s.Hold(StorageRole.General);
            general.Add(NickelOre, 3);
            var smelt = s.Materials.Recipe("recipe.smelt_nickel");
            bool started = s.Materials.StartRefine(smelt, out var why);
            s.Materials.Tick(60f);
            check(started && general.Count(NickelOre) == 0 && s.Inventory.Count(Nickel) == 1, $"E refiner: nickel ore from the General crate -> nickel ingot in the backpack ({why})");

            // crafting: wiring from crate copper; upgrade raises crate slots
            var c = s.Crafting;
            general.Add(Copper, 2);
            var wiring = config.craftRecipes.First(r => r.Id == "craft.wiring");
            bool ok = c.TryCraft(wiring); c.Tick(10f);
            check(ok && s.Inventory.Count(Wiring) == 1 && general.Count(Copper) == 0, "E crafting: timed craft takes ingredients from the crates, product to the backpack");
            var hold1 = config.craftRecipes.First(r => r.Id == "craft.hold_1");
            s.Inventory.Add(Plate, 4);
            s.Location.sceneName = "Moon_Excavation_01";
            string away = c.MissingReason(hold1);
            s.Location.sceneName = SceneNames.ShipStarter;
            ok = c.TryCraft(hold1); c.Tick(30f);
            check(away != null && away.Contains("workshop") && ok && s.Exploration.Ship.holdLevel == 1 && general.Capacity == 36 && s.Inventory.Count(Plate) == 0,
                $"E crafting: ship upgrade needs the ship, then raises crate slots 24 -> {general.Capacity}");
            var hold3 = config.craftRecipes.First(r => r.Id == "craft.hold_3");
            check(c.MissingReason(hold3) == "Install the previous upgrade first", "E crafting: upgrades install in order");
        }
    }
}
