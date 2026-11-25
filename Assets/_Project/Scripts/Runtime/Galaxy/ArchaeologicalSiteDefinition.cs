using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>An archaeological site on a body. Position is in the landing scene's world space (y snapped to ground).</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Archaeological Site", fileName = "Site_")]
    public sealed class ArchaeologicalSiteDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "site.";
        public string displayName;
        public string bodyId;
        public string sceneName;
        public Vector3 position;
        [Min(0.5f)] public float radius = 4f;
        [Tooltip("Depth of the main find below the surface (m).")] public float depth = 1.5f;
        [Range(1, 5)] public int difficulty = 1;
        [Tooltip("Orbital scan passes needed before this anomaly shows (stronger signatures need fewer).")] [Range(1, 4)] public int orbitalPassesNeeded = 1;
        [Range(0f, 1f)] public float signalStrength = 0.7f;
        public int seed;
        [Tooltip("Phase 1C stub: artifact/loot table id.")] public string artifactTableId;
        [TextArea] public string description;
        [Header("Excavation (Phase 1C)")]
        [Range(0.1f, 0.5f)] public float cellSize = 0.25f;
        [Tooltip("Dig-site type: soil bands, hardness, colours (Excavation/DigSiteStyle). Empty = default regolith.")]
        public DMI.Xenoarch.Excavation.DigSiteStyle style;
        [Tooltip("Buried finds: artifact id + cell (x, z across the site from its min corner; y = layer down from the surface).")]
        public List<SiteArtifactSpawn> artifacts = new List<SiteArtifactSpawn>();
        [Tooltip("Discovery fact needed before scans can reveal this site (chain gating). Empty = always revealable.")]
        public string gateFact;
    }
}
