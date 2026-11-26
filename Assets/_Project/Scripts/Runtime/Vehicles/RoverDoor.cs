using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>Rear door of the rover: E opens it, then E "Enter Rover".</summary>
    public sealed class RoverDoor : MonoBehaviour, IInteractable
    {
        public RoverVehicle rover;
        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => rover != null && c.Rig is FirstPersonController f && rover.CanEnter(f) ? 1.2f : -1f;
        public string GetPrompt(in InteractionContext c) => rover != null && rover.DoorOpen ? "Enter Rover" : "Open rear door";
        public float GetHoldDuration(in InteractionContext c) => 0f;
        public bool TryInteract(in InteractionContext c)
        {
            if (rover == null || !(c.Rig is FirstPersonController f)) return false;
            if (!rover.DoorOpen) { rover.SetDoor(true); return true; }
            return rover.Enter(f);
        }
    }
}
