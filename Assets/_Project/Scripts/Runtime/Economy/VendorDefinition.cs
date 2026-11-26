using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Economy
{
    public enum VendorKind { ArtifactBuyer, EquipmentSupplier, Refuelling, Shipyard, Museum, University, PrivateCollector, InformationBroker, Trader }

    [Serializable]
    public sealed class VendorStock
    {
        public string itemId;
        [Min(0)] public int price = 100;
        [Tooltip("Notoriety rank needed to see / buy it (0 = everyone).")] public int minRank;
        [Tooltip("Sold once per game (recorded in the wallet).")] public bool unique;
    }

    [Serializable]
    public sealed class VendorUpgrade
    {
        [Tooltip("GameSession upgrade id (ship.hold, scanner.handheld, scanner.orbital, scanner.depth_probe).")] public string upgradeId;
        public string label;
        [Tooltip("Price of the next level, indexed by the current level (last entry repeats).")] public int[] priceByLevel = { 500 };
        public int minRank;
    }

    /// <summary>
    /// Phase 1D vendor (data in Resources/Vendors). The <see cref="VendorDirector"/> turns the scene object named
    /// <see cref="anchorName"/> in <see cref="sceneName"/> into a <see cref="VendorTerminal"/> at runtime (no scene edits),
    /// so new vendors on new stations are data only.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Economy/Vendor", fileName = "Vendor_")]
    public sealed class VendorDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "vendor.";
        public string displayName = "Vendor";
        public VendorKind kind = VendorKind.Trader;
        [TextArea] public string greeting;
        [Header("Where")]
        public string sceneName = "TradeHub_Sol";
        [Tooltip("Scene object that becomes this vendor's terminal (e.g. a placeholder station).")] public string anchorName;
        [Header("Buying from the player")]
        [Tooltip("Item categories this vendor buys from your backpack.")] public List<ItemCategory> buys = new List<ItemCategory>();
        [Tooltip("Fraction of an item's value paid (artifacts: their appraised value).")] [Range(0f, 2f)] public float buyMultiplier = 0.8f;
        [Tooltip("Extra paid per notoriety rank (fraction).")] [Range(0f, 0.2f)] public float bonusPerRank = 0.03f;
        [Header("Selling to the player")]
        public List<VendorStock> stock = new List<VendorStock>();
        [Tooltip("Ship / equipment upgrades for credits (the same upgrades the workshop crafts).")] public List<VendorUpgrade> upgrades = new List<VendorUpgrade>();
        [Header("Services")]
        [Tooltip("Credits per unit of ship fuel (0 = no refuelling).")] [Min(0)] public float fuelPricePerUnit;

        public const string ResourceFolder = "Vendors";
        public static VendorDefinition[] All() => Resources.LoadAll<VendorDefinition>(ResourceFolder);
    }
}
