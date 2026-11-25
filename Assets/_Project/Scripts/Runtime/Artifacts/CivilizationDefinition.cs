using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Artifacts
{
    [CreateAssetMenu(menuName = "Xenoarch/Artifacts/Civilization", fileName = "Civ_")]
    public sealed class CivilizationDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "civ.";
        public string displayName;
        public string unknownName = "Unknown culture";
        public string historicalPeriod;
        public string languageName;
        public string writingSystem;
        public string architectureStyle;
        [TextArea(2, 4)] public string techTraits;
        [TextArea(2, 4)] public string culturalTraits;
        public string preferredMaterials;
        public string disappearanceStatus = "Unknown";
    }
}
