using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Artifacts
{
    public enum ArtifactCategory { Cultural, Tool, Jewelry, Ceremonial, Document, Tablet, DataCrystal, MechanicalComponent, ScientificInstrument, NavigationDevice, PowerSource, AncientTechnology, Unknown }
    public enum ArtifactRarity { Common, Uncommon, Rare, Exceptional, Legendary, Unique }
    public enum ArtifactShape { Tablet, Shard, Rod, Disc, Box, Crystal, Ring, Device }

    /// <summary>Lab research operations (Artifact_And_Research_System). Prerequisites/durations live in <see cref="ResearchRules"/>.</summary>
    public enum ResearchOp { Cleaning, Identification, MaterialAnalysis, AgeEstimation, CivilizationIdentification, Restoration, Translation, TechnologyAnalysis }

    /// <summary>One segment of a document's text, revealed once translation reaches <see cref="threshold"/>.</summary>
    [Serializable]
    public sealed class TranslationSegment
    {
        [Range(0f, 1f)] public float threshold = 0.2f;
        [TextArea(2, 4)] public string text;
    }

    /// <summary>Lab rules for Phase 1 (Mk I lab). Durations are short on purpose while testing.</summary>
    public static class ResearchRules
    {
        public static float Duration(ResearchOp op) => op switch
        {
            ResearchOp.Cleaning => 6f, ResearchOp.Identification => 10f, ResearchOp.MaterialAnalysis => 8f,
            ResearchOp.AgeEstimation => 8f, ResearchOp.CivilizationIdentification => 12f, ResearchOp.Restoration => 15f,
            ResearchOp.Translation => 15f, _ => 18f,
        };

        /// <summary>Lab reagent units an operation consumes (paid when it starts; see Economy doc, Materials and refining).</summary>
        public static int ReagentCost(ResearchOp op) => op switch
        {
            ResearchOp.CivilizationIdentification => 2, ResearchOp.Translation => 2,
            ResearchOp.Restoration => 3, ResearchOp.TechnologyAnalysis => 3, _ => 1,
        };

        public static ResearchOp? Prerequisite(ResearchOp op) => op switch
        {
            ResearchOp.Cleaning => null,
            ResearchOp.Identification => ResearchOp.Cleaning,
            ResearchOp.Restoration => ResearchOp.Cleaning,
            ResearchOp.Translation => ResearchOp.CivilizationIdentification,
            _ => ResearchOp.Identification,
        };

        public static bool Repeatable(ResearchOp op) => op == ResearchOp.Translation;

        public static string Label(ResearchOp op) => op switch
        {
            ResearchOp.MaterialAnalysis => "Material analysis", ResearchOp.AgeEstimation => "Age estimation",
            ResearchOp.CivilizationIdentification => "Civilization identification", ResearchOp.TechnologyAnalysis => "Technology analysis",
            _ => op.ToString(),
        };

        /// <summary>Translation gained by one run: better language knowledge and condition translate faster.</summary>
        public static float TranslationStep(float languageKnowledge, float condition) =>
            (0.2f + 0.15f * Mathf.Clamp01(languageKnowledge)) * (0.5f + 0.5f * Mathf.Clamp01(condition));
    }
}
