using System;
using System.Collections.Generic;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    public enum DigSpotState { Flagged = 0, Digging = 1, Unfinished = 2, Done = 3 }

    [Serializable]
    public sealed class DigSpot
    {
        public int number;          // order flagged within the site (code digit)
        public int artifact;        // GridArtifact.index (site spawn index)
        public float lx, lz;        // spot centre, site-local metres
        public int state;           // DigSpotState
        public DigSpotState State { get => (DigSpotState)state; set => state = (int)value; }
    }

    [Serializable]
    public sealed class DigSiteMap
    {
        public string siteId, displayName, prefix;
        public int number;          // order this body's dig sites were marked/fenced
        public float sizeX, sizeZ;  // fenced grid size (m)
        public List<DigSpot> spots = new List<DigSpot>();
        public string Code => $"{prefix}{number}";
        public string SpotCode(DigSpot s) => $"{prefix}{number}{s.number}";
        public DigSpot SpotFor(int artifact) { foreach (var s in spots) if (s.artifact == artifact) return s; return null; }
    }

    [Serializable]
    public sealed class DigSiteMapsState { public List<DigSiteMap> sites = new List<DigSiteMap>(); }

    /// <summary>
    /// Dig site maps (ZEN-9B): every fenced major dig site gets a code from its body's first two letters and the order it
    /// was marked (Ma1, Ma2...); refined-scanner pinpoints add numbered spots (Ma11, Ma12...) with a state
    /// (flagged / digging / unfinished / done). Save section "digsites.maps" (new section; old saves start empty).
    /// </summary>
    public sealed class DigSiteMapsService
    {
        public const string SaveKey = "digsites.maps";
        DigSiteMapsState _state = new DigSiteMapsState();
        public event Action Changed;

        public DigSiteMapsService(SaveService saves)
        {
            saves.Register(new JsonStateSection<DigSiteMapsState>(SaveKey, 25, () => _state, v => { _state = v ?? new DigSiteMapsState(); Changed?.Invoke(); }));
        }

        public IReadOnlyList<DigSiteMap> Sites => _state.sites;

        public DigSiteMap Find(string siteId)
        {
            foreach (var m in _state.sites) if (m.siteId == siteId) return m;
            return null;
        }

        public static string PrefixFor(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return "Xx";
            string letters = "";
            foreach (char c in bodyName) if (char.IsLetter(c)) { letters += letters.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c); if (letters.Length == 2) break; }
            return letters.Length == 2 ? letters : (letters + "x");
        }

        /// <summary>The site's map, created (numbered in marking order per body) the first time it is fenced.</summary>
        public DigSiteMap Ensure(string siteId, string displayName, string bodyName, float sizeX, float sizeZ)
        {
            var m = Find(siteId);
            if (m != null) { m.sizeX = sizeX; m.sizeZ = sizeZ; return m; }
            string prefix = PrefixFor(bodyName);
            int n = 1;
            foreach (var o in _state.sites) if (o.prefix == prefix) n = Mathf.Max(n, o.number + 1);
            m = new DigSiteMap { siteId = siteId, displayName = displayName, prefix = prefix, number = n, sizeX = sizeX, sizeZ = sizeZ };
            _state.sites.Add(m);
            Changed?.Invoke();
            return m;
        }

        public DigSpot AddSpot(DigSiteMap m, int artifact, float lx, float lz)
        {
            var s = m.SpotFor(artifact);
            if (s != null) return s;
            int n = 1;
            foreach (var o in m.spots) n = Mathf.Max(n, o.number + 1);
            s = new DigSpot { number = n, artifact = artifact, lx = lx, lz = lz, State = DigSpotState.Flagged };
            m.spots.Add(s);
            Changed?.Invoke();
            return s;
        }

        public void SetState(DigSpot s, DigSpotState st)
        {
            if (s == null || s.State == st) return;
            s.State = st;
            Changed?.Invoke();
        }
    }
}
