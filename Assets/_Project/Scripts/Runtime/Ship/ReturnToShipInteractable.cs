using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>On a surface / station: board the ship again (arrives at the ship airlock spawn).</summary>
    public sealed class ReturnToShipInteractable : MonoBehaviour, IInteractable
    {
        public string prompt = "Return to ship";
        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => 1f;
        public string GetPrompt(in InteractionContext c) => prompt;
        public float GetHoldDuration(in InteractionContext c) => 0.5f;
        public bool TryInteract(in InteractionContext c) => ShipTravel.ReturnToShip();
    }
}
