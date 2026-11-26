using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship airlock / shuttle bay. Planets and moons: "Take Shuttle" opens the shuttle console (choose a landing zone,
    /// the ship stays in orbit). Stations: disembark when docked. A hangar with a visible shuttle comes later.
    /// </summary>
    public sealed class ShipAirlockInteractable : MonoBehaviour, IInteractable
    {
        public Transform InteractionTransform => transform;

        static ExplorationService X => XenoarchRuntime.Session?.Exploration;
        ShuttleLaunchWindow _window;

        public float GetPriority(in InteractionContext c) => X != null && (_window == null || !_window.IsOpen) ? 1f : -1f;

        public string GetPrompt(in InteractionContext c)
        {
            var x = X; var b = x?.CurrentBody;
            if (b == null) return "Airlock";
            if (ShipTravel.UsesShuttle(b)) return $"Take Shuttle to {b.displayName}";
            if (x.Ship.docked) return $"Disembark: {b.displayName}";
            if (b.kind == CelestialKind.Station && b.HasLanding) return $"Airlock sealed. Dock at {b.displayName} via the Navigation Terminal";
            return $"Airlock sealed ({ShipTravel.LocationText(x)}). No landing zone";
        }

        public float GetHoldDuration(in InteractionContext c)
        {
            var x = X;
            return x != null && x.Ship.docked && !ShipTravel.UsesShuttle(x.CurrentBody) ? 0.5f : 0f;
        }

        public bool TryInteract(in InteractionContext c)
        {
            var x = X;
            if (x == null) return false;
            if (ShipTravel.UsesShuttle(x.CurrentBody))
            {
                if (_window == null)
                {
                    var go = new GameObject("Shuttle Bay Console");
                    go.transform.SetParent(transform, false);
                    _window = go.AddComponent<ShuttleLaunchWindow>();
                    _window.terminalName = "Shuttle Bay";
                }
                _window.Open();
                return _window.IsOpen;
            }
            if (!x.Ship.docked) return false;
            if (!ShipTravel.Disembark(out var reason)) { Debug.Log($"[Xenoarch] Airlock: {reason}"); return false; }
            return true;
        }
    }
}
