using System.Collections.Generic;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Materials
{
    /// <summary>
    /// A stackable material: raw pickups gathered on planets (bodies + node counts) and refined products made in the
    /// ship's refiner. <see cref="fuelValue"/> &gt; 0 means it can go straight into the ship's tank (fuel units per item);
    /// <see cref="reagentValue"/> &gt; 0 means the research lab can consume it as reagent units.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Materials/Material", fileName = "Material_")]
    public sealed class MaterialDefinition : ItemDefinition
    {
        [Header("Material")]
        [Tooltip("Refined in the ship's refiner (not found on planets).")] public bool refined;
        [Tooltip("Ship fuel units per item when refuelling (0 = not a fuel).")] [Min(0f)] public float fuelValue;
        [Tooltip("Lab reagent units per item (0 = not a reagent).")] [Min(0)] public int reagentValue;
        public Color tint = Color.grey;

        [Header("Planet pickups (raw materials)")]
        [Tooltip("Bodies where nodes of this material spawn (body ids).")] public List<string> bodies = new List<string>();
        [Tooltip("Nodes spawned per surface scene.")] [Min(0)] public int nodesPerScene = 4;
        [Tooltip("Items per node (min, max).")] public Vector2Int unitsPerNode = new Vector2Int(2, 4);
        [Tooltip("Play seconds before a harvested node comes back.")] [Min(0f)] public float respawnSeconds = 1200f;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate(); // keeps the stable-id default (was hidden before)
            category = ItemCategory.Resource;
            uniqueInstance = false;
            if (maxStack < 2) maxStack = 250;
        }
#endif
    }
}
