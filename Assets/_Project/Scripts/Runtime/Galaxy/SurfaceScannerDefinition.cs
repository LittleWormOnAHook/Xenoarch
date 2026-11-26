using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>Stats of a handheld surface scanner tool (keyed by its item id).</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Tools/Surface Scanner", fileName = "Scanner_")]
    public sealed class SurfaceScannerDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "scanner.";
        public string itemId = "item.tool.handheld_scanner";
        [Tooltip("Horizontal detection range (m).")] public float range = 30f;
        [Tooltip("Upgradable scan radius: range (m) per handheld scanner level (0 = base). Empty = range.")] public float[] rangeByLevel = { 30f, 45f, 60f, 80f };
        [Tooltip("Max detectable depth (m).")] public float maxDepth = 3f;
        [Range(0.1f, 1f)] [Tooltip("1 = exact readings; lower adds distance/bearing error.")] public float accuracy = 0.7f;
        [Tooltip("Within this distance a detected site is pinpointed and marked.")] public float pinpointRadius = 3f;
        public float cooldown = 2f;
    }
}
