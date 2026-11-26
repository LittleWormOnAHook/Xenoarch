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
        /// <summary>Planet arrival spawn created beside the landed shuttle (ShuttleDirector).</summary>
        public const string ShuttleSpawn = "shuttle";

        /// <summary>Planets/moons are reached by shuttle from the airlock (the ship stays where it is); stations dock.</summary>
        public static bool UsesShuttle(CelestialBodyDefinition b) => b != null && b.HasLanding && b.kind != CelestialKind.Station;

        /// <summary>Take the shuttle down to the current body, landing near <paramref name="siteId"/> (empty = base zone).</summary>
        public static bool TakeShuttle(string siteId, out string reason)
        {
            reason = null;
            var x = X; var rt = XenoarchRuntime.Instance;
            if (x == null || rt == null) { reason = "No session"; return false; }
            var body = x.CurrentBody;
            if (!UsesShuttle(body)) { reason = body == null ? "Nothing in range" : "No surveyed landing zone"; return false; }
            x.Ship.shuttleSiteId = siteId ?? "";
            x.Body(body.Id).visited = true;
            var site = string.IsNullOrEmpty(siteId) || x.Sites == null ? null : x.Sites.Get(siteId);
            var st = site != null ? x.Site(site.Id) : null;
            string where = site == null ? "base landing zone" : st != null && st.revealLevel >= (int)SiteRevealLevel.Pinpointed ? site.displayName : "orbital signature";
            Scenes.SceneLoader.NextTitle = $"Shuttle descent: {body.displayName}";
            Scenes.SceneLoader.NextSubtitle = $"Landing near the {where}";
            Debug.Log($"[Xenoarch] Shuttle to {body.displayName} ({(site != null ? site.Id : "base zone")})");
            return rt.Travel(body.sceneName, ShuttleSpawn);
        }

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
            if (rt == null) return false;
            Scenes.SceneLoader.NextTitle = "Returning to the ship";
            return rt.Travel(ShipScene, ShipSpawnAirlock);
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
