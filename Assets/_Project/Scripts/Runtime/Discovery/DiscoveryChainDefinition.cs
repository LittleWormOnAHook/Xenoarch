using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Discovery
{
    [CreateAssetMenu(menuName = "Xenoarch/Discovery/Discovery Chain", fileName = "Chain_")]
    public sealed class DiscoveryChainDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "chain.";
        public string title;
        public List<DiscoveryNode> nodes = new List<DiscoveryNode>();
    }
}
