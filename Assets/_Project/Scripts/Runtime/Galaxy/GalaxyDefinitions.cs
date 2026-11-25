using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    public enum CelestialKind { Star, Planet, DwarfPlanet, Moon, Belt, Station }

    [System.Serializable]
    public sealed class SiteArtifactSpawn
    {
        public string artifactId;
        public Vector3Int cell;
        [Tooltip("Minimum cells per side; big artifacts grow to fit their size (max 3).")] [Range(1, 3)] public int footprint = 1;
    }

}

