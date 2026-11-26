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
            AssetDatabase.SaveAssets();
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
