using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Scene-flow rules for the ship: travel (orbit change, stays aboard), land/dock (leave the ship into the body's
    /// scene) and return to the ship. State changes go through <see cref="ExplorationService"/>; scenes through the runtime.
    /// </summary>
    public static class ShipTravel
    {
        public const string ShipScene = SceneNames.ShipStarter;
        public const string ShipSpawnBridge = PlayerSpawnIds.Default;
        public const string ShipSpawnAirlock = "airlock";

        static ExplorationService X => XenoarchRuntime.Session?.Exploration;

        /// <summary>In-system hop: spends fuel and re-enters the ship scene in the new orbit (fade = "jump").</summary>
        public static bool TravelTo(CelestialBodyDefinition body, out string reason)
        {
            reason = null;
            var x = X; var rt = XenoarchRuntime.Instance;
            if (x == null || rt == null) { reason = "No session"; return false; }
            if (!x.CommitTravel(body, out reason)) return false;
            Debug.Log($"[Xenoarch] Ship travelled to {body.displayName} (fuel {x.Ship.fuel:0.#}/{x.Ship.fuelCapacity:0})");
            return rt.Travel(ShipScene, ShipSpawnBridge);
        }

        /// <summary>Land on / dock at the current body (needs a landing scene).</summary>
        public static bool Disembark(out string reason)
        {
            reason = null;
            var x = X; var rt = XenoarchRuntime.Instance;
            if (x == null || rt == null) { reason = "No session"; return false; }
            var body = x.CurrentBody;
            if (body == null || !body.HasLanding) { reason = "No surveyed landing zone"; return false; }
            bool station = body.kind == CelestialKind.Station;
            x.SetLanded(!station, station);
            var bd = x.Body(body.Id); bd.visited = true;
            return rt.Travel(body.sceneName, string.IsNullOrEmpty(body.arrivalSpawnId) ? PlayerSpawnIds.Default : body.arrivalSpawnId);
        }

        /// <summary>From a surface/station scene back aboard (arrives at the airlock).</summary>
        public static bool ReturnToShip()
        {
            var rt = XenoarchRuntime.Instance;
            return rt != null && rt.Travel(ShipScene, ShipSpawnAirlock);
        }

        public static string LocationText(ExplorationService x)
        {
            var b = x?.CurrentBody;
            if (b == null) return "Deep space";
            string mode = x.Ship.docked ? "Docked at" : x.Ship.landed ? "Landed on" : "In orbit of";
            return $"{mode} {b.displayName}";
        }
    }
}
