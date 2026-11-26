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
    /// Tools/Xenoarch/Landing/Build Landing Zones: in every build scene with <see cref="LandingZone"/> markers, adds under
    /// each marker a "LandingZone_&lt;id&gt;" child holding a preplaced Mark2Shuttle (grounded once on its gear) and the
    /// player's "shuttle" arrival spawn, plus one <see cref="LandingZoneSet"/> that switches them on/off at runtime.
    /// Additive and idempotent: markers that already have their zone are skipped, so zones are never re-positioned
    /// (hand-adjust them freely). Never moves existing objects. Scenes open with unsaved changes are skipped (staged).
    /// </summary>
    public static class XenoarchLandingZonesBuilder
    {
        public const string Trigger = "Temp/xenoarch_landing.trigger";
        const string Log = "Logs/xenoarch_landing.txt";

        [MenuItem("Tools/Xenoarch/Landing/Build Landing Zones")]
        public static void Run() => Run(null);

        /// <param name="allowDirty">Scene names the user approved building into while open with unsaved changes
        /// (their changes are saved with it). Trigger file content: "allow-dirty:SceneA,SceneB".</param>
        public static void Run(ICollection<string> allowDirty)
        {
            var sb = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Xenoarch] Build Landing Zones: exit Play mode first"); return; }
            var cfg = Resources.Load<XenoarchConfig>(XenoarchConfig.ResourcePath);
            var def = cfg != null ? cfg.starterShip : null;
            if (def == null || def.shuttlePrefab == null) { sb.AppendLine("No ShipDefinition.shuttlePrefab (XenoarchConfig.starterShip); nothing built."); Finish(sb); return; }
            sb.AppendLine($"shuttle prefab {AssetDatabase.GetAssetPath(def.shuttlePrefab)} scale {def.shuttleScale:0.###} gears [{string.Join(",", def.shuttleGearNames ?? new string[0])}]");

            foreach (var bs in EditorBuildSettings.scenes)
            {
                if (!bs.enabled || !File.Exists(bs.path)) continue;
                var text = File.ReadAllText(bs.path);
                bool hasMarkers = text.Contains("DMI.Xenoarch.Exploration.LandingZone") || text.Contains("LandingZone");
                if (!hasMarkers) continue;
                var open = SceneManager.GetSceneByPath(bs.path);
                bool wasOpen = open.IsValid() && open.isLoaded;
                if (wasOpen && open.isDirty && (allowDirty == null || !allowDirty.Contains(open.name))) { sb.AppendLine($"{bs.path}: open with unsaved changes - skipped (save it, then run again)"); continue; }
                var scene = wasOpen ? open : EditorSceneManager.OpenScene(bs.path, OpenSceneMode.Additive);
                try
                {
                    int built = BuildScene(scene, def, sb);
                    if (built > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); sb.AppendLine($"{scene.name}: {built} zone(s) built, saved"); }
                    else sb.AppendLine($"{scene.name}: nothing new");
                }
                finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
            }
            Finish(sb);
        }

        static void Finish(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Log, sb.ToString());
            Debug.Log("[Xenoarch] Build Landing Zones:\n" + sb);
        }

        static int BuildScene(Scene scene, ShipDefinition def, StringBuilder sb)
        {
            var roots = scene.GetRootGameObjects();
            var markers = roots.SelectMany(r => r.GetComponentsInChildren<LandingZone>(true)).ToList();
            if (markers.Count == 0) return 0;
            Terrain terrain = null;
            foreach (var r in roots) { terrain = r.GetComponentInChildren<Terrain>(true); if (terrain != null) break; }
            int built = 0;
            LandingZoneSet set = roots.SelectMany(r => r.GetComponentsInChildren<LandingZoneSet>(true)).FirstOrDefault();
            if (set == null)
            {
                var host = markers[0].transform.parent != null ? markers[0].transform.parent.gameObject : null;
                if (host == null || host.GetComponent<LandingZone>() != null)
                {
                    host = new GameObject("LandingZoneSet");
                    SceneManager.MoveGameObjectToScene(host, scene);
                }
                set = host.AddComponent<LandingZoneSet>();
                built++; // scene changed
                sb.AppendLine($"{scene.name}: LandingZoneSet on '{host.name}'");
            }
            if (terrain != null)
                sb.AppendLine($"{scene.name}: terrain '{terrain.name}' world y {terrain.transform.position.y:F2} (parent {(terrain.transform.parent != null ? terrain.transform.parent.name + " y " + terrain.transform.parent.position.y.ToString("F2") : "none")}), size {terrain.terrainData.size}");
            foreach (var m in markers)
            {
                var mp = m.transform.position;
                sb.AppendLine($"  marker {m.name} at {mp:F2}: terrain height there {(Height(terrain, mp)?.ToString("F2") ?? "outside terrain")}, SampleHeight raw {(terrain != null ? terrain.SampleHeight(mp).ToString("F2") : "-")}");
            }
            foreach (var m in markers)
            {
                string id = string.IsNullOrEmpty(m.siteId) ? "base" : m.siteId;
                string name = "LandingZone_" + id;
                if (m.transform.Cast<Transform>().Any(c => c.GetComponent<LandingZoneSlot>() != null || c.name == name)) { sb.AppendLine($"  {m.name}: zone exists, left as is"); continue; }

                var zone = new GameObject(name);
                zone.transform.SetParent(m.transform, false);
                var slot = zone.AddComponent<LandingZoneSlot>();
                slot.siteId = m.siteId ?? "";

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(def.shuttlePrefab, scene);
                inst.transform.SetParent(zone.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.Euler(0f, def.shuttleYawOffset, 0f);
                if (def.shuttleScale > 0.01f) inst.transform.localScale = Vector3.one * def.shuttleScale;
                var gears = new List<Transform>();
                foreach (var gn in def.shuttleGearNames ?? new string[0])
                {
                    var g = FindDeep(inst.transform, gn);
                    if (g != null) { g.gameObject.SetActive(true); gears.Add(g); } else sb.AppendLine($"  {m.name}: gear '{gn}' not found");
                }
                foreach (var mc in inst.GetComponentsInChildren<MeshCollider>(true)) if (mc.convex && mc.GetComponentInParent<Rigidbody>(true) == null) mc.convex = false;
                foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                foreach (var bay in inst.GetComponentsInChildren<DMI.Xenoarch.Ship.ShuttleBayShuttle>(true)) { try { Object.DestroyImmediate(bay); } catch (System.Exception e) { sb.AppendLine($"  could not remove ShuttleBayShuttle from the instance: {e.Message}"); } }
                if (inst.GetComponent<DMI.Xenoarch.Ship.ShuttleInteractable>() == null) inst.AddComponent<DMI.Xenoarch.Ship.ShuttleInteractable>();
                slot.shuttle = inst;

                // Ground once: lowest gear renderer (world bounds) on the highest terrain under the craft / gear.
                float oldEstimate = OldGearMinY(zone.transform, gears);
                float minY = float.PositiveInfinity;
                foreach (var g in gears) foreach (var r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
                if (float.IsInfinity(minY)) foreach (var r in inst.GetComponentsInChildren<Renderer>()) if (r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
                float ground = Height(terrain, inst.transform.position) ?? m.transform.position.y;
                foreach (var g in gears) { var h = Height(terrain, g.position); if (h.HasValue) ground = Mathf.Max(ground, h.Value); }
                if (!float.IsInfinity(minY)) inst.transform.position += Vector3.up * (ground - minY);
                sb.AppendLine($"  {m.name}: shuttle at {inst.transform.position:F2} (gear bottom was {minY - zone.transform.position.y:F2} m from the marker; old inactive-mesh estimate {oldEstimate:F2} m)");

                // Player arrival beside the shuttle, facing the site.
                var sp = new GameObject("XA_Player Spawn (shuttle)");
                sp.transform.SetParent(zone.transform, false);
                Vector3 pos;
                if (m.playerOffset != Vector3.zero) pos = m.transform.TransformPoint(m.playerOffset);
                else
                {
                    var b = new Bounds(inst.transform.position, Vector3.one);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
                    var toSite = m.hasSiteHint ? m.siteHint - m.transform.position : m.transform.forward; toSite.y = 0f;
                    var dir = toSite.sqrMagnitude > 0.01f ? toSite.normalized : Vector3.forward;
                    pos = b.center + dir * (Mathf.Max(b.extents.x, b.extents.z) + 2f);
                }
                pos.y = (Height(terrain, pos) ?? pos.y) + 0.05f;
                var face = (m.hasSiteHint ? m.siteHint : pos + m.transform.forward) - pos; face.y = 0f;
                sp.transform.SetPositionAndRotation(pos, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
                var spawn = sp.AddComponent<PlayerSpawnPoint>();
                spawn.spawnId = ShipTravel.ShuttleSpawn;
                slot.spawn = spawn;
                built++;
            }
            return built;
        }

        static float? Height(Terrain t, Vector3 p)
        {
            if (t == null || t.terrainData == null) return null;
            var tp = t.transform.position; var size = t.terrainData.size;
            if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) return null;
            return tp.y + t.SampleHeight(p);
        }

        /// <summary>The runtime's previous estimate (holder space, from mesh bounds), logged for comparison.</summary>
        static float OldGearMinY(Transform holder, List<Transform> gears)
        {
            float min = float.PositiveInfinity; var toLocal = holder.worldToLocalMatrix;
            foreach (var g in gears)
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds; var mm = toLocal * mf.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                        min = Mathf.Min(min, mm.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))).y);
                }
            return min;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }
    }
}
