using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Tools/Xenoarch/Landing/Spread Sites and Landing Zones: on every surface scene with landing zone markers, moves each
    /// dig site (its definition position) to flat, open ground well apart from the others and from the base zone
    /// (XenoarchConfig.siteLayout), and moves the site's LandingZone marker - with its LandingZone_ child (shuttle +
    /// arrival spawn) - beside it, re-grounding the shuttle. The base zone is never moved. Then re-bakes the surface maps
    /// and landing hints (setup step 12). Idempotent: a scene whose sites already satisfy the spacing is left as is.
    /// </summary>
    public static class XenoarchSiteLayout
    {
        public const string Trigger = "Temp/xenoarch_sitelayout.trigger";
        const string Log = "Logs/xenoarch_sitelayout.txt";

        [MenuItem("Tools/Xenoarch/Landing/Spread Sites and Landing Zones")]
        public static void RunMenu() => Run(null);

        public static void Run(ICollection<string> allowDirty)
        {
            var sb = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Xenoarch] Spread Sites: exit Play mode first"); return; }
            var cfg = Resources.Load<XenoarchConfig>(XenoarchConfig.ResourcePath);
            if (cfg == null || cfg.siteRegistry == null) { sb.AppendLine("No XenoarchConfig / site registry."); Finish(sb); return; }
            var ls = cfg.siteLayout ?? new SiteLayoutSettings();

            var vs = cfg.valuableSites;
            sb.AppendLine($"config: caches {vs.perBody} over the map + {vs.perLandingZone} per landing zone (ring {vs.zoneRing.x:0}-{vs.zoneRing.y:0} m)");
            sb.AppendLine($"config: dig yield x{cfg.digYieldMultiplier:0.##}, resource nodes x{cfg.resourceNodeMultiplier:0.##} ({cfg.resourceNodesNearZones:P0} near zones)");
            var def = cfg.starterShip;

            foreach (var name in new[] { SceneNames.MarsExcavation01, SceneNames.MoonExcavation01 })
            {
                string path = XenoarchSetup.ScenePath(name);
                if (!File.Exists(path)) { sb.AppendLine($"{name}: missing"); continue; }
                var open = SceneManager.GetSceneByPath(path);
                bool wasOpen = open.IsValid() && open.isLoaded;
                if (wasOpen && open.isDirty && (allowDirty == null || !allowDirty.Contains(name))) { sb.AppendLine($"{name}: open with unsaved changes - skipped (save it, then run again)"); continue; }
                var scene = wasOpen ? open : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    if (Layout(scene, cfg, ls, def, sb)) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); sb.AppendLine($"{name}: saved"); }
                }
                finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
            }
            AssetDatabase.SaveAssets();
            var maps = new StringBuilder();
            XenoarchMapsSetup.Run(maps);
            sb.AppendLine("maps:").Append(maps);
            Finish(sb);
        }

        static void Finish(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Log, sb.ToString());
            Debug.Log("[Xenoarch] Spread Sites and Landing Zones:\n" + sb);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static float FlatDist(Vector3 a, Vector3 b) => Vector3.Distance(Flat(a), Flat(b));

        static bool Layout(Scene scene, XenoarchConfig cfg, SiteLayoutSettings ls, ShipDefinition def, StringBuilder sb)
        {
            var roots = scene.GetRootGameObjects();
            Terrain terrain = null;
            foreach (var r in roots) { terrain = r.GetComponentInChildren<Terrain>(true); if (terrain != null) break; }
            var markers = roots.SelectMany(r => r.GetComponentsInChildren<LandingZone>(true)).ToList();
            var baseZone = markers.FirstOrDefault(m => string.IsNullOrEmpty(m.siteId));
            var sites = cfg.siteRegistry.Entries.Where(s => s != null && s.sceneName == scene.name).OrderBy(s => s.Id).ToList();
            if (terrain == null || terrain.terrainData == null || baseZone == null || sites.Count == 0) { sb.AppendLine($"{scene.name}: no terrain / base zone / sites - skipped"); return false; }
            Physics.SyncTransforms();
            var height = LandingZonePicker.ForTerrain(terrain);
            var basePos = baseZone.transform.position;
            var tp = terrain.transform.position; var size = terrain.terrainData.size;
            sb.AppendLine($"{scene.name}: terrain x {tp.x:0}..{tp.x + size.x:0}, z {tp.z:0}..{tp.z + size.z:0}; base zone at ({basePos.x:0}, {basePos.z:0})");

            // Already laid out?
            bool done = true;
            for (int i = 0; i < sites.Count && done; i++)
            {
                var m = markers.FirstOrDefault(z => z.siteId == sites[i].Id);
                if (m == null || FlatDist(m.transform.position, sites[i].position) > sites[i].radius * 1.42f + ls.landingOffset + 20f) done = false;
                if (FlatDist(sites[i].position, basePos) < ls.spacing * 0.5f) done = false;
                for (int j = 0; j < i; j++) if (FlatDist(sites[i].position, sites[j].position) < ls.spacing * 0.5f) done = false;
            }
            if (done) { sb.AppendLine($"{scene.name}: already laid out - left as is"); Report(sites, markers, basePos, sb); return false; }

            // Exclude his scene objects (anything with a collider that is not terrain / our zones) from the footprints.
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

            // Candidate grid inside the margins.
            var cands = new List<(Vector3 p, float score)>();
            float x0 = tp.x + ls.edgeMargin, x1 = tp.x + size.x - ls.edgeMargin, z0 = tp.z + ls.edgeMargin, z1 = tp.z + size.z - ls.edgeMargin;
            for (float x = x0; x <= x1; x += Mathf.Max(4f, ls.gridStep))
                for (float z = z0; z <= z1; z += Mathf.Max(4f, ls.gridStep))
                {
                    var p = new Vector3(x, 0f, z);
                    float db = FlatDist(p, basePos);
                    if (db < ls.spacing * 0.6f || db > ls.maxFromBase) continue;
                    if (!Flatish(p, ls.flatRadius, ls.maxSlope * 1.6f, ls.maxRelief * 2f, out float y, out float sc)) continue;
                    p.y = y; cands.Add((p, sc));
                }
            sb.AppendLine($"{scene.name}: {cands.Count} candidate spots");

            var picked = new List<Vector3> { basePos };
            var plan = new List<(ArchaeologicalSiteDefinition site, Vector3 s, Vector3 l)>();
            foreach (var site in sites)
            {
                float rSite = site.radius * 1.42f;
                bool found = false;
                foreach (var relax in new[] { 1f, 0.85f, 0.7f, 0.55f })
                {
                    float spacing = ls.spacing * relax, slopeMax = ls.maxSlope / Mathf.Sqrt(relax), reliefMax = ls.maxRelief / relax;
                    // Farthest-from-everything first, flatness as tie-break.
                    var order = cands.Where(c => picked.All(q => FlatDist(c.p, q) >= spacing))
                                     .OrderByDescending(c => Mathf.Min(picked.Min(q => FlatDist(c.p, q)), spacing * 1.6f) - c.score * 10f)
                                     .Take(400);
                    foreach (var c in order)
                    {
                        if (!Flatish(c.p, Mathf.Max(ls.flatRadius, rSite + 2f), slopeMax, reliefMax, out float sy, out _)) continue;
                        if (Blocked(new Vector3(c.p.x, sy, c.p.z), rSite + 1f)) continue;
                        var s = new Vector3(c.p.x, sy, c.p.z);
                        if (!FindLanding(s, basePos, rSite, ls, slopeMax, reliefMax, Flatish, Blocked, out var l)) continue;
                        plan.Add((site, s, l)); picked.Add(s); found = true;
                        if (relax < 1f) sb.AppendLine($"  {site.Id}: relaxed rules x{relax:0.##}");
                        break;
                    }
                    if (found) break;
                }
                if (!found) sb.AppendLine($"  {site.Id}: NO spot found - left where it was");
            }

            bool changed = false;
            foreach (var (site, s, l) in plan)
            {
                var m = markers.FirstOrDefault(z => z.siteId == site.Id);
                Undo.RecordObject(site, "Spread sites");
                site.position = new Vector3(s.x, 0f, s.z);
                site.landingHint = l; site.hasLandingHint = m != null;
                EditorUtility.SetDirty(site);
                if (m != null)
                {
                    Undo.RecordObject(m.transform, "Spread sites"); Undo.RecordObject(m, "Spread sites");
                    var face = Flat(s - l);
                    m.transform.SetPositionAndRotation(l, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : m.transform.rotation);
                    m.siteHint = s; m.hasSiteHint = true;
                    m.autoPlaced = true; m.relaxedPick = false; m.autoPosition = l;
                    EditorUtility.SetDirty(m);
                    Reground(m, terrain, def, s, sb);
                }
                else sb.AppendLine($"  {site.Id}: no LandingZone marker in the scene (site moved only)");
                changed = true;
            }
            Report(sites, markers, basePos, sb);
            return changed;
        }

        delegate bool FlatFn(Vector3 p, float r, float slopeMax, float reliefMax, out float y, out float score);

        static bool FindLanding(Vector3 s, Vector3 basePos, float rSite, SiteLayoutSettings ls, float slopeMax, float reliefMax,
            FlatFn flatish, System.Func<Vector3, float, bool> blocked, out Vector3 l)
        {
            var toBase = Flat(basePos - s); var dir0 = toBase.sqrMagnitude > 0.01f ? toBase.normalized : Vector3.forward;
            const float footprint = 9f;
            foreach (float extra in new[] { 0f, 8f, 16f })
                for (int k = 0; k < 18; k++)
                {
                    float ang = (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 20f;
                    var dir = Quaternion.Euler(0f, ang, 0f) * dir0;
                    var p = s + dir * (rSite + ls.landingOffset + extra);
                    if (!flatish(p, footprint, slopeMax * 1.2f, reliefMax * 1.3f, out float y, out _)) continue;
                    p.y = y;
                    if (blocked(p, footprint)) continue;
                    l = p; return true;
                }
            l = default; return false;
        }

        /// <summary>Shuttle: lowest gear renderer onto the highest terrain under craft / gear. Spawn: beside it, facing the site.</summary>
        static void Reground(LandingZone m, Terrain t, ShipDefinition def, Vector3 site, StringBuilder sb)
        {
            foreach (var slot in m.GetComponentsInChildren<LandingZoneSlot>(true))
            {
                var inst = slot.shuttle;
                if (inst != null)
                {
                    var gears = new List<Transform>();
                    if (def != null) foreach (var gn in def.shuttleGearNames ?? new string[0]) { var g = FindDeep(inst.transform, gn); if (g != null) gears.Add(g); }
                    float minY = float.PositiveInfinity;
                    foreach (var g in gears) foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
                    if (float.IsInfinity(minY)) foreach (var r in inst.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
                    float ground = Height(t, inst.transform.position) ?? m.transform.position.y;
                    foreach (var g in gears) { var h = Height(t, g.position); if (h.HasValue) ground = Mathf.Max(ground, h.Value); }
                    Undo.RecordObject(inst.transform, "Spread sites");
                    if (!float.IsInfinity(minY)) inst.transform.position += Vector3.up * (ground - minY);
                }
                if (slot.spawn != null)
                {
                    var tr = slot.spawn.transform; Undo.RecordObject(tr, "Spread sites");
                    var pos = tr.position; pos.y = (Height(t, pos) ?? pos.y) + 0.05f;
                    var face = Flat(site - pos);
                    tr.SetPositionAndRotation(pos, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : tr.rotation);
                }
            }
        }

        static void Report(List<ArchaeologicalSiteDefinition> sites, List<LandingZone> markers, Vector3 basePos, StringBuilder sb)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i]; var m = markers.FirstOrDefault(z => z.siteId == s.Id);
                var others = string.Join(", ", sites.Where(o => o != s).Select(o => $"{o.Id.Substring(o.Id.LastIndexOf('.') + 1)} {FlatDist(o.position, s.position):0} m"));
                sb.AppendLine($"  {s.Id} at ({s.position.x:0}, {s.position.z:0}): {FlatDist(s.position, basePos):0} m from base; zone {(m != null ? $"{FlatDist(m.transform.position, s.position):0} m from the site" : "none")}; to {others}");
            }
        }

        static float? Height(Terrain t, Vector3 p)
        {
            if (t == null || t.terrainData == null) return null;
            var tp = t.transform.position; var size = t.terrainData.size;
            if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) return null;
            return tp.y + t.SampleHeight(p);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }
    }
}
