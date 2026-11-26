using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Clears the "MaterialLocation.External is obsolete" FBX importer warnings from the Creepy Cat kit without changing
    /// how anything looks: for every model still on the legacy External material location it records which material
    /// asset each renderer slot uses, switches the importer to InPrefab (embedded), then remaps every embedded
    /// material back to the exact same .mat asset via the importer's external-object remap. Only the .meta importer
    /// settings change; meshes, materials and prefabs are untouched.
    /// Menu: Tools/Xenoarch/Fix Creepy Cat FBX Material Location, or drop Temp/xenoarch_ccfix.trigger
    /// (Temp/xenoarch_ccfix_dry.trigger = report only). Report: Logs/xenoarch_ccfix.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchImporterFixes
    {
        const string Root = "Assets/_Creepy_Cat";
        const string Trigger = "Temp/xenoarch_ccfix.trigger", DryTrigger = "Temp/xenoarch_ccfix_dry.trigger";
        const string Report = "Logs/xenoarch_ccfix.txt";

        static XenoarchImporterFixes() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(DryTrigger)) { File.Delete(DryTrigger); Run(true); }
            else if (File.Exists(Trigger)) { File.Delete(Trigger); Run(false); }
        }

        [MenuItem("Tools/Xenoarch/Fix Creepy Cat FBX Material Location")]
        static void Menu() => Run(false);

#pragma warning disable 618 // ModelImporterMaterialLocation.External is the obsolete value we are migrating away from
        static bool IsLegacy(ModelImporter mi) => mi != null && mi.materialLocation == ModelImporterMaterialLocation.External;
#pragma warning restore 618

        sealed class Slot { public string path; public int index; public Material material; }

        public static void Run(bool dryRun)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} Creepy Cat FBX material location fix (dryRun={dryRun})");
            var targets = new List<string>();
            if (AssetDatabase.IsValidFolder(Root))
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (IsLegacy(AssetImporter.GetAtPath(p) as ModelImporter)) targets.Add(p);
                }
            sb.AppendLine($"legacy External importers: {targets.Count}");
            if (dryRun || targets.Count == 0) { foreach (var t in targets) sb.AppendLine("  " + t); Write(sb); return; }

            // 1) record the material asset each renderer slot uses today
            var before = new Dictionary<string, List<Slot>>();
            foreach (var p in targets) before[p] = Slots(p);

            // 2) switch to embedded materials (one batched reimport)
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var p in targets)
                {
                    var mi = (ModelImporter)AssetImporter.GetAtPath(p);
                    mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                    mi.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            // 3) remap each embedded material back to the same external .mat (second batched reimport)
            int remaps = 0, conflicts = 0, unchangedLook = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var p in targets)
                {
                    var mi = (ModelImporter)AssetImporter.GetAtPath(p);
                    var now = Slots(p);
                    var map = new Dictionary<string, Material>();
                    bool ok = true;
                    foreach (var old in before[p])
                    {
                        if (old.material == null || AssetDatabase.GetAssetPath(old.material) == p) continue;
                        var cur = now.Find(s => s.path == old.path && s.index == old.index);
                        if (cur == null || cur.material == null) { ok = false; continue; }
                        if (map.TryGetValue(cur.material.name, out var prev)) { if (prev != old.material) { conflicts++; ok = false; sb.AppendLine($"  CONFLICT {p}: '{cur.material.name}' -> {prev.name} / {old.material.name}"); } continue; }
                        map[cur.material.name] = old.material;
                    }
                    foreach (var kv in map) { mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value); remaps++; }
                    if (map.Count > 0) mi.SaveAndReimport();
                    if (ok) unchangedLook++;
                    sb.AppendLine($"  {p}: {map.Count} remap(s){(ok ? "" : "  (check)")}");
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            // 4) verify
            int still = 0, mismatched = 0;
            foreach (var p in targets)
            {
                if (IsLegacy(AssetImporter.GetAtPath(p) as ModelImporter)) still++;
                var now = Slots(p);
                foreach (var old in before[p])
                {
                    if (old.material == null) continue;
                    var cur = now.Find(s => s.path == old.path && s.index == old.index);
                    if (cur == null || cur.material != old.material) { mismatched++; sb.AppendLine($"  MISMATCH {p} {old.path}[{old.index}] {old.material.name} -> {(cur?.material != null ? cur.material.name : "null")}"); }
                }
            }
            sb.AppendLine($"done: {targets.Count} importers switched to InPrefab, {remaps} remaps, {conflicts} conflicts, {still} still legacy, {mismatched} slot mismatches, {unchangedLook} verified unchanged");
            Write(sb);
        }

        static List<Slot> Slots(string path)
        {
            var list = new List<Slot>();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return list;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                string rp = AnimationUtility.CalculateTransformPath(r.transform, go.transform);
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) list.Add(new Slot { path = rp, index = i, material = mats[i] });
            }
            return list;
        }

        static void Write(StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Report, sb.ToString());
            Debug.Log("[Xenoarch] Creepy Cat importer fix: " + sb.ToString().Split('\n')[1].Trim() + " (see " + Report + ")");
        }
    }
}
