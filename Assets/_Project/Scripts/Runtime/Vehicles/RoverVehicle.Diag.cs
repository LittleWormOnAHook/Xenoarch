#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// DEV / TESTING only: scripted drive input for the rover test (replaces keyboard/gamepad in FixedUpdate while
    /// <see cref="Active"/>). Off by default; only the dev rover test (XenoarchDevLaunch) turns it on.
    /// </summary>
    public static class RoverDevInput
    {
        public static bool Active;
        public static float Throttle, Steer;
        public static bool Hand;
        public static void Clear() { Active = false; Throttle = 0f; Steer = 0f; Hand = false; }
    }

    public sealed partial class RoverVehicle
    {
        /// <summary>Seated, powered and driveable.</summary>
        public bool DiagDriving => Live && _state == State.Driving && _powered;
        public bool DiagParked => Live && _state == State.Parked;
        public Rigidbody DiagBody => _rb;
        /// <summary>Dev rover test: a cloned profile used instead of the asset for rovers created while set.</summary>
        public static RoverPhysicsProfile DiagProfileOverride;
        public Vector3 DiagComWorld => _rb != null ? _rb.worldCenterOfMass : transform.position;
        /// <summary>The visual-forward frame ([Rover Rig]); falls back to the root.</summary>
        public Transform DiagRig => rig != null ? rig : transform;
    }
}
#endif
