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
        [Tooltip("Unused since Phase 1C-2d (the swirl tunnel replaces the spool glow). Kept for save/asset compatibility.")] public GameObject ftlSpoolPrefab;
        [Tooltip("Material for the star streaks (URP Particles). Empty = runtime additive material.")] public Material ftlStreakMaterial;
        public AudioClip ftlAudio;
        [Tooltip("Seconds for the descent from orbit to the landing zone (and docking). Shown as a timer bar with beeps and a touchdown chime.")] public float landingSeconds = 8f;
        [Tooltip("Seconds to lift off / undock back to orbit.")] public float takeoffSeconds = 5f;
    }
}
