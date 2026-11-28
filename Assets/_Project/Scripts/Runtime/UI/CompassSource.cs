using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.UI
{
    public enum CompassKind { Site, Cache, Poi, Shuttle, Rover, Waypoint, Objective }

    /// <summary>Compass / edge-arrow tuning (approved mockups, t408s1998). Colours are the approved set; they mirror the
    /// map tokens in XenoarchPalette.uss (site, cache, base, secondary, heading, landing-green).</summary>
    public static class CompassConfig
    {
        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        public static readonly Color Site = Hex(0xF23326);
        public static readonly Color Cache = Hex(0xFFD140);
        public static readonly Color Poi = Hex(0x68DFD6);
        public static readonly Color Shuttle = Hex(0xD8F2FF);
        public static readonly Color Rover = Hex(0x5CA7A8);
        public static readonly Color Waypoint = Hex(0xFFA967);
        public static readonly Color Objective = Hex(0x59FF8C);

        public static Color ColorOf(CompassKind k)
        {
            switch (k)
            {
                case CompassKind.Site: return Site;
                case CompassKind.Cache: return Cache;
                case CompassKind.Poi: return Poi;
                case CompassKind.Shuttle: return Shuttle;
                case CompassKind.Rover: return Rover;
                case CompassKind.Waypoint: return Waypoint;
                default: return Objective;
            }
        }

        public static string NameOf(CompassKind k)
        {
            switch (k)
            {
                case CompassKind.Site: return "SITE";
                case CompassKind.Cache: return "CACHE";
                case CompassKind.Poi: return "POI";
                case CompassKind.Shuttle: return "SHUTTLE";
                case CompassKind.Rover: return "ROVER";
                case CompassKind.Waypoint: return "WAYPOINT";
                default: return "OBJECTIVE";
            }
        }

        /// <summary>Compass strip size curve: big when close, small when far (px, reference 1920x1080).</summary>
        public const float SizeNear = 24f, SizeFar = 7f, CurveNearM = 20f, CurveFarM = 350f, ObjectiveExtra = 2f;
        /// <summary>Other kinds hide beyond this; objective, shuttle and rover always show (clamped to the strip ends).</summary>
        public const float MaxMarkerDistance = 500f;
        /// <summary>Scanner pulse arrows: total life and the part of it spent fading out.</summary>
        public const float ArrowSeconds = 3.5f, ArrowFadeSeconds = 0.9f;
        public const int MaxPulseArrows = 8;
        /// <summary>Panel reference size (PanelSettings scale-with-screen 1920x1080); fallback if the root has no layout yet.</summary>
        public const float RefW = 1920f, RefH = 1080f;
        /// <summary>Pulse arrows reach scanner range x this (same reach the old chevron used).</summary>
        public const float PulseRangeMult = 6f;

        public static float SizeAt(float distM, bool objective)
        {
            float t = Mathf.Clamp01((distM - CurveNearM) / (CurveFarM - CurveNearM));
            return Mathf.Lerp(SizeNear, SizeFar, t) + (objective ? ObjectiveExtra : 0f);
        }
    }

    public struct CompassMark
    {
        public string id;
        public CompassKind kind;
        public Vector3 pos;
        public float dist;        // flat distance from the viewer, filled by the HUD each frame
    }

    /// <summary>
    /// Shared marker list for the compass and the scanner edge arrows. Rebuilt at ~10 Hz from the same services the maps
    /// use (KnownSites, ValuableSiteDirector, WaypointService, ShuttleDirector, RoverDirector, ObjectiveService), so they
    /// always agree. Empty off a landable body (aboard the ship).
    /// </summary>
    public static class CompassSource
    {
        public static readonly List<CompassMark> Marks = new List<CompassMark>(32);
        public static bool OnSurface { get; private set; }
        static float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Marks.Clear(); OnSurface = false; _next = 0f; }

        public static void Refresh(bool force = false)
        {
            if (!force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            Gather();
        }

        /// <summary>View position and heading (0 = +Z = north, clockwise) from the player's view camera.</summary>
        public static bool View(out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            var rig = PlayerRigLocator.Find();
            var cam = rig != null && rig.ViewCamera != null && rig.ViewCamera.isActiveAndEnabled ? rig.ViewCamera : Camera.main;
            if (cam == null) return false;
            var t = cam.transform; pos = t.position;
            var f = t.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.04f) { f = t.up; f.y = 0f; }   // looking almost straight down
            if (f.sqrMagnitude < 1e-6f) return false;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return true;
        }

        public static float Flat(Vector3 from, Vector3 to) { float dx = to.x - from.x, dz = to.z - from.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        static void Gather()
        {
            Marks.Clear(); OnSurface = false;
            var s = XenoarchRuntime.Session;
            var x = s != null ? s.Exploration : null;
            if (x == null || x.Galaxy == null) return;
            var body = x.Galaxy.FindBodyByScene(SceneManager.GetActiveScene().name);
            if (!ShipTravel.UsesShuttle(body) || body.surfaceMapRect.width < 10f) return;
            OnSurface = true;

            // Objective: only when its destination site is known (revealed) on this body.
            var dest = s.Objectives != null ? s.Objectives.Destination() : null;
            string objSite = dest != null ? dest.siteId : null;

            foreach (var k in KnownSites.OnBody(x, body.Id))
            {
                var p = k.Estimate;
                bool isObj = !string.IsNullOrEmpty(objSite) && k.Def.Id == objSite;
                if (isObj) Marks.Add(new CompassMark { id = "objective", kind = CompassKind.Objective, pos = p });
                else if (!k.Cleared) Marks.Add(new CompassMark { id = k.Def.Id, kind = k.Def.isSurveyCache ? CompassKind.Cache : CompassKind.Site, pos = p });
                if (k.Def.hasLandingHint && !k.Cleared)
                    Marks.Add(new CompassMark { id = "landing:" + k.Def.Id, kind = CompassKind.Poi, pos = k.Def.landingHint });
            }
            if (body.hasBaseLanding) Marks.Add(new CompassMark { id = "landing:", kind = CompassKind.Poi, pos = body.baseLanding });

            var vdir = DMI.Xenoarch.Valuables.ValuableSiteDirector.Instance;
            if (vdir != null && s.Valuables != null)
                foreach (var v in vdir.Sites)
                    if (s.Valuables.IsRevealed(v.id) && !s.Valuables.IsCollected(v.id))
                        Marks.Add(new CompassMark { id = v.id, kind = CompassKind.Cache, pos = v.position });

            if (s.Waypoints != null)
                foreach (var w in s.Waypoints.OnBody(body.Id))
                    Marks.Add(new CompassMark { id = w.id, kind = CompassKind.Waypoint, pos = new Vector3(w.x, 0f, w.z) });

            var shuttle = ShuttleDirector.Instance != null ? ShuttleDirector.Instance.Shuttle : null;
            if (shuttle != null) Marks.Add(new CompassMark { id = "shuttle", kind = CompassKind.Shuttle, pos = shuttle.transform.position });
            var rover = RoverDirector.Instance != null ? RoverDirector.Instance.Current : null;
            if (rover != null) Marks.Add(new CompassMark { id = "rover", kind = CompassKind.Rover, pos = rover.transform.position });
        }

        public static bool HudVisible()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || !rt.SessionActive) return false;
            if (DMI.Xenoarch.Scenes.SceneLoader.Busy || UIState.IsOpen(Panels.Loading) || UIState.BlocksGameplay) return false;
            return true;
        }
    }
}