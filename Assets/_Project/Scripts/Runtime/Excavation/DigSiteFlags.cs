using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Red survey flags for refined-scanner pinpoints (ZEN-9B). Flags stand over flagged / unfinished spots of fenced dig
    /// sites (rebuilt from <see cref="DigSiteMapsService"/> on every load). Digging at a flagged spot pulls its flag
    /// (state Digging); leaving the spot's dig perimeter with the find still buried puts the flag back at the spot centre
    /// and marks it Unfinished on the dig map; recovering the find marks it Done.
    /// </summary>
    public sealed class DigSiteFlags : MonoBehaviour
    {
        public static DigSiteFlags Instance { get; private set; }
        [Tooltip("Strokes within this distance (m) of a spot centre start digging it (flag pulled).")] public float startRadius = 1.2f;
        [Tooltip("Leaving this distance (m) from a spot being dug, with its find still buried, marks it unfinished.")] public float perimeter = 3f;

        public static readonly Color FlagRed = new Color(0.9f, 0.08f, 0.06f, 1f), PoleColor = new Color(0.8f, 0.8f, 0.78f, 1f);
        readonly Dictionary<(string, int), GameObject> _flags = new Dictionary<(string, int), GameObject>();
        static Mesh _pole, _cloth;
        float _next;

        void Awake() => Instance = this;

        static DigSiteMapsService Maps => XenoarchRuntime.Session?.DigMaps;

        /// <summary>Called when a major site's fence is shown: makes sure it has a dig map and its flags stand.</summary>
        public void SiteFenced(ExcavationSite site)
        {
            var maps = Maps;
            if (maps == null || site == null || site.IsCache) return;
            var def = site.Definition;
            var body = XenoarchRuntime.Session.Config.galaxy != null ? XenoarchRuntime.Session.Config.galaxy.FindBodyByScene(def.sceneName) : null;
            var map = maps.Ensure(def.Id, def.displayName, body != null ? body.displayName : def.bodyId, site.Grid.SizeX, site.Grid.SizeZ);
            foreach (var s in map.spots) Sync(site, map, s);
        }

        public static GridArtifact Artifact(ExcavationSite site, int index)
        {
            foreach (var a in site.Grid.artifacts) if (a.index == index) return a;
            return null;
        }

        /// <summary>
        /// Refined scan: pinpoints the nearest buried, not yet flagged find of a fenced site within <paramref name="range"/>
        /// (horizontal, m) of <paramref name="from"/> and plants a flag over it.
        /// </summary>
        public bool TryPinpoint(ExcavationSite site, Vector3 from, float range, out DigSiteMap map, out DigSpot spot, out GridArtifact find)
        {
            map = null; spot = null; find = null;
            var maps = Maps;
            if (maps == null || site == null) return false;
            map = maps.Find(site.Definition.Id);
            if (map == null) { SiteFenced(site); map = maps.Find(site.Definition.Id); }
            if (map == null) return false;
            float best = range;
            foreach (var a in site.Grid.artifacts)
            {
                if (a.recovered || map.SpotFor(a.index) != null) continue;
                var c = site.CellCentre(a.x, a.y, a.z);
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(from.x, from.z));
                if (d < best) { best = d; find = a; }
            }
            if (find == null) return false;
            spot = AddSpotFor(site, map, find);
            Sync(site, map, spot);
            return true;
        }

        DigSpot AddSpotFor(ExcavationSite site, DigSiteMap map, GridArtifact find)
        {
            float lx = (find.x + find.footprint * 0.5f) * site.Grid.cell, lz = (find.z + find.footprint * 0.5f) * site.Grid.cell;
            return Maps.AddSpot(map, find.index, lx, lz);
        }

        /// <summary>t178u: flag one specific find (detector sweep held on it). False if it already has a spot.</summary>
        public bool TryFlag(ExcavationSite site, GridArtifact find, out DigSiteMap map, out DigSpot spot)
        {
            map = null; spot = null;
            var maps = Maps;
            if (maps == null || site == null || site.IsCache || find == null || find.recovered) return false;
            map = maps.Find(site.Definition.Id);
            if (map == null) { SiteFenced(site); map = maps.Find(site.Definition.Id); }
            if (map == null || map.SpotFor(find.index) != null) return false;
            spot = AddSpotFor(site, map, find);
            Sync(site, map, spot);
            return true;
        }

        /// <summary>t179u: a find uncovered by digging without a refined-scan flag still goes on the dig site map
        /// (state Digging; leaving it unfinished then plants its flag like any other spot).</summary>
        void MarkUncovered(ExcavationSite site, DigSiteMap map)
        {
            foreach (var a in site.Grid.artifacts)
            {
                if (a.recovered || map.SpotFor(a.index) != null) continue;
                if (site.Exposure(a) < 0.05f) continue;
                var s = AddSpotFor(site, map, a);
                Maps.SetState(s, DigSpotState.Digging);
                Sync(site, map, s);
                DMI.Xenoarch.UI.Toast.Show($"Find uncovered: marked {map.SpotCode(s)} on the dig site map (N).", 3.5f);
            }
        }

        /// <summary>A dig stroke landed at <paramref name="point"/>: pull the flag of a flagged/unfinished spot there.</summary>
        public void OnStroke(ExcavationSite site, Vector3 point)
        {
            var maps = Maps; if (maps == null || site == null || site.IsCache) return;
            var map = maps.Find(site.Definition.Id); if (map == null) return;
            var l = site.transform.InverseTransformPoint(point);
            foreach (var s in map.spots)
            {
                if (s.State != DigSpotState.Flagged && s.State != DigSpotState.Unfinished) continue;
                if (new Vector2(l.x - s.lx, l.z - s.lz).magnitude > startRadius) continue;
                maps.SetState(s, DigSpotState.Digging);
                Sync(site, map, s);
            }
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            var maps = Maps; if (maps == null) return;
            var rig = PlayerRigLocator.Find();
            Vector3? me = rig != null ? rig.Root.position : (Vector3?)null;
            foreach (var site in ExcavationSite.Active)
            {
                if (site == null || site.IsCache) continue;
                var map = maps.Find(site.Definition.Id);
                if (map == null && site.BorderVisible) { SiteFenced(site); map = maps.Find(site.Definition.Id); }
                if (map == null) continue;
                if (me.HasValue && Vector3.Distance(site.transform.position, me.Value) < site.Grid.SizeX + site.Grid.SizeZ + 30f) MarkUncovered(site, map);
                foreach (var s in map.spots)
                {
                    var a = Artifact(site, s.artifact);
                    if (s.State != DigSpotState.Done && (a == null || a.recovered)) { maps.SetState(s, DigSpotState.Done); Sync(site, map, s); continue; }
                    if (s.State == DigSpotState.Digging && me.HasValue)
                    {
                        var w = site.transform.TransformPoint(new Vector3(s.lx, 0f, s.lz));
                        if (new Vector2(w.x - me.Value.x, w.z - me.Value.z).magnitude > perimeter)
                        {
                            maps.SetState(s, DigSpotState.Unfinished);
                            Sync(site, map, s);
                            DMI.Xenoarch.UI.Toast.Show($"Dig spot {map.SpotCode(s)} left unfinished: flag replaced, marked on the dig site map.", 3.5f);
                        }
                    }
                }
            }
        }

        /// <summary>Flag shown iff the spot is flagged or unfinished.</summary>
        void Sync(ExcavationSite site, DigSiteMap map, DigSpot s)
        {
            var key = (map.siteId, s.number);
            bool want = s.State == DigSpotState.Flagged || s.State == DigSpotState.Unfinished;
            _flags.TryGetValue(key, out var go);
            if (go == null && want) _flags[key] = go = MakeFlag(site, s, map.SpotCode(s));
            if (go != null) go.SetActive(want);
        }

        GameObject MakeFlag(ExcavationSite site, DigSpot s, string code)
        {
            EnsureMeshes();
            var w = site.transform.TransformPoint(new Vector3(s.lx, 0f, s.lz));
            w.y = ExcavationSite.TerrainHeight(w) ?? site.transform.position.y;
            var root = new GameObject($"Flag {code}");
            root.transform.SetParent(site.transform, true);
            root.transform.position = w;
            var pole = new GameObject("Pole"); pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.3f, 0f); pole.transform.localScale = new Vector3(0.018f, 0.32f, 0.018f);
            pole.AddComponent<MeshFilter>().sharedMesh = _pole;
            var pr = pole.AddComponent<MeshRenderer>(); pr.sharedMaterial = ExcavationSite.BorderMaterial(PoleColor); pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var cloth = new GameObject("Cloth"); cloth.transform.SetParent(root.transform, false);
            cloth.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            cloth.AddComponent<MeshFilter>().sharedMesh = _cloth;
            var cr = cloth.AddComponent<MeshRenderer>(); cr.sharedMaterial = ExcavationSite.BorderMaterial(FlagRed); cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        static void EnsureMeshes()
        {
            if (_pole == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _pole = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            if (_cloth == null)
            {
                // Double-sided pennant: 0.22 m long, 0.14 m tall.
                _cloth = new Mesh { name = "FlagCloth" };
                var v = new[] { new Vector3(0, 0, 0), new Vector3(0, -0.14f, 0), new Vector3(0.22f, -0.07f, 0) };
                _cloth.vertices = new[] { v[0], v[1], v[2], v[0], v[1], v[2] };
                _cloth.triangles = new[] { 0, 2, 1, 3, 4, 5 };
                _cloth.RecalculateNormals(); _cloth.RecalculateBounds();
            }
        }

        public static void Prewarm() { ExcavationSite.BorderMaterial(FlagRed); ExcavationSite.BorderMaterial(PoleColor); }
    }
}
