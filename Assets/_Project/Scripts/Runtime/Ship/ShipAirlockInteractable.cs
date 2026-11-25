using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>Ship airlock: disembark onto the body the ship is landed on / docked at. Sealed while in orbit.</summary>
    public sealed class ShipAirlockInteractable : MonoBehaviour, IInteractable
    {
        public Transform InteractionTransform => transform;

        static ExplorationService X => XenoarchRuntime.Session?.Exploration;

        public float GetPriority(in InteractionContext c) => X != null ? 1f : -1f;

        public string GetPrompt(in InteractionContext c)
        {
            var x = X; var b = x?.CurrentBody;
            if (b == null) return "Airlock";
            if (x.Ship.docked) return $"Disembark: {b.displayName}";
            if (x.Ship.landed) return $"Disembark onto {b.displayName}";
            return $"Airlock sealed ({ShipTravel.LocationText(x)}). Land via the Navigation Terminal";
        }

        public float GetHoldDuration(in InteractionContext c) => X != null && (X.Ship.landed || X.Ship.docked) ? 0.5f : 0f;

        public bool TryInteract(in InteractionContext c)
        {
            var x = X;
            if (x == null || !(x.Ship.landed || x.Ship.docked)) return false;
            if (!ShipTravel.Disembark(out var reason)) { Debug.Log($"[Xenoarch] Airlock: {reason}"); return false; }
            return true;
        }
    }
}
