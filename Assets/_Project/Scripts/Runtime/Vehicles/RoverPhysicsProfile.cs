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
        [Tooltip("Metres to drop the centre of mass below the axle midpoint (0-2, wide range). Lower = less pitch.")]
        public float comDown = 0.55f;
        [Tooltip("Metres to shift the centre of mass along forward from the axle midpoint (negative = toward the rear). -2 to 2.")]
        public float comForeAft = 0f;

        [Header("Suspension (world metres / N / N-s)")]
        public float spring = 70000f;
        public float damper = 5500f;
        [Tooltip("Minimum damping as a fraction of critical (0 still uses a 1.6 PhysX floor, 0.2-3). 1.6 kills pogo-bounce; the authored damper is still used if it is firmer.")]
        public float damperRatio = 1.6f;

        [Header("Planet adaptation (wide ranges; 0 disables a term)")]
        [Tooltip("Scale spring, damper, anti-pitch and grip with local gravity and terrain/temperature.")]
        public bool adaptToPlanet = true;
        [Tooltip("Clamps gravity/9.81 used to scale the springs (0.05-4).")]
        public float minGravityScale = 0.15f;
        public float maxGravityScale = 2.5f;
        [Tooltip("While grounded, add extra downward acceleration so wheel load never falls below this (m/s^2). Keeps low-g worlds plantable. 0 = off.")]
        public float minWheelLoad = 3.5f;
        [Tooltip("Ice / very cold grip multiplier (0-2).")]
        public float iceGrip = 0.55f;
        [Tooltip("Loose sand / desert grip multiplier (0-2).")]
        public float sandGrip = 0.75f;
        [Tooltip("Rock / highland grip multiplier (0-2).")]
        public float rockGrip = 1.05f;
        public float coldGripBelowC = -80f;
        public float coldGrip = 0.7f;
        public float hotGripAboveC = 80f;
        public float hotGrip = 0.85f;
        [Tooltip("Extra planar drag on ice (1/s).")]
        public float iceRoll = 0.04f;
        [Tooltip("Extra planar drag on sand (1/s).")]
        public float sandRoll = 0.08f;
        public float defaultRoll = 0.015f;
        [Tooltip("Top-speed scale on ice (0.3-1.5).")]
        public float iceSpeed = 0.8f;
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
        [Tooltip("Seconds to ease the throttle up to the stick/key value (0.05-2). Smooths squat on takeoff.")]
        public float throttleRise = 0.45f;
        [Tooltip("Seconds to ease the throttle back to zero (0.05-2).")]
        public float throttleFall = 0.22f;
        [Tooltip("Do not apply reverse/forward drive torque until speed along the body is below this (m/s). Brakes only until then.")]
        public float reverseEngageSpeed = 0.35f;

        [Header("Air / bounce")]
        [Header("Pitch (wide ranges; 0 disables each one)")]
        [Tooltip("Damps pitch rate (0-40). Stops the ends from pumping up when you accelerate or brake.")]
        public float antiPitch = 18f;
        [Tooltip("Nose-down torque while accelerating, as a multiplier of longitudinal accel (0-40). Kills wheelies.")]
        public float antiSquat = 10f;
        [Tooltip("On the brakes: stops the front lifting and leaves only a mild nose-dive (0-40). Higher = firmer, less pitch.")]
        public float antiDive = 16f;
        [Tooltip("Multiplies pitch inertia (1-4). Higher = the nose is slower to rise or dip.")]
        public float pitchInertia = 1.6f;
        [Tooltip("Rigidbody angular damping (0-8). Was 1.2.")]
        public float angularDamping = 2.2f;

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
