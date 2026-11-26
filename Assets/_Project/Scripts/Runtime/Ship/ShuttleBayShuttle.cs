using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>The shuttle parked in the ship's bay: E opens its flight menu (map of the current planet's landing zones
    /// and scanned sites, then Take Shuttle).</summary>
    public sealed class ShuttleBayShuttle : MonoBehaviour, IInteractable
    {
        public Transform InteractionTransform => transform;
        ShuttleLaunchWindow _window;
        static ExplorationService X => XenoarchRuntime.Session?.Exploration;

        public float GetPriority(in InteractionContext c) => X != null && (_window == null || !_window.IsOpen) ? 1f : -1f;
        public string GetPrompt(in InteractionContext c)
        {
            var b = X?.CurrentBody;
            return ShipTravel.UsesShuttle(b) ? $"Shuttle: fly to {b.displayName}" : "Shuttle: no landing zone in range";
        }
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            if (X == null || !ShipTravel.UsesShuttle(X.CurrentBody)) { UI.Toast.Show("Shuttle: travel to a planet or moon first (Navigation Terminal)."); return false; }
            if (_window == null)
            {
                var go = new GameObject("Shuttle Flight Menu");
                go.transform.SetParent(transform, false);
                _window = go.AddComponent<ShuttleLaunchWindow>();
                _window.terminalName = "Shuttle";
            }
            _window.Open();
            return _window.IsOpen;
        }
    }
}
