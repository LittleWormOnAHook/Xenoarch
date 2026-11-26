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
        [Header("Star (sun view)")]
        [Tooltip("Star radius in solar radii (0 = built-in table by system id, else 1). Drives the sun's apparent size.")] [Min(0f)] public float starRadiusSolar;
        [Tooltip("Star luminosity in solar luminosities (0 = built-in table by system id, else 1). Drives sun brightness / light.")] [Min(0f)] public float starLuminositySolar;
        [Tooltip("Deep-space sky aboard the ship in this system when the body has no orbit sky (empty = config default, the Milky Way).")] public Material spaceSkybox;
        [Tooltip("Planet shown outside the ship window on arrival when no body is selected (e.g. an exoplanet). Optional.")] public OrbitViewDefinition arrivalOrbitView;
        public List<CelestialBodyDefinition> bodies = new List<CelestialBodyDefinition>();

        public CelestialBodyDefinition FindBody(string id)
        {
            foreach (var b in bodies) if (b != null && b.Id == id) return b;
            return null;
        }
    }
}
