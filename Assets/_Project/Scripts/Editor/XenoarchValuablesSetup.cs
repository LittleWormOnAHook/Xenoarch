using System.Collections.Generic;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Valuables;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>Batch D: valuable item definitions, the cache loot table (common .. super rare) and config wiring. Additive.</summary>
    public static class XenoarchValuablesSetup
    {
        public const string Folder = XenoarchSetup.Root + "/Data/Items/Valuables";
        public const string LootPath = XenoarchSetup.Root + "/Data/Valuables/CacheLoot.asset";

        static readonly (string id, string name, string desc, int value, LootTier tier, int min, int max)[] Defs =
        {
            ("item.valuable.copper_nugget", "Native Copper Nugget", "Pure copper, weathered green. Sells well at any station.", 25, LootTier.Common, 1, 3),
            ("item.valuable.meteoric_iron", "Meteoric Iron Fragment", "Nickel-iron from an old impact. Dense and pitted.", 30, LootTier.Common, 1, 3),
            ("item.valuable.silver_nugget", "Silver Nugget", "A rounded lump of native silver.", 70, LootTier.Uncommon, 1, 2),
            ("item.valuable.quartz_geode", "Quartz Geode", "Hollow stone lined with clear crystal. Collectors pay for intact ones.", 90, LootTier.Uncommon, 1, 2),
            ("item.valuable.gold_nugget", "Gold Nugget", "Heavy, bright and unmistakable.", 220, LootTier.Rare, 1, 2),
            ("item.valuable.platinum_nugget", "Platinum Nugget", "Grey, dense platinum-group metal. Prized for ship electronics.", 300, LootTier.Rare, 1, 1),
            ("item.valuable.vael_coin", "Vael Token", "A stamped alloy disc of Vael make. Not part of any signal - just lost, long ago.", 350, LootTier.Rare, 1, 1),
            ("item.valuable.iridium_core", "Iridium Meteorite Core", "The heart of a rare iron meteorite, laced with iridium.", 900, LootTier.SuperRare, 1, 1),
            ("item.valuable.starstone", "Vael Starstone", "A cut crystal that holds a faint light of its own. Nobody knows what it was for.", 1200, LootTier.SuperRare, 1, 1),
        };

        public static void Run(StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.itemRegistry == null) { log?.AppendLine("Valuables: config missing"); return; }
            XenoarchSetup.EnsureFolder(Folder);
            XenoarchSetup.EnsureFolder(XenoarchSetup.Root + "/Data/Valuables");
            var items = new List<ItemDefinition>();
            foreach (var d in Defs)
            {
                string path = $"{Folder}/Item_{d.id.Substring("item.valuable.".Length)}.asset";
                var a = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (a == null) { a = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(a, path); }
                a.SetIdForTooling(d.id); a.displayName = d.name; a.description = d.desc; a.category = ItemCategory.Valuable;
                a.maxStack = 20; a.uniqueInstance = false; a.mass = 0.3f; a.baseValue = d.value;
                EditorUtility.SetDirty(a); items.Add(a);
            }
            var reg = config.itemRegistry;
            var entries = reg.Entries.Where(e => e != null).ToList();
            foreach (var i in items) if (!entries.Any(e => e.Id == i.Id)) entries.Add(i);
            reg.SetEntries(entries); EditorUtility.SetDirty(reg);

            var loot = AssetDatabase.LoadAssetAtPath<LootTableDefinition>(LootPath);
            if (loot == null) { loot = ScriptableObject.CreateInstance<LootTableDefinition>(); AssetDatabase.CreateAsset(loot, LootPath); }
            loot.SetIdForTooling("loot.valuable_cache");
            if (loot.tiers == null || loot.tiers.Count == 0) // keep his tuning once it exists
            {
                float[] weights = { 60f, 28f, 10f, 2f };
                loot.tiers = new List<LootTierEntry>();
                foreach (LootTier t in System.Enum.GetValues(typeof(LootTier)))
                    loot.tiers.Add(new LootTierEntry
                    {
                        tier = t, weight = weights[(int)t], extraRolls = t == LootTier.Common ? 1 : 0,
                        items = Defs.Where(d => d.tier == t).Select(d => new LootEntry { itemId = d.id, min = d.min, max = d.max }).ToList(),
                    });
            }
            EditorUtility.SetDirty(loot);
            if (config.valuableLoot == null) config.valuableLoot = loot;
            if (config.valuableSites.perBody <= 0 && config.valuableSites.spacing <= 0f) config.valuableSites = ValuableSiteSettings.Default;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            log?.AppendLine($"Valuables: {items.Count} items, loot table {loot.tiers.Count} tiers");
        }

        public static void Validate(System.Action<bool, string> check)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            var loot = config != null ? config.valuableLoot : null;
            if (loot == null) { check(true, "4D valuables: setup not run yet - skipped"); return; }
            bool allIds = loot.tiers.SelectMany(t => t.items).All(e => config.itemRegistry.TryGetItem(e.itemId, out var d) && (d.category == ItemCategory.Valuable || d.category == ItemCategory.Resource));
            check(loot.tiers.Count == 4 && loot.tiers.All(t => t.items.Count > 0) && allIds, $"4D valuables: loot table has 4 tiers, every entry is a registered Valuable or Resource item");
            var rng = new System.Random(5); var counts = new int[4];
            for (int i = 0; i < 4000; i++) counts[(int)loot.RollTier(rng)]++;
            check(counts[0] > counts[1] && counts[1] > counts[2] && counts[2] > counts[3] && counts[3] > 0, $"4D valuables: tier rolls {string.Join("/", counts)} (common > uncommon > rare > super rare > 0)");

            // Generation on a synthetic flat region with a steep strip and avoid zones.
            LandingZonePicker.HeightFn h = (float x, float z, out float y, out Vector3 n) => { y = 0f; n = x > 150f ? Quaternion.Euler(0, 0, 40f) * Vector3.up : Vector3.up; return true; };
            var s = ValuableSiteSettings.Default; var avoid = new List<(Vector3, float)> { (new Vector3(0, 0, 0), 30f) };
            var region = new Rect(-250, -250, 500, 500);
            var a = ValuableService.Generate("body.test", 1337, region, s, h, avoid);
            var b = ValuableService.Generate("body.test", 1337, region, s, h, avoid);
            bool same = a.Count == b.Count && a.Zip(b, (p, q) => p.id == q.id && p.position == q.position).All(v => v);
            bool spaced = a.All(p => a.All(q => p.id == q.id || Vector2.Distance(new Vector2(p.position.x, p.position.z), new Vector2(q.position.x, q.position.z)) >= s.spacing - 0.01f));
            bool clear = a.All(p => new Vector2(p.position.x, p.position.z).magnitude >= 30f + s.clearance && p.position.x <= 150f);
            check(a.Count == s.perBody && same && spaced && clear, $"4D valuables: {a.Count} caches generated deterministically, spaced, off steep ground and clear of dig/landing zones");

            var save = new ValuableService(null);
            save.Reveal(a[0].id); save.Collect(a[0].id, LootTier.Rare);
            check(save.IsRevealed(a[0].id) && save.IsCollected(a[0].id) && !save.IsRevealed(a[1].id) && save.State(a[0].id).tier == (int)LootTier.Rare, "4D valuables: reveal/collect state");
            var sd = config.surfaceScanner;
            check(sd != null && sd.rangeByLevel != null && sd.rangeByLevel.Length > 1 && ScannerStatsCheck(sd.rangeByLevel), "4D scan radius: handheld radius has upgrade levels (rising)");
            var ship = config.starterShip;
            check(ship != null && ship.orbitalScanRadiusByLevel != null && ScannerStatsCheck(ship.orbitalScanRadiusByLevel), "4D scan radius: orbital radius has upgrade levels (rising)");
        }

        static bool ScannerStatsCheck(float[] t)
        {
            for (int i = 1; i < t.Length; i++) if (DMI.Xenoarch.Galaxy.ScannerStats.Pick(t, i, 0f) <= DMI.Xenoarch.Galaxy.ScannerStats.Pick(t, i - 1, 0f)) return false;
            return DMI.Xenoarch.Galaxy.ScannerStats.Pick(t, 99, 0f) == t[t.Length - 1];
        }
    }
}
