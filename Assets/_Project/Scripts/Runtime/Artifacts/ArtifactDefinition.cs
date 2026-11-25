using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Artifacts
{
    /// <summary>Static artifact data. Derives from ItemDefinition so it enters the inventory as item id + instance id.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Artifacts/Artifact Definition", fileName = "Artifact_")]
    public sealed class ArtifactDefinition : ItemDefinition
    {
        protected override string DefaultIdPrefix => "artifact.";

        [Header("Artifact")]
        public string unidentifiedName = "Unidentified object";
        [TextArea(2, 4)] public string researchedDescription;
        public string civilizationId;
        public ArtifactCategory artifactCategory = ArtifactCategory.Unknown;
        public ArtifactRarity rarity = ArtifactRarity.Common;
        public string material = "Unknown alloy";
        public string ageEstimate = "Unknown";
        [Range(0f, 1f)] [Tooltip("How easily tools and careless recovery damage it.")] public float fragility = 0.5f;
        [Range(0f, 1f)] public float historicalSignificance = 0.3f;
        [Range(0f, 1f)] public float technologicalSignificance = 0.1f;
        [Range(0.05f, 1f)] [Tooltip("Condition while still buried (before tools touch it).")] public float buriedCondition = 0.85f;
        public List<ResearchOp> researchOps = new List<ResearchOp> { ResearchOp.Cleaning, ResearchOp.Identification, ResearchOp.MaterialAnalysis, ResearchOp.AgeEstimation, ResearchOp.CivilizationIdentification, ResearchOp.Restoration };
        public List<TranslationSegment> translation = new List<TranslationSegment>();
        [Header("Placeholder visual")]
        public ArtifactShape shape = ArtifactShape.Shard;
        public Color tint = new Color(0.6f, 0.62f, 0.66f);
        [Tooltip("Rough size (m) of the longest side.")] [Range(0.05f, 0.6f)] public float size = 0.2f;

        public bool HasTranslation => translation != null && translation.Count > 0;

        public static float RarityMultiplier(ArtifactRarity r) => r switch
        {
            ArtifactRarity.Common => 1f, ArtifactRarity.Uncommon => 1.6f, ArtifactRarity.Rare => 2.8f,
            ArtifactRarity.Exceptional => 5f, ArtifactRarity.Legendary => 9f, _ => 14f,
        };

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            uniqueInstance = true; category = ItemCategory.Artifact; maxStack = 1;
        }
#endif
    }
}
