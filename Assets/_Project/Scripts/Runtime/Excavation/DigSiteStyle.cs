using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Data-driven dig-site type: the soil bands a site is dug through (top to bottom, last band runs to the bedrock
    /// floor), their hardness (which tools can cut them), colours, the Digger texture each band paints, and the border
    /// colour shown once the site is pinpointed. Sites reference a style; new site types are just new assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Excavation/Dig Site Style", fileName = "DigStyle_")]
    public sealed class DigSiteStyle : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "digstyle.";
        public string displayName;
        [TextArea] public string description;
        public List<SoilLayer> layers = new List<SoilLayer>();
        public Color borderColor = new Color(1f, 0.62f, 0.15f, 0.9f);

        public SoilLayer[] Profile() => layers != null && layers.Count > 0 ? layers.ToArray() : null;
    }
}
