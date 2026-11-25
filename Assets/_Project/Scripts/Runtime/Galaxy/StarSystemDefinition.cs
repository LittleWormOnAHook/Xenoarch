using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Star System", fileName = "System_")]
    public sealed class StarSystemDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "sys.";
        public string displayName;
        [Tooltip("Galactic coordinates in light years.")] public Vector3 galacticPosition;
        public string stellarClass = "G2V";
        public int seed;
        public bool knownByDefault = true;
        [Tooltip("FTL range (ly) needed to jump here from a neighbouring system.")] public float minFtlRangeLy;
        [Range(0f, 1f)] public float archaeologicalPotential;
        public List<CelestialBodyDefinition> bodies = new List<CelestialBodyDefinition>();

        public CelestialBodyDefinition FindBody(string id)
        {
            foreach (var b in bodies) if (b != null && b.Id == id) return b;
            return null;
        }
    }
}
