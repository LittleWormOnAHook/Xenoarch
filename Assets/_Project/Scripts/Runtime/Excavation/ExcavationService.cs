using System;
using System.Collections.Generic;
using DMI.Xenoarch.Save;

namespace DMI.Xenoarch.Excavation
{
    [Serializable] public sealed class BuriedArtifactState { public int index; public float condition; public float clean; public bool recovered; public string instanceId; }

    [Serializable]
    public sealed class SiteExcavationState
    {
        public string siteId;
        public int nx, nz, ny;
        public string cells; // RLE + base64 of the fill bytes
        public int strokes;  // also the Digger "generation" (replay check)
        public List<BuriedArtifactState> artifacts = new List<BuriedArtifactState>();
    }

    [Serializable] public sealed class ExcavationData { public List<SiteExcavationState> sites = new List<SiteExcavationState>(); }

    /// <summary>Persisted excavation state per dig site (section "excavation.sites"). Sites write back on every change.</summary>
    public sealed class ExcavationService
    {
        public const string Key = "excavation.sites";
        public ExcavationData Data { get; private set; } = new ExcavationData();

        public ExcavationService(SaveService saves)
        {
            saves?.Register(new JsonStateSection<ExcavationData>(Key, 12, () => Data, d => Data = d ?? new ExcavationData()));
        }

        public SiteExcavationState Find(string siteId)
        {
            foreach (var s in Data.sites) if (s.siteId == siteId) return s;
            return null;
        }

        public bool IsRecovered(string siteId, int index)
        {
            var s = Find(siteId);
            if (s == null) return false;
            foreach (var a in s.artifacts) if (a.index == index) return a.recovered;
            return false;
        }

        /// <summary>Restore a grid (cells + artifact conditions/recovery) from saved state. False = no/incompatible state.</summary>
        public bool Load(string siteId, ExcavationGrid g)
        {
            var s = Find(siteId);
            if (s == null || s.nx != g.nx || s.nz != g.nz || s.ny != g.ny || !g.Decode(s.cells)) return false;
            g.strokes = s.strokes;
            foreach (var a in g.artifacts)
                foreach (var st in s.artifacts)
                    if (st.index == a.index) { a.condition = st.condition; a.recovered = st.recovered; a.clean = st.clean; }
            return true;
        }

        public void Store(string siteId, ExcavationGrid g, string recoveredInstance = null, int recoveredIndex = -1)
        {
            var s = Find(siteId);
            if (s == null) { s = new SiteExcavationState { siteId = siteId }; Data.sites.Add(s); }
            s.nx = g.nx; s.nz = g.nz; s.ny = g.ny; s.cells = g.Encode(); s.strokes = g.strokes;
            var old = s.artifacts;
            s.artifacts = new List<BuriedArtifactState>();
            foreach (var a in g.artifacts)
            {
                string inst = null;
                foreach (var o in old) if (o.index == a.index) inst = o.instanceId;
                if (a.index == recoveredIndex) inst = recoveredInstance;
                s.artifacts.Add(new BuriedArtifactState { index = a.index, condition = a.condition, clean = a.clean, recovered = a.recovered, instanceId = inst });
            }
        }
    }
}
