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
            Chamber(log);
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

        /// <summary>
        /// Mars buried chamber (t140u): under the Canyon Relay dig site floor. Dig down to the site floor at cell (6, 6)
        /// to uncover a sealed door; through it a tunnel leads to a room with 2 m of diggable soil and four Vael finds.
        /// Adds an optional chain node. Creates the asset only if missing (hand edits kept).
        /// </summary>
        static void Chamber(StringBuilder log)
        {
            string folder = XenoarchSetup.ResourcesFolder + "/" + DMI.Xenoarch.Excavation.ChamberDefinition.ResourceFolder;
            XenoarchSetup.EnsureFolder(folder);
            string path = folder + "/Chamber_mars_canyon_relay.asset";
            var def = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Excavation.ChamberDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<DMI.Xenoarch.Excavation.ChamberDefinition>();
                def.SetIdForTooling("chamber.mars.canyon_relay");
                def.displayName = "Vael Relay Chamber"; def.hostSiteId = "site.mars.canyon_relay";
                def.entranceCell = new Vector2Int(6, 6); def.yaw = 0f;
                def.artifacts = new List<DMI.Xenoarch.Galaxy.SiteArtifactSpawn>
                {
                    new DMI.Xenoarch.Galaxy.SiteArtifactSpawn { artifactId = "artifact.mars_glyph_tablet", cell = new Vector3Int(8, 3, 9), footprint = 1 },
                    new DMI.Xenoarch.Galaxy.SiteArtifactSpawn { artifactId = "artifact.vael_effigy", cell = new Vector3Int(19, 5, 18), footprint = 1 },
                    new DMI.Xenoarch.Galaxy.SiteArtifactSpawn { artifactId = "artifact.mars_offering_bowl", cell = new Vector3Int(14, 4, 21), footprint = 1 },
                    new DMI.Xenoarch.Galaxy.SiteArtifactSpawn { artifactId = "artifact.mars_bead_string", cell = new Vector3Int(21, 2, 7), footprint = 1 },
                };
                def.openedMessage = "The Vael seal gives way. A tunnel leads down into a buried chamber.";
                AssetDatabase.CreateAsset(def, path);
                log.AppendLine("created " + path);
            }
            else log.AppendLine("kept " + path);
            var chain = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Discovery.DiscoveryChainDefinition>(XenoarchSetup.Root + "/Data/Discovery/Chain_MoonMars.asset");
            if (chain != null && !chain.nodes.Exists(n => n.id == "chamber"))
            {
                chain.nodes.Add(new DMI.Xenoarch.Discovery.DiscoveryNode
                {
                    id = "chamber", title = "Open the buried chamber under the Canyon Relay",
                    factKey = DMI.Xenoarch.Discovery.Facts.ChamberOpened(def.Id), threshold = 1f,
                    prerequisites = new List<string> { "mars_scan" }, optional = true,
                    reward = new DMI.Xenoarch.Discovery.DiscoveryReward { journal = "A sealed Vael door lay under the Canyon Relay floor. Beyond it: a tunnel and a buried room." },
                });
                EditorUtility.SetDirty(chain);
                log.AppendLine("chain: added chamber node");
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
