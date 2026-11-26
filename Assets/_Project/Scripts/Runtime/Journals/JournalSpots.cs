using System;
using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Journals
{
    /// <summary>
    /// Where each journal goes on a surface scene, from data only (placement, siteIndex, the scene's LandingZones and the
    /// body's dig sites). Shared by the Editor menu (Place Journals) and the runtime fallback, so both agree.
    /// </summary>
    public static class JournalSpots
    {
        public const string RootName = "Journals";
        public static string ObjectName(string entryId) => "XA_Journal_" + entryId;

        /// <summary>Entries that belong on this body for the given placement kinds, in order.</summary>
        public static IEnumerable<JournalEntry> For(string bodyId, params JournalPlacement[] kinds) =>
            JournalService.All.Where(e => kinds.Contains(e.placement) && (string.IsNullOrEmpty(e.bodyId) || e.bodyId == bodyId));

        /// <param name="slot">Index among entries sharing the same anchor (spreads them out).</param>
        /// <param name="height">Ground height at a point (null = off the terrain).</param>
        public static bool TryPlace(JournalEntry e, int slot, Scene scene, IList<ArchaeologicalSiteDefinition> sites, Func<Vector3, float?> height, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var zones = new List<LandingZone>();
            foreach (var r in scene.GetRootGameObjects()) zones.AddRange(r.GetComponentsInChildren<LandingZone>(true));
            var baseZone = zones.FirstOrDefault(z => string.IsNullOrEmpty(z.siteId));
            if (baseZone == null) return false;
            var bp = baseZone.transform.position;
            var ordered = (sites ?? new List<ArchaeologicalSiteDefinition>()).Where(s => s != null)
                .OrderBy(s => Flat(s.position - bp).magnitude).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();

            Vector3 anchor, face;
            switch (e.placement)
            {
                case JournalPlacement.BaseLanding:
                {
                    // Ring 11 m from the base marker (outside the shuttle footprint), starting at its right, 22 deg apart.
                    var dir = Quaternion.Euler(0f, 70f + slot * 22f, 0f) * Flat(baseZone.transform.forward).normalized;
                    if (dir.sqrMagnitude < 0.5f) dir = Vector3.right;
                    anchor = bp + dir * 11f; face = bp - anchor; break;
                }
                case JournalPlacement.SiteLanding:
                {
                    if (ordered.Count == 0) return false;
                    var site = ordered[Mathf.Clamp(e.siteIndex, 0, ordered.Count - 1)];
                    var z = zones.FirstOrDefault(m => m.siteId == site.Id);
                    var zp = z != null ? z.transform.position : site.position;
                    var dir = Quaternion.Euler(0f, 90f + slot * 22f, 0f) * Flat(site.position - zp).normalized;
                    if (dir.sqrMagnitude < 0.5f) dir = Vector3.right;
                    anchor = zp + dir * 11f; face = site.position - anchor; break;
                }
                case JournalPlacement.FirstDigSite:
                {
                    if (ordered.Count == 0) return false;
                    var site = ordered[0];
                    var z = zones.FirstOrDefault(m => m.siteId == site.Id);
                    var from = z != null ? z.transform.position : bp;
                    var toward = Flat(from - site.position).normalized; if (toward.sqrMagnitude < 0.5f) toward = Vector3.forward;
                    var side = Vector3.Cross(Vector3.up, toward);
                    anchor = site.position + toward * (site.radius * 1.42f + 2.5f) + side * ((slot - 0.5f) * 1.1f);
                    face = site.position - anchor; break;
                }
                default: return false;
            }
            var h = height(anchor);
            if (!h.HasValue) return false;
            pos = new Vector3(anchor.x, h.Value + 0.02f, anchor.z);
            face.y = 0f;
            rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized) : Quaternion.identity;
            return true;
        }

        public static Func<Vector3, float?> TerrainHeight(Terrain t) => p =>
        {
            if (t == null || t.terrainData == null) return null;
            var tp = t.transform.position; var size = t.terrainData.size;
            if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) return null;
            return tp.y + t.SampleHeight(p);
        };

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
