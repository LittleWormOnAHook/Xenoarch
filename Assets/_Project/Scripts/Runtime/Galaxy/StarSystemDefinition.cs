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
        [Tooltip("Discovery fact that reveals this system on the galaxy map (e.g. decoded coordinates). Empty = known by default flag only.")] public string revealFact;
        [Tooltip("Galaxy map note once revealed.")] [TextArea] public string revealNote;
        [Tooltip("FTL range (ly) needed to jump here from a neighbouring system.")] public float minFtlRangeLy;
        [Range(0f, 1f)] public float archaeologicalPotential;
        [Tooltip("Extra Nav destination (DLC / expansion content). The four core systems are built in (ExplorationService.NavSystems).")] public bool navDestination;
        public List<CelestialBodyDefinition> bodies = new List<CelestialBodyDefinition>();

        public CelestialBodyDefinition FindBody(string id)
        {
            foreach (var b in bodies) if (b != null && b.Id == id) return b;
            return null;
        }
    }
}
