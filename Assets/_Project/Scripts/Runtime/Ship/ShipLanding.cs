using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Timed land / take-off / dock / undock from the navigation terminal. Shows the <see cref="CountdownHud"/> (bar,
    /// time remaining, descent hum, beeps, touchdown chime) for <see cref="ShipDefinition.landingSeconds"/> or
    /// <see cref="ShipDefinition.takeoffSeconds"/>; the landed/docked state (and so the airlock) only changes on touchdown.
    /// </summary>
    public static class ShipLanding
    {
        static CountdownHud _hud;
        public static bool Running => _hud != null && CountdownHud.Current == _hud;

        public static bool Begin(CelestialBodyDefinition body, bool landing, bool station)
        {
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null || body == null || Running) return false;
            var def = x.ShipDefinition;
            float secs = landing ? (def != null ? def.landingSeconds : 8f) : (def != null ? def.takeoffSeconds : 5f);
            secs = Mathf.Max(0.5f, secs);
            string title = landing ? (station ? $"Docking: {body.displayName}" : $"Descent: {body.displayName}") : (station ? "Undocking" : "Lift-off to orbit");
            string done = landing ? (station ? "Docked. Airlock unsealed." : "Touchdown. Airlock unsealed.") : "In orbit.";
            _hud = CountdownHud.Run(title, secs, () =>
            {
                _hud = null;
                if (landing) x.SetLanded(!station, station); else x.SetLanded(false, false);
                Debug.Log($"[Xenoarch] Ship: {done} ({body.displayName})");
            }, done);
            Debug.Log($"[Xenoarch] Ship: {title} ({secs:0.#}s)");
            return true;
        }
    }
}
