using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Materials;
using UnityEngine;

namespace DMI.Xenoarch.Crafting
{
    public enum CraftCategory { Components, Tools, Equipment, Upgrades, Consumables }
    /// <summary>Anywhere = hand crafting; Ship = needs the ship's workshop (crafting while aboard).</summary>
    public enum CraftStation { Anywhere, Ship }

    /// <summary>
    /// Crafting recipe. Same shape as ASH's recipe (id, name, description, category, ingredients, output + amount,
    /// craft time, station, optional upgrade instead of an item), with Xenoarch item ids and an optional unlock fact.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Crafting/Craft Recipe", fileName = "Craft_")]
    public sealed class CraftRecipe : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "craft.";
        public string displayName;
        [TextArea(2, 3)] public string description;
        public CraftCategory category = CraftCategory.Components;
        public List<ItemAmount> ingredients = new List<ItemAmount>();
        [Tooltip("Item made (empty for upgrade recipes).")] public string outputItemId;
        [Min(1)] public int outputAmount = 1;
        [Tooltip("If set, crafting applies this upgrade instead of making an item: ship.hold, scanner.handheld, scanner.orbital.")] public string upgradeId;
        [Tooltip("Upgrade level this recipe takes you to (must be exactly one above the current level).")] [Min(0)] public int upgradeLevel;
        [Range(0f, 30f)] public float craftSeconds = 2f;
        public CraftStation station = CraftStation.Anywhere;
        [Tooltip("Discovery fact that unlocks this recipe (empty = known from the start).")] public string unlockFact;

        public bool IsUpgrade => !string.IsNullOrEmpty(upgradeId) && upgradeLevel > 0;
        public string Title => string.IsNullOrEmpty(displayName) ? Id : displayName;
    }
}
