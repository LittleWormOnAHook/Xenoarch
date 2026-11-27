using UnityEngine;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// Adjustable Grasshopper drive / suspension / air profile (t213u). Loaded at runtime from
    /// Resources/Vehicles/RoverPhysics_Grasshopper so the uncommitted prefab does not need a new reference.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Rover Physics Profile", fileName = "RoverPhysics_Grasshopper")]
    public sealed class RoverPhysicsProfile : ScriptableObject
    {
        public const string ResourcePath = "Vehicles/RoverPhysics_Grasshopper";

        [Header("Gravity")]
        [Tooltip("Multiplies the current planet / environment gravity.")]
        public float gravityMultiplier = 1f;
        [Tooltip("Extra downward acceleration while grounded, as a fraction of planet g, so the rover sits firmly.")]
        public float extraDownforceG = 0.35f;
        [Tooltip("Downforce from speed (acceleration = coeff * planar speed squared).")]
        public float downforceCoeff = 0.12f;

        [Header("Mass and centre")]
        public float mass = 2800f;
        [Tooltip("Metres to drop the centre of mass below the COM marker.")]
        public float comDown = 0.30f;

        [Header("Suspension (world metres / N / N-s)")]
        public float spring = 70000f;
        public float damper = 5500f;
        [Tooltip("Suspension travel in world metres.")]
        public float travel = 0.28f;
        [Tooltip("Spring target (0 = full droop, 1 = fully compressed).")]
        public float targetPosition = 0.45f;
        [Tooltip("0 applies forces at the contact patch (less wheel wobble).")]
        public float forceAppPointDistance = 0f;

        [Header("Grip")]
        public float sidewaysStiffness = 2.0f;
        public float forwardStiffness = 1.8f;

        [Header("Speed (km/h and m/s^2)")]
        public float maxSpeedKph = 40f;
        public float boostSpeedKph = 80f;
        public float acceleration = 5.5f;
        public float brakeDecel = 12f;
        public float maxSteer = 30f;
        public float highSpeedSteer = 8f;
        public float boostSeconds = 3.5f;
        public float boostRechargeSeconds = 7f;

        [Header("Air / bounce")]
        [Tooltip("Anti-flip uprighting torque.")]
        public float antiFlip = 16f;
        [Tooltip("Extra velocity damping per second while airborne (not a launch killer on jumps, just air control).")]
        public float airborneDamping = 1.6f;
        [Tooltip("Clamp on upward speed so hills don't throw the rover into space (m/s).")]
        public float maxAirUpSpeed = 5.5f;
        [Tooltip("Clamp on downward speed (m/s).")]
        public float maxAirDownSpeed = 14f;

        [Header("Follow camera (metres); mid is the current 14 m view.")]
        public float chaseDistanceNear = 8f;
        public float chaseDistanceMid = 14f;
        public float chaseDistanceFar = 22f;
        public float chaseHeightNear = 3.2f;
        public float chaseHeightMid = 4.8f;
        public float chaseHeightFar = 6.8f;

        public float MaxSpeedMs => maxSpeedKph / 3.6f;
        public float BoostSpeedMs => boostSpeedKph / 3.6f;

        public static RoverPhysicsProfile LoadOrDefault()
        {
            var p = Resources.Load<RoverPhysicsProfile>(ResourcePath);
            return p != null ? p : CreateInstance<RoverPhysicsProfile>();
        }

        public void DistanceForZoom(int step, out float distance, out float height)
        {
            step = Mathf.Clamp(step, 0, 2);
            if (step == 0) { distance = chaseDistanceNear; height = chaseHeightNear; }
            else if (step == 2) { distance = chaseDistanceFar; height = chaseHeightFar; }
            else { distance = chaseDistanceMid; height = chaseHeightMid; }
        }
    }
}
