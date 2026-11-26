using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Journals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Tools/Xenoarch/Journals/Place Journals: puts a datapad pickup (prefab Resources/Journals/XA_JournalPickup) for each
    /// JournalEntry with a scene placement (BaseLanding, FirstDigSite, SiteLanding) into every body's landing scene, at
    /// the spot <see cref="JournalSpots"/> computes from the data, grounded on the terrain, under a "Journals" root.
    /// Additive and idempotent: an entry that already has a pickup in the scene is left where it is (move them freely).
    /// Ship and NearCache entries are placed at runtime (JournalDirector). Also run at the end of Spread Sites.
    /// </summary>
    public static class XenoarchJournals
    {
        public const string Trigger = "Temp/xenoarch_journals.trigger";
        const string Log = "Logs/xenoarch_journals.txt";
        public const string PrefabPath = "Assets/_Project/Resources/Journals/XA_JournalPickup.prefab";

        [MenuItem("Tools/Xenoarch/Journals/Place Journals")]
        public static void RunMenu() { var sb = new StringBuilder(); Run(null, sb); Finish(sb); }

        public static void Run(ICollection<string> allowDirty, StringBuilder sb)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { sb.AppendLine("Exit Play mode first"); return; }
            var cfg = Resources.Load<XenoarchConfig>(XenoarchConfig.ResourcePath);
            if (cfg == null || cfg.galaxy == null) { sb.AppendLine("No config / galaxy"); return; }
            var prefab = EnsurePrefab(sb);
            sb.AppendLine($"{JournalService.All.Length} journal entries in Resources/{JournalService.ResourceFolder}");
            foreach (var (name, path) in XenoarchSiteLayout.SurfaceScenes(cfg))
            {
                var open = SceneManager.GetSceneByPath(path);
                bool wasOpen = open.IsValid() && open.isLoaded;
                if (wasOpen && open.isDirty && (allowDirty == null || !allowDirty.Contains(name))) { sb.AppendLine($"{name}: open with unsaved changes - skipped (save it, then run again; runtime places them meanwhile)"); continue; }
                var scene = wasOpen ? open : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    int made = PlaceScene(scene, cfg, prefab, sb);
                    if (made > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); sb.AppendLine($"{name}: {made} placed, saved"); }
                    else sb.AppendLine($"{name}: nothing new");
                }
                finally { if (!wasOpen) EditorSceneManager.CloseScene(scene, true); }
            }
        }

        static void Finish(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Log, sb.ToString());
            Debug.Log("[Xenoarch] Place Journals:\n" + sb);
        }

        public static void RunFromTrigger(ICollection<string> allowDirty) { var sb = new StringBuilder(); Run(allowDirty, sb); Finish(sb); }

        static GameObject EnsurePrefab(StringBuilder sb)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (p != null) return p;
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var go = new GameObject("XA_JournalPickup");
            go.AddComponent<JournalPickup>();
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.08f, 0f); bc.size = new Vector3(0.5f, 0.2f, 0.6f); bc.isTrigger = true;
            p = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            sb.AppendLine($"prefab created: {PrefabPath}");
            return p;
        }

        static int PlaceScene(Scene scene, XenoarchConfig cfg, GameObject prefab, StringBuilder sb)
        {
            var body = cfg.galaxy.FindBodyByScene(scene.name);
            if (body == null) return 0;
            var roots = scene.GetRootGameObjects();
            Terrain terrain = null;
            foreach (var r in roots) { terrain = r.GetComponentInChildren<Terrain>(true); if (terrain != null) break; }
            var height = JournalSpots.TerrainHeight(terrain);
            var present = new HashSet<string>(roots.SelectMany(r => r.GetComponentsInChildren<JournalPickup>(true)).Select(p => p.entryId));
            var sites = cfg.siteRegistry.Entries.Where(s => s != null && s.sceneName == scene.name).ToList();
            Transform holder = null;
            int made = 0;
            var slots = new Dictionary<string, int>();
            foreach (var e in JournalSpots.For(body.Id, JournalPlacement.BaseLanding, JournalPlacement.FirstDigSite, JournalPlacement.SiteLanding))
            {
                string key = e.placement + ":" + e.siteIndex; slots.TryGetValue(key, out int slot); slots[key] = slot + 1;
                if (present.Contains(e.Id)) continue;
                if (!JournalSpots.TryPlace(e, slot, scene, sites, height, out var pos, out var rot)) { sb.AppendLine($"  {e.Id}: no spot ({e.placement}) - runtime will retry"); continue; }
                if (holder == null)
                {
                    var hg = roots.FirstOrDefault(r => r.name == JournalSpots.RootName);
                    if (hg == null) { hg = new GameObject(JournalSpots.RootName); SceneManager.MoveGameObjectToScene(hg, scene); }
                    holder = hg.transform;
                }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                inst.name = JournalSpots.ObjectName(e.Id);
                inst.transform.SetParent(holder, true);
                inst.transform.SetPositionAndRotation(pos, rot);
                inst.GetComponent<JournalPickup>().entryId = e.Id;
                EditorUtility.SetDirty(inst);
                sb.AppendLine($"  {e.Id} ({e.placement}) at ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0})");
                made++;
            }
            return made;
        }
    }
}
