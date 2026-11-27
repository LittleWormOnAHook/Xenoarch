using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Journals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Moves ONE dig site to flat, open ground at a chosen distance from its body's surface-map centre (the orbital scan
    /// centre), with the same rules as Spread Sites (XenoarchConfig.siteLayout: spacing from the other sites, their zones
    /// and the base, edge margin, slope, relief, not on his scene objects). It moves the site definition, the site's
    /// LandingZone marker (with its shuttle + arrival spawn, re-grounded) and the scene journal pickups placed for that
    /// site, then saves only that scene and re-bakes the surface maps / landing hints. Nothing else in the scene moves.
    /// Skips (does nothing) when the scene is open with unsaved changes or the Editor is in Play mode.
    /// Trigger: Temp/xenoarch_sitemove.trigger with "plan|apply siteId ringMin ringMax"; log Logs/xenoarch_sitemove.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchSiteMove
    {
        public const string Trigger = "Temp/xenoarch_sitemove.trigger";
        const string Log = "Logs/xenoarch_sitemove.txt";

        static XenoarchSiteMove() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (!File.Exists(Trigger) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            string arg = File.ReadAllText(Trigger).Trim();
            File.Delete(Trigger);
            var sb = new StringBuilder();
            sb.AppendLine($"Site move {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}: {arg}");
            try
            {
                var a = arg.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (a.Length < 4) sb.AppendLine("usage: plan|apply siteId ringMin ringMax");
                else Move(a[1], float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture), a[0] == "apply", sb);
            }
            catch (System.Exception e) { sb.AppendLine("ERROR " + e); }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Log, sb.ToString());
            Debug.Log("[Xenoarch] Site move:\n" + sb);
        }

        static Vector3 Flat(Vector3 v) => XenoarchSiteLayout.Flat(v);
        static float FlatDist(Vector3 a, Vector3 b) => XenoarchSiteLayout.FlatDist(a, b);

        public static bool Move(string siteId, float ringMin, float ringMax, bool apply, StringBuilder sb)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { sb.AppendLine("Play mode - nothing done"); return false; }
            var cfg = Resources.Load<XenoarchConfig>(XenoarchConfig.ResourcePath);
            var site = cfg != null && cfg.siteRegistry != null ? cfg.siteRegistry.Entries.FirstOrDefault(s => s != null && s.Id == siteId) : null;
            if (site == null) { sb.AppendLine($"no site {siteId}"); return false; }
            var body = cfg.galaxy != null ? cfg.galaxy.FindBody(site.bodyId) : null;
            if (body == null || body.surfaceMapRect.width <= 1f) { sb.AppendLine($"no body / surface map rect for {site.bodyId}"); return false; }
            var entry = XenoarchSiteLayout.SurfaceScenes(cfg).FirstOrDefault(e => e.name == site.sceneName);
            if (string.IsNullOrEmpty(entry.path)) { sb.AppendLine($"no scene {site.sceneName}"); return false; }
            var open = SceneManager.GetSceneByPath(entry.path);
            bool wasOpen = open.IsValid() && open.isLoaded;
            if (wasOpen && open.isDirty) { sb.AppendLine($"{entry.name} is open with unsaved changes - STOPPED, nothing changed"); return false; }
            sb.AppendLine($"{entry.name}: {(wasOpen ? "open in the Editor (no unsaved changes)" : "not open - opened additively")}");
            var scene = wasOpen ? open : EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
            bool saved = false;
            try
            {
                saved = Work(scene, cfg, body, site, ringMin, ringMax, apply, sb);
                if (saved) { EditorSceneManager.MarkSceneDirty(scene); saved = EditorSceneManager.SaveScene(scene); sb.AppendLine(saved ? $"{entry.name}: saved" : $"{entry.name}: SAVE FAILED"); }
            }
            finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
            if (saved)
            {
                AssetDatabase.SaveAssets();
                var maps = new StringBuilder(); XenoarchMapsSetup.Run(maps);
                sb.AppendLine("maps:").Append(maps);
            }
            return saved;
        }

        static bool Work(Scene scene, XenoarchConfig cfg, CelestialBodyDefinition body, ArchaeologicalSiteDefinition site, float ringMin, float ringMax, bool apply, StringBuilder sb)
        {
            var ls = cfg.siteLayout ?? new SiteLayoutSettings();
            var roots = scene.GetRootGameObjects();
            Terrain terrain = null;
            foreach (var r in roots) { terrain = r.GetComponentInChildren<Terrain>(true); if (terrain != null) break; }
            if (terrain == null || terrain.terrainData == null) { sb.AppendLine("no terrain"); return false; }
            var markers = roots.SelectMany(r => r.GetComponentsInChildren<LandingZone>(true)).ToList();
            var baseZone = markers.FirstOrDefault(m => string.IsNullOrEmpty(m.siteId));
            var marker = markers.FirstOrDefault(m => m.siteId == site.Id);
            if (baseZone == null || marker == null) { sb.AppendLine("no base zone / site marker"); return false; }
            var sites = cfg.siteRegistry.Entries.Where(s => s != null && s.sceneName == scene.name).ToList();
            var others = sites.Where(s => s != site).ToList();
            var otherZones = markers.Where(m => m != marker && m != baseZone).Select(m => m.transform.position).ToList();
            var centre = new Vector3(body.surfaceMapRect.center.x, 0f, body.surfaceMapRect.center.y);
            var basePos = baseZone.transform.position;
            Vector3 oldSite = site.position, oldZone = marker.transform.position;
            sb.AppendLine($"map centre ({centre.x:0.0}, {centre.z:0.0}); base zone ({basePos.x:0}, {basePos.z:0})");
            sb.AppendLine($"before: {site.Id} at ({oldSite.x:0.0}, {oldSite.z:0.0}) = {FlatDist(oldSite, centre):0.0} m from the map centre; zone ({oldZone.x:0.0}, {oldZone.z:0.0})");

            Physics.SyncTransforms();
            var height = LandingZonePicker.ForTerrain(terrain);
            bool Blocked(Vector3 p, float r)
            {
                foreach (var c in Physics.OverlapSphere(p + Vector3.up * (r + 0.6f), r, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c is TerrainCollider || c.gameObject.scene != scene) continue;
                    if (c.GetComponentInParent<LandingZone>() != null) continue;
                    return true;
                }
                return false;
            }
            bool Flatish(Vector3 p, float r, float slopeMax, float reliefMax, out float y, out float score)
            {
                score = 0f;
                if (!LandingZonePicker.Measure(p, r, height, out y, out float slope, out float step)) return false;
                if (slope > slopeMax || step > reliefMax) return false;
                score = slope / Mathf.Max(1f, slopeMax) + step / Mathf.Max(0.1f, reliefMax);
                return true;
            }

            var tp = terrain.transform.position; var size = terrain.terrainData.size;
            float x0 = tp.x + ls.edgeMargin, x1 = tp.x + size.x - ls.edgeMargin, z0 = tp.z + ls.edgeMargin, z1 = tp.z + size.z - ls.edgeMargin;
            float mid = (ringMin + ringMax) * 0.5f, step8 = Mathf.Max(4f, ls.gridStep * 0.5f);
            var cands = new List<(Vector3 p, float score, float d)>();
            for (float x = centre.x - ringMax; x <= centre.x + ringMax; x += step8)
                for (float z = centre.z - ringMax; z <= centre.z + ringMax; z += step8)
                {
                    if (x < x0 || x > x1 || z < z0 || z > z1) continue;
                    var p = new Vector3(x, 0f, z);
                    float d = FlatDist(p, centre);
                    if (d < ringMin || d > ringMax) continue;
                    float db = FlatDist(p, basePos);
                    if (db < ls.spacing * 0.6f || db > ls.maxFromBase) continue;
                    if (!Flatish(p, ls.flatRadius, ls.maxSlope * 1.6f, ls.maxRelief * 2f, out float y, out float sc)) continue;
                    p.y = y; cands.Add((p, sc, d));
                }
            sb.AppendLine($"{cands.Count} candidate spots {ringMin:0}-{ringMax:0} m from the map centre");

            float rSite = site.radius * 1.42f;
            Vector3 s = default, l = default; bool found = false; float usedRelax = 1f;
            foreach (var relax in new[] { 1f, 0.85f, 0.7f, 0.55f })
            {
                float spacing = ls.spacing * relax, slopeMax = ls.maxSlope / Mathf.Sqrt(relax), reliefMax = ls.maxRelief / relax;
                var order = cands.Where(c => FlatDist(c.p, basePos) >= spacing * 0.6f && others.All(o => FlatDist(c.p, o.position) >= spacing) && otherZones.All(z => FlatDist(c.p, z) >= spacing * 0.8f))
                                 .OrderBy(c => Mathf.Abs(c.d - mid) * 0.5f + FlatDist(c.p, oldSite) * 0.1f + c.score * 10f)
                                 .Take(400);
                foreach (var c in order)
                {
                    if (!Flatish(c.p, Mathf.Max(ls.flatRadius, rSite + 2f), slopeMax, reliefMax, out float sy, out _)) continue;
                    var sp = new Vector3(c.p.x, sy, c.p.z);
                    if (Blocked(sp, rSite + 1f)) continue;
                    if (!XenoarchSiteLayout.FindLanding(sp, basePos, rSite, ls, slopeMax, reliefMax, Flatish, Blocked, out var lp)) continue;
                    if (FlatDist(lp, centre) > ringMax + 40f) continue;
                    s = sp; l = lp; found = true; usedRelax = relax; break;
                }
                if (found) break;
            }
            if (!found) { sb.AppendLine("NO spot found - nothing changed"); return false; }
            sb.AppendLine($"pick{(usedRelax < 1f ? $" (rules relaxed x{usedRelax:0.##})" : "")}: site ({s.x:0.0}, {s.y:0.00}, {s.z:0.0}) = {FlatDist(s, centre):0.0} m from the map centre, {FlatDist(s, basePos):0} m from base, moved {FlatDist(s, oldSite):0} m; zone ({l.x:0.0}, {l.z:0.0}) {FlatDist(l, s):0} m from the site");
            sb.AppendLine("  to other sites: " + string.Join(", ", others.Select(o => $"{o.Id} {FlatDist(o.position, s):0} m")));
            sb.AppendLine("  to other zones: " + string.Join(", ", markers.Where(m => m != marker).Select(m => $"{m.name} {FlatDist(m.transform.position, s):0} m")));

            // Scene objects near the OLD spot that are not ours (left where they are; reported only).
            var left = new HashSet<string>();
            foreach (var c in Physics.OverlapSphere(oldSite, rSite + 25f, ~0, QueryTriggerInteraction.Collide))
                if (!(c is TerrainCollider) && c.gameObject.scene == scene && c.GetComponentInParent<LandingZone>() == null && c.GetComponentInParent<JournalPickup>() == null) left.Add(c.transform.root.name + "/" + c.name);
            sb.AppendLine(left.Count == 0 ? "  nothing else of his near the old spot" : "  other scene objects near the old spot (NOT moved): " + string.Join(", ", left));

            // Journals placed for this site (near the old site / zone).
            var journalsHere = roots.SelectMany(r => r.GetComponentsInChildren<JournalPickup>(true))
                .Where(j => FlatDist(j.transform.position, oldSite) < rSite + 40f || FlatDist(j.transform.position, oldZone) < 40f).ToList();
            sb.AppendLine($"  journals tied to the site: {(journalsHere.Count == 0 ? "none" : string.Join(", ", journalsHere.Select(j => j.entryId)))}");
            if (!apply) { sb.AppendLine("plan only - nothing changed"); return false; }

            Undo.RecordObject(site, "Move site");
            site.position = new Vector3(s.x, 0f, s.z);
            site.landingHint = l; site.hasLandingHint = true;
            EditorUtility.SetDirty(site);
            Undo.RecordObject(marker.transform, "Move site"); Undo.RecordObject(marker, "Move site");
            var face = Flat(s - l);
            marker.transform.SetPositionAndRotation(l, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : marker.transform.rotation);
            marker.siteHint = s; marker.hasSiteHint = true;
            marker.autoPlaced = true; marker.relaxedPick = false; marker.autoPosition = l;
            EditorUtility.SetDirty(marker);
            XenoarchSiteLayout.Reground(marker, terrain, cfg.starterShip, s, sb);
            Physics.SyncTransforms();

            // Journals: same placement rule as Place Journals, recomputed for the new spot.
            var jheight = JournalSpots.TerrainHeight(terrain);
            var slots = new Dictionary<string, int>(); var slotOf = new Dictionary<string, int>(); var entries = new Dictionary<string, JournalEntry>();
            foreach (var e in JournalSpots.For(body.Id, JournalPlacement.BaseLanding, JournalPlacement.FirstDigSite, JournalPlacement.SiteLanding))
            {
                string key = e.placement + ":" + e.siteIndex; slots.TryGetValue(key, out int slot); slots[key] = slot + 1;
                slotOf[e.Id] = slot; entries[e.Id] = e;
            }
            foreach (var j in journalsHere)
            {
                if (!entries.TryGetValue(j.entryId, out var e) || e.placement == JournalPlacement.BaseLanding) { sb.AppendLine($"  journal {j.entryId}: not a site placement - left"); continue; }
                if (!JournalSpots.TryPlace(e, slotOf[e.Id], scene, sites, jheight, out var pos, out var rot)) { sb.AppendLine($"  journal {j.entryId}: no spot - left"); continue; }
                Undo.RecordObject(j.transform, "Move site");
                var was = j.transform.position;
                j.transform.SetPositionAndRotation(pos, rot);
                EditorUtility.SetDirty(j.transform);
                sb.AppendLine($"  journal {j.entryId} ({e.placement}): ({was.x:0.0}, {was.z:0.0}) -> ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0})");
            }
            return true;
        }
    }
}
