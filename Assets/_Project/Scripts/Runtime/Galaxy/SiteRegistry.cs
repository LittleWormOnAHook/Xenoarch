using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Site Registry", fileName = "SiteRegistry")]
    public sealed class SiteRegistry : DefinitionRegistry<ArchaeologicalSiteDefinition> { }
}
