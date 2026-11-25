using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    public enum CelestialKind { Star, Planet, DwarfPlanet, Moon, Belt, Station }

    /// <summary>Environment of a place: gravity, atmosphere, lighting/fog and an optional URP Volume profile.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Environment Profile", fileName = "Env_")]
    public sealed class EnvironmentProfile : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "env.";
        public string displayName;
        [Tooltip("Downward acceleration, m/s^2 (Earth 9.81).")] public float gravity = 9.81f;
        public string atmosphere = "None";
        public bool breathable;
        public float surfaceTemperatureC;
        public bool applyLighting = true;
        public Color ambientColor = new Color(0.25f, 0.25f, 0.28f);
        public bool fog;
        public Color fogColor = Color.gray;
        [Min(0f)] public float fogDensity = 0.01f;
        [Tooltip("Optional URP Volume profile applied by a global volume while in this environment.")]
        public UnityEngine.Rendering.VolumeProfile volumeProfile;
        [Tooltip("Optional sky for this environment's scenes (surfaces). Ship/orbit scenes use the body's orbit skybox instead.")]
        public Material skybox;
    }

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
        [TextArea] public string description;

        public bool HasLanding => !string.IsNullOrEmpty(sceneName);
    }

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

    /// <summary>Root of the galaxy data. Handcrafted systems are listed; generated ones derive ids from (parent, seed, index).</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Galaxy", fileName = "Galaxy")]
    public sealed class GalaxyDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "galaxy.";
        public int seed = 1337;
        public List<StarSystemDefinition> systems = new List<StarSystemDefinition>();

        public StarSystemDefinition FindSystem(string id)
        {
            foreach (var s in systems) if (s != null && s.Id == id) return s;
            return null;
        }

        public CelestialBodyDefinition FindBody(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in systems) { var b = s != null ? s.FindBody(id) : null; if (b != null) return b; }
            return null;
        }

        public StarSystemDefinition SystemOf(string bodyId)
        {
            foreach (var s in systems) if (s != null && s.FindBody(bodyId) != null) return s;
            return null;
        }

        public CelestialBodyDefinition FindBodyByScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            foreach (var s in systems) if (s != null) foreach (var b in s.bodies) if (b != null && b.sceneName == sceneName) return b;
            return null;
        }

        /// <summary>Deterministic id for generated content: same (parent, galaxy seed, index) gives the same id every session.</summary>
        public static string GeneratedId(string prefix, string parentId, int galaxySeed, int index)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in parentId ?? "") h = (h ^ ch) * 16777619;
                h = (h ^ (uint)galaxySeed) * 16777619;
                h = (h ^ (uint)index) * 16777619;
                return $"{prefix}{h:x8}";
            }
        }
    }

    /// <summary>Base stats of a ship hull. Runtime values live in <see cref="ShipState"/>.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Ship Definition", fileName = "Ship_")]
    public sealed class ShipDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "ship.";
        public string displayName = "Constellation Mk I";
        public float fuelCapacity = 100f;
        [Tooltip("Fuel per AU of in-system travel.")] public float fuelPerAU = 10f;
        [Tooltip("Minimum fuel for any hop (orbit changes, moons).")] public float minimumHopFuel = 1f;
        public float ftlRangeLy = 0f;
        [Tooltip("Orbital scanner level: max orbital reveal passes / resolution.")] public int scannerLevel = 1;
        public int moduleSlots = 4;
        public int cargoSlots = 12;
        public List<string> startingModules = new List<string>();
        public string startingBodyId = "body.sol.earth";
        public EnvironmentProfile interiorEnvironment;
        [Tooltip("Exterior-only prefab (travel views, docking bay).")] public GameObject exteriorPrefab;
        [Tooltip("Walkable interior prefab used in Ship_Starter.")] public GameObject interiorPrefab;
        [Header("FTL sequence")]
        [Tooltip("Optional particle prefab shown through the cockpit window during the jump (Creepy Cat Particle_Dust_Space).")] public GameObject ftlDustPrefab;
        [Tooltip("Optional engine glow prefab during spool-up (Creepy Cat Particle_Afterburner_Small).")] public GameObject ftlSpoolPrefab;
        [Tooltip("Material for the star streaks (URP Particles). Empty = runtime additive material.")] public Material ftlStreakMaterial;
        public AudioClip ftlAudio;
    }

    /// <summary>An archaeological site on a body. Position is in the landing scene's world space (y snapped to ground).</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Archaeological Site", fileName = "Site_")]
    public sealed class ArchaeologicalSiteDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "site.";
        public string displayName;
        public string bodyId;
        public string sceneName;
        public Vector3 position;
        [Min(0.5f)] public float radius = 4f;
        [Tooltip("Depth of the main find below the surface (m).")] public float depth = 1.5f;
        [Range(1, 5)] public int difficulty = 1;
        [Tooltip("Orbital scan passes needed before this anomaly shows (stronger signatures need fewer).")] [Range(1, 4)] public int orbitalPassesNeeded = 1;
        [Range(0f, 1f)] public float signalStrength = 0.7f;
        public int seed;
        [Tooltip("Phase 1C stub: artifact/loot table id.")] public string artifactTableId;
        [TextArea] public string description;
    }

    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Site Registry", fileName = "SiteRegistry")]
    public sealed class SiteRegistry : DefinitionRegistry<ArchaeologicalSiteDefinition> { }

    /// <summary>Stats of a handheld surface scanner tool (keyed by its item id).</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Tools/Surface Scanner", fileName = "Scanner_")]
    public sealed class SurfaceScannerDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "scanner.";
        public string itemId = "item.tool.handheld_scanner";
        [Tooltip("Horizontal detection range (m).")] public float range = 30f;
        [Tooltip("Max detectable depth (m).")] public float maxDepth = 3f;
        [Range(0.1f, 1f)] [Tooltip("1 = exact readings; lower adds distance/bearing error.")] public float accuracy = 0.7f;
        [Tooltip("Within this distance a detected site is pinpointed and marked.")] public float pinpointRadius = 3f;
        public float cooldown = 2f;
    }
}
