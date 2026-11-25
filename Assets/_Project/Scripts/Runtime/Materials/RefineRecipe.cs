using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Materials
{
    [Serializable]
    public struct ItemAmount
    {
        public string itemId;
        [Min(1)] public int count;
        public ItemAmount(string id, int n) { itemId = id; count = n; }
    }

    /// <summary>One refiner process: inputs are taken when it starts, outputs arrive after <see cref="seconds"/>.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Materials/Refine Recipe", fileName = "Recipe_")]
    public sealed class RefineRecipe : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "recipe.";
        public string displayName;
        public List<ItemAmount> inputs = new List<ItemAmount>();
        public List<ItemAmount> outputs = new List<ItemAmount>();
        [Min(1f)] public float seconds = 10f;
    }
}
