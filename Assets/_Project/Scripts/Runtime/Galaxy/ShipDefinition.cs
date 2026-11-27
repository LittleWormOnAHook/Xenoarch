using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
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
        [Tooltip("t186u: fuel per light year of an interstellar jump (placeholder balance).")] public float fuelPerLy = 3f;
        [Tooltip("t186u: jump range (ly) each fitted navigation drive adds (the decoded Vael navigation core).")] public float navDriveRangeLy = 15f;
        [Tooltip("Orbital scanner level: max orbital reveal passes / resolution.")] public int scannerLevel = 1;
        [Tooltip("Upgradable orbital scan radius (m around the landing region) per orbital range level (0 = base).")] public float[] orbitalScanRadiusByLevel = { 460f, 700f, 1000f, 1500f };
        public int moduleSlots = 4;
        public int cargoSlots = 12;
        public List<string> startingModules = new List<string>();
        public string startingBodyId = "body.sol.earth";
        public EnvironmentProfile interiorEnvironment;
        [Tooltip("Exterior-only prefab (travel views, docking bay).")] public GameObject exteriorPrefab;
        [Tooltip("Walkable interior prefab used in Ship_Starter.")] public GameObject interiorPrefab;
        [Header("FTL sequence")]
        [Tooltip("Optional particle prefab shown through the cockpit window during the jump (Creepy Cat Particle_Dust_Space).")] public GameObject ftlDustPrefab;
        [Tooltip("Unused since Phase 1C-2d (the swirl tunnel replaces the spool glow). Kept for save/asset compatibility.")] public GameObject ftlSpoolPrefab;
        [Tooltip("Material for the star streaks (URP Particles). Empty = runtime additive material.")] public Material ftlStreakMaterial;
        public AudioClip ftlAudio;
        [Tooltip("Seconds for the descent from orbit to the landing zone (and docking). Shown as a timer bar with beeps and a touchdown chime.")] public float landingSeconds = 8f;
        [Tooltip("Seconds to lift off / undock back to orbit.")] public float takeoffSeconds = 5f;
        [Header("Shuttle (planet landings)")]
        [Tooltip("Landed craft placed on planets (Creepy Cat Mark_2 Shuttle). Instantiated at runtime; the prefab itself is never edited.")] public GameObject shuttlePrefab;
        [Tooltip("Shuttle footprint radius (m), measured from the prefab by setup. 0 = measure at runtime.")] public float shuttleFootprint;
        [Tooltip("Added to the shuttle heading (deg) so its hatch faces the dig site.")] public float shuttleYawOffset;
        [Tooltip("Uniform scale of the landed shuttle (copied from the shuttle in Ship_Starter's bay by the ship hookup pass; 0 = prefab scale).")] public float shuttleScale;
        [Tooltip("Landing-gear children switched on when the shuttle lands; the lowest of them rests on the ground.")] public string[] shuttleGearNames = { "Gear_Left", "Gear_Right", "Gear_Behind" };
        [Tooltip("Ship hold (Storage) slots per hold upgrade level (0 = base).")] public int[] holdSlotsByLevel = { 24, 36, 48, 72 };
        [Tooltip("Landing-zone auto-pick rules (used for sites without a hand-placed LandingZone marker).")]
        public DMI.Xenoarch.Exploration.LandingZonePicker.Rules landingRules = DMI.Xenoarch.Exploration.LandingZonePicker.Rules.Default;
    }
}
