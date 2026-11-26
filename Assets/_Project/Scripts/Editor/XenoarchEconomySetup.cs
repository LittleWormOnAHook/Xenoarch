using System.Collections.Generic;
using System.IO;
using System.Text;
using DMI.Xenoarch.Economy;
using DMI.Xenoarch.Items;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1D: creates the Trade Hub (Sol) vendors in Resources/Vendors: Artifact Buyer, Equipment Supplier and
    /// Refuelling Service, each anchored on the scene's existing placeholder station (no scene edits). Only creates
    /// missing assets, so hand-tuned prices and stock survive reruns. Prices are placeholders for the cost pass.
    /// </summary>
    public static class XenoarchEconomySetup
    {
        public const string Trigger = "Temp/xenoarch_economy.trigger";
        const string Folder = XenoarchSetup.ResourcesFolder + "/" + VendorDefinition.ResourceFolder;

        [MenuItem("Tools/Xenoarch/Setup/Economy (Trade Hub vendors)")]
        public static void Menu() => RunFromTrigger();

        public static void RunFromTrigger()
        {
            var sb = new StringBuilder();
            Run(sb);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xenoarch_economy.txt", sb.ToString());
            Debug.Log("[Xenoarch] Economy setup:\n" + sb);
        }

        static VendorStock S(string id, int price, int rank = 0, bool unique = false) => new VendorStock { itemId = id, price = price, minRank = rank, unique = unique };

        public static void Run(StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(Folder);
            Make("Vendor_artifact_buyer", "vendor.sol.artifact_buyer", v =>
            {
                v.displayName = "Artifact Buyer"; v.kind = VendorKind.ArtifactBuyer; v.anchorName = "Artifact Buyer (vendor)";
                v.greeting = "Licensed dealer in recovered antiquities. Better condition and finished research raise the price.";
                v.buys = new List<ItemCategory> { ItemCategory.Artifact, ItemCategory.Valuable };
                v.buyMultiplier = 0.8f; v.bonusPerRank = 0.03f;
            }, log);
            Make("Vendor_equipment_supplier", "vendor.sol.equipment_supplier", v =>
            {
                v.displayName = "Equipment Supplier"; v.kind = VendorKind.EquipmentSupplier; v.anchorName = "Equipment Supplier (vendor)";
                v.greeting = "Field gear, parts and consumables. Some stock is only offered to explorers with a reputation.";
                v.buys = new List<ItemCategory> { ItemCategory.Resource };
                v.buyMultiplier = 0.5f; v.bonusPerRank = 0.02f;
                v.stock = new List<VendorStock>
                {
                    S("item.consumable.sample_container", 15), S("material.lab_reagent", 20), S("material.fuel_cell", 60),
                    S("item.component.alloy_plate", 40), S("item.component.wiring", 35), S("item.component.circuit", 90, 1),
                    S("item.component.sensor_array", 180, 2), S("item.tool.precision_tool", 250, 1),
                    S("item.tool.depth_probe", 400, 1, true), S("item.tool.powered_drill", 500, 2, true),
                };
            }, log);
            Make("Vendor_refuelling", "vendor.sol.refuelling", v =>
            {
                v.displayName = "Refuelling Service"; v.kind = VendorKind.Refuelling; v.anchorName = "Refuelling Service";
                v.greeting = "Docking-bay refuelling, billed per unit.";
                v.fuelPricePerUnit = 4f;
            }, log);
            // Batch 1D-2: upgrades for credits on the Equipment Supplier (added once; hand edits kept).
            var eq = AssetDatabase.LoadAssetAtPath<VendorDefinition>(Folder + "/Vendor_equipment_supplier.asset");
            if (eq != null && (eq.upgrades == null || eq.upgrades.Count == 0))
            {
                eq.upgrades = new List<VendorUpgrade>
                {
                    new VendorUpgrade { upgradeId = "ship.hold", label = "Ship storage", priceByLevel = new[] { 800, 1600, 3200 }, minRank = 0 },
                    new VendorUpgrade { upgradeId = "scanner.handheld", label = "Handheld scanner range", priceByLevel = new[] { 600, 1200, 2400 }, minRank = 1 },
                    new VendorUpgrade { upgradeId = "scanner.orbital", label = "Orbital scan radius", priceByLevel = new[] { 900, 1800, 3600 }, minRank = 1 },
                    new VendorUpgrade { upgradeId = "scanner.depth_probe", label = "Depth probe", priceByLevel = new[] { 700, 1400 }, minRank = 2 },
                };
                EditorUtility.SetDirty(eq);
                log.AppendLine("added 4 upgrades to Vendor_equipment_supplier");
            }
            AlienTech(log);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Phase 1D alien tech: the Vael Sensor Lattice (Mars, Canyon Relay) becomes an installable handheld-scanner device
        /// (+1 tier). The Moon-Mars chain's "Analyse the sensor module" node no longer grants the tier itself; a new
        /// "Install the sensor module" node (fact tech.installed) logs it. Rerunnable.
        /// </summary>
        static void AlienTech(StringBuilder log)
        {
            const string LatticeId = "artifact.mars_sensor_lattice";
            var lattice = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Artifacts.ArtifactDefinition>(XenoarchSetup.Root + "/Data/Artifacts/Artifact_mars_sensor_lattice.asset");
            if (lattice == null) { log.AppendLine("sensor lattice asset not found"); return; }
            if (lattice.installTarget != DMI.Xenoarch.Artifacts.TechInstallTarget.HandheldScanner)
            {
                lattice.installTarget = DMI.Xenoarch.Artifacts.TechInstallTarget.HandheldScanner; lattice.installBonus = 1;
                lattice.researchedDescription = "Adaptable sensor lattice. Install it at the research lab to add +1 handheld scanner tier (range, accuracy and depth).";
                if (!lattice.researchOps.Contains(DMI.Xenoarch.Artifacts.ResearchOp.TechnologyAnalysis)) lattice.researchOps.Add(DMI.Xenoarch.Artifacts.ResearchOp.TechnologyAnalysis);
                EditorUtility.SetDirty(lattice);
                log.AppendLine("sensor lattice: installable in the handheld scanner (+1)");
            }
            var chain = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Discovery.DiscoveryChainDefinition>(XenoarchSetup.Root + "/Data/Discovery/Chain_MoonMars.asset");
            if (chain == null) { log.AppendLine("Chain_MoonMars not found"); return; }
            var tech = chain.nodes.Find(n => n.id == "sensor_tech");
            if (tech != null && tech.reward != null && tech.reward.scannerLevelBonus != 0)
            {
                tech.reward.scannerLevelBonus = 0;
                tech.reward.journal = "Sensor lattice analysed: it can be fitted to the handheld scanner at the research lab.";
                EditorUtility.SetDirty(chain);
                log.AppendLine("chain: sensor_tech no longer grants the scanner tier");
            }
            if (!chain.nodes.Exists(n => n.id == "sensor_install"))
            {
                chain.nodes.Add(new DMI.Xenoarch.Discovery.DiscoveryNode
                {
                    id = "sensor_install", title = "Install the sensor module",
                    factKey = DMI.Xenoarch.Discovery.Facts.Installed(LatticeId), threshold = 1f,
                    prerequisites = new List<string> { "sensor_tech" }, optional = true,
                    reward = new DMI.Xenoarch.Discovery.DiscoveryReward { journal = "Sensor lattice installed: handheld scanner tier +1." },
                });
                EditorUtility.SetDirty(chain);
                log.AppendLine("chain: added sensor_install node");
            }
        }

        static void Make(string file, string id, System.Action<VendorDefinition> fill, StringBuilder log)
        {
            var path = Folder + "/" + file + ".asset";
            if (AssetDatabase.LoadAssetAtPath<VendorDefinition>(path) != null) { log.AppendLine($"kept {file}"); return; }
            var v = ScriptableObject.CreateInstance<VendorDefinition>();
            v.SetIdForTooling(id); fill(v);
            AssetDatabase.CreateAsset(v, path);
            log.AppendLine($"created {file} ({v.displayName}, anchor '{v.anchorName}')");
        }
    }
}
