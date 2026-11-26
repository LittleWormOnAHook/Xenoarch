using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>The landed shuttle on a planet: hold E to fly back up (fade to black, arrive at the ship's airlock bay).</summary>
    public sealed class ShuttleInteractable : MonoBehaviour, IInteractable
    {
        public string prompt = "Board shuttle: return to ship";
        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => 1f;
        public string GetPrompt(in InteractionContext c) => prompt;
        public float GetHoldDuration(in InteractionContext c) => 0.5f;
        public bool TryInteract(in InteractionContext c) => ShipTravel.ReturnToShip();
    }
}
