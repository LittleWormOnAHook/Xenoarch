using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// One dig tool tier as data (put assets in Resources/Excavation/DigTools). Slot order sets the hotkey (first tool = 2).
    /// Crude tools take big, fast chunks and can jar or damage finds; fine tools take smaller, slower bites and are safe.
    /// New tiers (e.g. a sonic cleaner for ultra-fine work) are just new assets plus their inventory item.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Excavation/Dig Tool", fileName = "DigTool_")]
    public sealed class DigToolDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "digtool.";
        [Tooltip("Hotkey order: 0 = key 2, 1 = key 3, ... (max 8 tools).")] public int slotOrder;
        [Tooltip("Untick to keep the asset but leave it out of the game.")] public bool enabledInGame = true;
        public DigToolSpec spec;
    }
}
