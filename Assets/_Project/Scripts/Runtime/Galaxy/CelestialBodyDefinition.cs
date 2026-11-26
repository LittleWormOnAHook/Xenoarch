using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>A star, planet, moon, belt or station. Ids: body.&lt;system&gt;.&lt;name&gt;.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Celestial Body", fileName = "Body_")]
    public sealed class CelestialBodyDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "body.";
        public string displayName;
        public CelestialKind kind = CelestialKind.Planet;
        [Tooltip("Parent body id (moons/stations), empty for bodies orbiting the star.")] public string parentId;
        [Tooltip("Map layout / travel distance only: mean distance from the star in AU (moons: offset from parent).")] public float orbitAU;
        public float orbitPeriodDays;
        public float radiusKm;
        [Tooltip("Surface gravity in g (informational; the environment profile drives the player).")] public float gravityG = 1f;
        public EnvironmentProfile environment;
        [Tooltip("Landing / docking scene (empty = no landing zone yet: orbit only).")] public string sceneName;
        public string arrivalSpawnId = PlayerSpawnIds.Default;
        public bool discoveredByDefault = true;
        [Range(0f, 1f)] public float archaeologicalPotential;
        public int seed;
        public Color mapColor = Color.white;
        [Tooltip("Sky seen from the ship while parked in orbit of / docked at this body (empty = galaxy default deep space).")]
        public Material orbitSkybox;
        [Tooltip("Planet sphere shown outside the ship window while in orbit of this body (used when there is no orbitSkybox). Optional.")]
        public OrbitViewDefinition orbitView;
        [TextArea] public string description;
        [Header("Surface map (baked by setup from the landing scene's terrain)")]
        [Tooltip("Top-down relief map of the landing region (orbit scan view, minimap).")] public Texture2D surfaceMap;
        [Tooltip("World x/z rectangle covered by the surface map (x, z, width, depth).")] public Rect surfaceMapRect;
        [Tooltip("Base landing zone position (from the scene's LandingZone marker).")] public Vector3 baseLanding;
        public bool hasBaseLanding;

        public bool HasLanding => !string.IsNullOrEmpty(sceneName);
    }
}
