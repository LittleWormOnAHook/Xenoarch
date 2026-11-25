using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Items
{
    public enum ItemCategory { Tool, Equipment, Artifact, Resource, Consumable, ShipModule, DataRecord, Misc }

    /// <summary>
    /// Static item definition. Artifacts (Phase 1C) will get their own definition type with richer data; they still
    /// enter the inventory through an item id + instance id, so the inventory does not change.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Items/Item Definition", fileName = "Item_")]
    public class ItemDefinition : DefinitionAsset
    {
        public string displayName;
        [TextArea(2, 5)] public string description;
        public Sprite icon;
        public ItemCategory category = ItemCategory.Misc;
        [Min(1)] public int maxStack = 1;
        [Tooltip("Every unit is a distinct instance with its own id (artifacts, recovered technology). Never stacks.")]
        public bool uniqueInstance;
        [Min(0f)] public float mass = 1f;
        [Min(0)] public int baseValue;
        [Tooltip("Optional world/pickup visual.")]
        public GameObject worldPrefab;

        public bool IsStackable => !uniqueInstance && maxStack > 1;
        public int EffectiveMaxStack => uniqueInstance ? 1 : Mathf.Max(1, maxStack);
        public string DisplayNameOrId => string.IsNullOrEmpty(displayName) ? Id : displayName;

        protected override string DefaultIdPrefix => "item.";
    }
}
