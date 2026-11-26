using System;
using System.Collections.Generic;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>Runtime ship values (saved as "ship.state").</summary>
    [Serializable]
    public sealed class ShipState
    {
        public string shipDefinitionId;
        public float fuel;
        public float fuelCapacity;
        public float ftlRangeLy;
        public int scannerLevel;
        public int moduleSlots;
        public int cargoSlots;
        public List<string> modules = new List<string>();
        public string currentSystemId;
        [Tooltip("Body the ship is orbiting / landed on / docked at.")] public string currentBodyId;
        public bool landed;
        public bool docked;
        [Tooltip("Course plotted on the navigation terminal, flown from the cockpit helm.")] public string plottedBodyId;
        [Tooltip("Dig site the shuttle was sent to (empty = base landing zone).")] public string shuttleSiteId = "";
    }

    [Serializable]
    public sealed class BodyDiscovery
    {
        public string bodyId;
        public bool discovered;
        public bool visited;
        public int orbitalScanPasses;
    }

    /// <summary>0 hidden, 1 orbital anomaly (region), 2 surface signal (area), 3 pinpointed (marked on site).</summary>
    public enum SiteRevealLevel { Hidden = 0, OrbitalAnomaly = 1, SurfaceSignal = 2, Pinpointed = 3 }

    [Serializable]
    public sealed class SiteState
    {
        public string siteId;
        public int revealLevel;
        public bool surveyed;
        [Tooltip("Phase 1C stub.")] public float excavationProgress;
    }

    [Serializable]
    public sealed class DiscoveryData
    {
        public List<BodyDiscovery> bodies = new List<BodyDiscovery>();
        public List<SiteState> sites = new List<SiteState>();
    }

    /// <summary>
    /// Galaxy/ship/discovery state and rules (plain C#, owned by GameSession). Data-driven from the galaxy, ship and
    /// site definitions; state is saved in two sections ("ship.state", "world.discovery").
    /// </summary>
    public sealed class ExplorationService
    {
        public const string ShipKey = "ship.state";
        public const string DiscoveryKey = "world.discovery";
        public const string NoBody = "";

        public GalaxyDefinition Galaxy { get; }
        public ShipDefinition ShipDefinition { get; }
        public SiteRegistry Sites { get; }
        public ShipState Ship { get; private set; } = new ShipState();
        public DiscoveryData Discovery { get; private set; } = new DiscoveryData();
        public event Action Changed;
        /// <summary>(siteId, new level) whenever a site's reveal level rises.</summary>
        public event Action<string, int> SiteRevealed;
        /// <summary>Discovery fact check for gated sites (set by the session). Null = no gating.</summary>
        public Func<string, bool> Gate;

        public ExplorationService(GalaxyDefinition galaxy, ShipDefinition ship, SiteRegistry sites, SaveService saves)
        {
            Galaxy = galaxy; ShipDefinition = ship; Sites = sites;
            ResetToNewGame();
            saves.Register(new ShipSection(this));
            saves.Register(new DiscoverySection(this));
        }

        public void ResetToNewGame()
        {
            var d = ShipDefinition;
            Ship = new ShipState
            {
                shipDefinitionId = d != null ? d.Id : "",
                fuelCapacity = d != null ? d.fuelCapacity : 100f,
                fuel = d != null ? d.fuelCapacity : 100f,
                ftlRangeLy = d != null ? d.ftlRangeLy : 0f,
                scannerLevel = d != null ? d.scannerLevel : 1,
                moduleSlots = d != null ? d.moduleSlots : 4,
                cargoSlots = d != null ? d.cargoSlots : 12,
                modules = d != null ? new List<string>(d.startingModules) : new List<string>(),
                currentBodyId = d != null ? d.startingBodyId : NoBody,
            };
            Ship.currentSystemId = Galaxy != null && Galaxy.SystemOf(Ship.currentBodyId) is StarSystemDefinition s ? s.Id : "";
            Discovery = new DiscoveryData();
            if (Galaxy != null)
                foreach (var sys in Galaxy.systems)
                    if (sys != null) foreach (var b in sys.bodies)
                        if (b != null) Discovery.bodies.Add(new BodyDiscovery { bodyId = b.Id, discovered = b.discoveredByDefault && sys.knownByDefault });
            Body(Ship.currentBodyId).visited = true;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ queries

        public CelestialBodyDefinition CurrentBody => Galaxy != null ? Galaxy.FindBody(Ship.currentBodyId) : null;
        public StarSystemDefinition CurrentSystem => Galaxy != null ? Galaxy.FindSystem(Ship.currentSystemId) : null;

        public BodyDiscovery Body(string bodyId)
        {
            foreach (var b in Discovery.bodies) if (b.bodyId == bodyId) return b;
            var nb = new BodyDiscovery { bodyId = bodyId ?? "" };
            Discovery.bodies.Add(nb);
            return nb;
        }

        public bool IsDiscovered(string bodyId) => Body(bodyId).discovered;

        public SiteState Site(string siteId)
        {
            foreach (var s in Discovery.sites) if (s.siteId == siteId) return s;
            var ns = new SiteState { siteId = siteId };
            Discovery.sites.Add(ns);
            return ns;
        }

        public IEnumerable<ArchaeologicalSiteDefinition> SitesOnBody(string bodyId)
        {
            if (Sites == null) yield break;
            foreach (var s in Sites.Entries) if (s != null && s.bodyId == bodyId) yield return s;
        }

        public IEnumerable<ArchaeologicalSiteDefinition> SitesInScene(string sceneName)
        {
            if (Sites == null) yield break;
            foreach (var s in Sites.Entries) if (s != null && s.sceneName == sceneName) yield return s;
        }

        /// <summary>Map distance in AU (moons/stations: parent distance + own offset).</summary>
        public float DistanceAU(CelestialBodyDefinition a, CelestialBodyDefinition b)
        {
            if (a == null || b == null) return 0f;
            if (a == b) return 0f;
            float pa = StarDistance(a), pb = StarDistance(b);
            float d = Mathf.Abs(pa - pb);
            if (RootOf(a) == RootOf(b)) d = Mathf.Abs(LocalOffset(a) - LocalOffset(b)) + 0.002f; // same planetary neighbourhood
            return d;
        }

        public float FuelCost(CelestialBodyDefinition to)
        {
            var from = CurrentBody;
            if (to == null || from == to) return 0f;
            float perAU = ShipDefinition != null ? ShipDefinition.fuelPerAU : 10f;
            float min = ShipDefinition != null ? ShipDefinition.minimumHopFuel : 1f;
            return Mathf.Max(min, Mathf.Round(DistanceAU(from, to) * perAU * 10f) / 10f);
        }

        public bool CanTravel(CelestialBodyDefinition to, out string reason)
        {
            reason = null;
            if (to == null) { reason = "No destination"; return false; }
            if (to.Id == Ship.currentBodyId) { reason = "Already here"; return false; }
            if (to.kind == CelestialKind.Star) { reason = "Too close to the star"; return false; }
            if (!IsDiscovered(to.Id)) { reason = "Unknown destination"; return false; }
            var sys = Galaxy != null ? Galaxy.SystemOf(to.Id) : null;
            if (sys != null && sys.Id != Ship.currentSystemId && Ship.ftlRangeLy < sys.minFtlRangeLy) { reason = "Out of FTL range"; return false; }
            float cost = FuelCost(to);
            if (cost > Ship.fuel + 0.001f) { reason = $"Not enough fuel ({cost:0.#} needed)"; return false; }
            return true;
        }

        /// <summary>Moves the ship into orbit of <paramref name="to"/> (fuel spent, discovery updated). No scene load.</summary>
        public bool CommitTravel(CelestialBodyDefinition to, out string reason)
        {
            if (!CanTravel(to, out reason)) return false;
            Ship.fuel = Mathf.Max(0f, Ship.fuel - FuelCost(to));
            Ship.currentBodyId = to.Id;
            var sys = Galaxy != null ? Galaxy.SystemOf(to.Id) : null;
            if (sys != null) Ship.currentSystemId = sys.Id;
            Ship.landed = false; Ship.docked = false;
            if (Ship.plottedBodyId == to.Id) Ship.plottedBodyId = "";
            var bd = Body(to.Id); bd.discovered = true; bd.visited = true;
            Changed?.Invoke();
            return true;
        }

        public CelestialBodyDefinition PlottedBody => Galaxy != null && !string.IsNullOrEmpty(Ship.plottedBodyId) ? Galaxy.FindBody(Ship.plottedBodyId) : null;

        public bool PlotCourse(CelestialBodyDefinition to, out string reason)
        {
            if (!CanTravel(to, out reason)) return false;
            Ship.plottedBodyId = to.Id;
            Changed?.Invoke();
            return true;
        }

        public void ClearCourse() { Ship.plottedBodyId = ""; Changed?.Invoke(); }

        public void SetLanded(bool landed, bool docked)
        {
            Ship.landed = landed; Ship.docked = docked;
            Changed?.Invoke();
        }

        public void Refuel(float amount) { Ship.fuel = Mathf.Min(Ship.fuelCapacity, Ship.fuel + Mathf.Max(0f, amount)); Changed?.Invoke(); }

        // ------------------------------------------------------------------ scanning

        public int MaxOrbitalPasses => Mathf.Clamp(1 + Ship.scannerLevel, 1, 4);

        /// <summary>One orbital scan pass over the current body. Returns how many sites gained reveal level.</summary>
        public int OrbitalScanPass()
        {
            var body = CurrentBody;
            if (body == null) return 0;
            var bd = Body(body.Id);
            if (bd.orbitalScanPasses < MaxOrbitalPasses) bd.orbitalScanPasses++;
            int changed = 0;
            foreach (var site in SitesOnBody(body.Id))
            {
                int target = 0;
                if (bd.orbitalScanPasses >= site.orbitalPassesNeeded) target = (int)SiteRevealLevel.OrbitalAnomaly;
                if (bd.orbitalScanPasses >= site.orbitalPassesNeeded + 1 && Ship.scannerLevel >= site.difficulty - 1) target = (int)SiteRevealLevel.SurfaceSignal;
                if (Reveal(site.Id, target)) changed++;
            }
            Changed?.Invoke();
            return changed;
        }

        public bool Reveal(string siteId, int level)
        {
            var s = Site(siteId);
            if (level <= s.revealLevel) return false;
            var def = Sites != null ? Sites.Get(siteId) : null;
            if (def != null && !string.IsNullOrEmpty(def.gateFact) && Gate != null && !Gate(def.gateFact)) return false;
            s.revealLevel = Mathf.Clamp(level, 0, (int)SiteRevealLevel.Pinpointed);
            SiteRevealed?.Invoke(siteId, s.revealLevel);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Uncertainty radius (m) of a site's shown position for its reveal level.</summary>
        public static float Uncertainty(int level) => level >= 3 ? 0f : level == 2 ? 15f : level == 1 ? 60f : float.PositiveInfinity;

        /// <summary>Deterministic offset of the displayed estimate from the true position (stable per site and level).</summary>
        public static Vector3 EstimateOffset(ArchaeologicalSiteDefinition site, int level)
        {
            float r = Uncertainty(level);
            if (float.IsInfinity(r) || r <= 0f) return Vector3.zero;
            var rng = new System.Random(site.seed * 31 + level);
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float d = (float)(rng.NextDouble() * r * 0.7);
            return new Vector3(Mathf.Cos(ang) * d, 0f, Mathf.Sin(ang) * d);
        }

        // ------------------------------------------------------------------ helpers

        CelestialBodyDefinition Parent(CelestialBodyDefinition b) => Galaxy != null && !string.IsNullOrEmpty(b.parentId) ? Galaxy.FindBody(b.parentId) : null;
        CelestialBodyDefinition RootOf(CelestialBodyDefinition b) { var p = Parent(b); return p == null ? b : RootOf(p); }
        float StarDistance(CelestialBodyDefinition b) { var p = Parent(b); return p == null ? b.orbitAU : StarDistance(p); }
        float LocalOffset(CelestialBodyDefinition b) => Parent(b) == null ? 0f : b.orbitAU + LocalOffset(Parent(b));

        sealed class ShipSection : ISaveSection
        {
            readonly ExplorationService _s;
            public ShipSection(ExplorationService s) { _s = s; }
            public string Key => ShipKey;
            public int RestoreOrder => 20;
            public string CaptureJson() => JsonUtility.ToJson(_s.Ship);
            public void RestoreJson(string json) { _s.Ship = JsonUtility.FromJson<ShipState>(json) ?? new ShipState(); _s.Changed?.Invoke(); }
            public void ResetToNewGame() => _s.ResetToNewGame();
        }

        sealed class DiscoverySection : ISaveSection
        {
            readonly ExplorationService _s;
            public DiscoverySection(ExplorationService s) { _s = s; }
            public string Key => DiscoveryKey;
            public int RestoreOrder => 20;
            public string CaptureJson() => JsonUtility.ToJson(_s.Discovery);
            public void RestoreJson(string json) { _s.Discovery = JsonUtility.FromJson<DiscoveryData>(json) ?? new DiscoveryData(); _s.Changed?.Invoke(); }
            public void ResetToNewGame() { /* ResetToNewGame on the service resets both ship and discovery */ }
        }
    }
}
