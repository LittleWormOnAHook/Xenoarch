using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    public enum ConvScaleMode { Auto, UnrealCm, None, Custom }
    public enum RigMode { Auto, Generic, Humanoid }
    public enum TexRole { Unknown, BaseColor, Normal, Packed, Roughness, Metallic, AO, Emissive, Height, Opacity, Specular }
    public enum MeshKind { Static, Skeletal, AnimOnly }
    public enum ReportKind { Converted, Planned, Skipped, Warning, Unconvertible, Info }
    public enum UModelMesh { Gltf, Psk, None }

    /// <summary>Everything the converter window exposes. Persisted (JSON) in EditorPrefs.</summary>
    [Serializable]
    public class ConverterOptions
    {
        public string sourceFolder = "";
        public string destRoot = "Assets/_Vendor/Converted";
        public string packName = "";
        public ConvScaleMode scaleMode = ConvScaleMode.Auto;
        public float customScale = 0.01f;
        public bool bakeAxis = true;
        public bool objIsZUp = false;
        public int extraYaw = 0;
        public bool generateColliders = true;
        public bool detectLods = true;
        public bool dryRun = false;
        public bool overwrite = false;
        public bool flipNormalGreen = true;
        public bool cutoutFoliage = true;
        public int maxTextureSize = 4096;
        public bool lightmapUVs = false;
        public RigMode rig = RigMode.Auto;
        public bool importAnimations = true;
        public bool copyAudio = true;
        public bool makePrefabs = true;

        // UModel (UE Viewer) extraction for .uasset sources
        public bool useUModel = true;
        public string umodelExe = "";
        public string ueGame = "(auto-detect)";   // forcing a tag breaks packages whose own version differs (the "v4.26" Pro Sound pack is a 4.10-era package)
        public UModelMesh meshFormat = UModelMesh.Psk;   // Psk is converted to OBJ internally (no package needed); Gltf needs glTFast
        public bool exportPng = true;
        public bool exportLods = false;
        public bool keepStaging = false;
        public bool maskMIsMRA = false;                // "_M" textures are packed masks: R=Metallic, G=Roughness, B=AO (inferred per pack)
        public bool soundsOnly = false;                 // skip meshes/textures/animations (UModel -nomesh -nostat -noanim -notex)
        public string destOverride = "";                // exact destination folder (ignores destRoot/packName)
        public string audioSubfolder = "Audio";         // "" puts clips straight under the destination
        public bool reuseStaging = true;
        // batch: each folder is converted as its own pack (pack name = folder name) under destRoot
        public List<string> batchFolders = new List<string>();

        const string PrefKey = "Xenoarch.UE4Converter.Options";

        public string DestFolder => !string.IsNullOrWhiteSpace(destOverride) ? destOverride.Replace('\\', '/').TrimEnd('/') : destRoot.Replace('\\', '/').TrimEnd('/') + "/" + Sanitize(string.IsNullOrEmpty(packName) ? "Pack" : packName);

        public static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            var r = sb.ToString().Trim('.', '_');
            return r.Length == 0 ? "Pack" : r;
        }

        public static ConverterOptions Load()
        {
            var o = new ConverterOptions();
            try { var j = EditorPrefs.GetString(PrefKey, ""); if (!string.IsNullOrEmpty(j)) JsonUtility.FromJsonOverwrite(j, o); } catch { }
            o.dryRun = false;
            return o;
        }

        public void Save() => EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(this));
    }

    [Serializable]
    public class ReportItem { public string kind, category, source, dest, message; }

    [Serializable]
    public class ConversionReport
    {
        public string tool = "Xenoarch UE4 -> Unity Converter";
        public string pack, sourceFolder, destFolder, startedAt, finishedAt;
        public bool dryRun, cancelled;
        public List<ReportItem> items = new List<ReportItem>();
        public List<string> created = new List<string>();
        public List<string> overwritten = new List<string>();
        public string umodelLog, umodelSummary;
        [NonSerialized] public string mdPath, jsonPath;

        public void Add(ReportKind k, string category, string source, string dest, string message)
            => items.Add(new ReportItem { kind = k.ToString(), category = category, source = source, dest = dest, message = message });

        public int Count(ReportKind k) { var s = k.ToString(); int n = 0; foreach (var i in items) if (i.kind == s) n++; return n; }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendLine(dryRun ? "DRY RUN (nothing written)" : (cancelled ? "CANCELLED (partial)" : "Done"));
            sb.AppendLine($"Converted {Count(ReportKind.Converted)}   Planned {Count(ReportKind.Planned)}   Skipped {Count(ReportKind.Skipped)}   Warnings {Count(ReportKind.Warning)}   Unconvertible {Count(ReportKind.Unconvertible)}");
            foreach (var g in items.Where(i => i.kind == nameof(ReportKind.Converted) || i.kind == nameof(ReportKind.Planned)).GroupBy(i => i.category).OrderBy(g => g.Key))
                sb.AppendLine($"  {g.Key}: {g.Count()}");
            foreach (var g in items.Where(i => i.kind == nameof(ReportKind.Unconvertible)).GroupBy(i => i.category).OrderBy(g => g.Key))
                sb.AppendLine($"  Not convertible - {g.Key}: {g.Count()}");
            int w = 0;
            foreach (var i in items.Where(i => i.kind == nameof(ReportKind.Warning)))
            {
                if (w++ >= 12) { sb.AppendLine("  ... more warnings in the report"); break; }
                sb.AppendLine("  ! " + i.message + (string.IsNullOrEmpty(i.source) ? "" : "  [" + Path.GetFileName(i.source) + "]"));
            }
            return sb.ToString();
        }

        public void Write(string folder)
        {
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string stem = Path.Combine(folder, ConverterOptions.Sanitize(pack) + "_" + stamp + (dryRun ? "_dryrun" : ""));
            jsonPath = stem + ".json"; mdPath = stem + ".md";
            File.WriteAllText(jsonPath, JsonUtility.ToJson(this, true));
            var sb = new StringBuilder();
            sb.AppendLine($"# UE4 -> Unity conversion report: {pack}");
            sb.AppendLine();
            sb.AppendLine($"- Source: `{sourceFolder}`");
            sb.AppendLine($"- Destination: `{destFolder}`");
            sb.AppendLine($"- Started: {startedAt}   Finished: {finishedAt}");
            sb.AppendLine($"- Mode: {(dryRun ? "dry run (nothing written)" : "converted")}{(cancelled ? " - CANCELLED, partial result" : "")}");
            sb.AppendLine($"- Converted {Count(ReportKind.Converted)}, planned {Count(ReportKind.Planned)}, skipped {Count(ReportKind.Skipped)}, warnings {Count(ReportKind.Warning)}, not convertible {Count(ReportKind.Unconvertible)}");
            if (!string.IsNullOrEmpty(umodelSummary)) { sb.AppendLine($"- UModel: {umodelSummary}"); if (!string.IsNullOrEmpty(umodelLog)) sb.AppendLine($"- UModel log: `{umodelLog}`"); }
            sb.AppendLine();
            foreach (var kind in new[] { ReportKind.Warning, ReportKind.Unconvertible, ReportKind.Skipped, ReportKind.Converted, ReportKind.Planned, ReportKind.Info })
            {
                var list = items.Where(i => i.kind == kind.ToString()).ToList();
                if (list.Count == 0) continue;
                sb.AppendLine($"## {kind} ({list.Count})");
                foreach (var g in list.GroupBy(i => i.category).OrderBy(g => g.Key))
                {
                    sb.AppendLine($"### {g.Key} ({g.Count()})");
                    foreach (var i in g)
                    {
                        sb.Append("- ");
                        if (!string.IsNullOrEmpty(i.source)) sb.Append($"`{i.source}`");
                        if (!string.IsNullOrEmpty(i.dest)) sb.Append($" -> `{i.dest}`");
                        if (!string.IsNullOrEmpty(i.message)) sb.Append((string.IsNullOrEmpty(i.source) && string.IsNullOrEmpty(i.dest) ? "" : ": ") + i.message);
                        sb.AppendLine();
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine("## Notes");
            sb.AppendLine("- Asset creation is not part of Unity's Undo stack. The window's **Revert last conversion** deletes the assets this run created (listed in the JSON report under `created`); assets that already existed and were overwritten are not reverted.");
            sb.AppendLine("- Unreal `.uasset` / `.umap` files are binary and cannot be read outside the engine. In UE4: Content Browser > right-click > Asset Actions > Export (FBX/OBJ for meshes, PNG/TGA for textures), or use the pack's Source folder, then point this tool at the export.");
            File.WriteAllText(mdPath, sb.ToString());
        }
    }

    // ---- Planning model -------------------------------------------------------------------------------------------

    internal sealed class TexEntry
    {
        public string src, relDir, stem, ext, key, normKey;
        public TexRole role;
        public int[] layout;      // packed: channel index of {AO, roughness, metallic}
        public bool invert, gl, dx;
    }

    internal sealed class SetPlan
    {
        public string relDir, key, normKey;
        public List<TexEntry> all = new List<TexEntry>();
        public TexEntry baseColor, normal, packed, rough, metal, ao, emissive, height, opacity;
        public string outBase, outNormal, outMetalSmooth, outAO, outEmissive, outHeight;
        public bool mergeOpacity, convertNormal, packMS, packAO, baseAlpha, cutout, alphaKnown;
    }

    internal sealed class MeshEntry
    {
        public string src, relDir, ext, stem, baseName;
        public int lod = -1;
        public MeshKind kind;
        public bool hasAnim, hasUcx, nameIsAnim, probed;
        public string destAsset;
        public float objExtent, scale = 1f;
        public bool useFileScale = true;
        public string scaleNote = "";
        public bool IsObj => ext == ".obj";
        public bool IsGltf => ext == ".glb" || ext == ".gltf";
    }

    internal sealed class PrefabJob
    {
        public string name, prefabPath, relDir;
        public List<MeshEntry> lods = new List<MeshEntry>();
    }

    internal sealed class Plan
    {
        public ConverterOptions o;
        public string src, dest, stagingRoot;
        public int ueFiles;
        public List<string> pendingGltf = new List<string>();
        public List<MeshEntry> meshes = new List<MeshEntry>();
        public List<SetPlan> sets = new List<SetPlan>();
        public List<PrefabJob> prefabs = new List<PrefabJob>();
        public List<(string src, string dest)> audio = new List<(string, string)>();
        public List<(string src, string dest)> aux = new List<(string, string)>();
        public Dictionary<string, List<SetPlan>> setsByNorm = new Dictionary<string, List<SetPlan>>();
        public string TmpFolder => dest + "/__convtmp";
    }

    // ---- Import settings handed to the AssetPostprocessor -----------------------------------------------------------

    internal sealed class ModelJob
    {
        public MeshKind kind; public bool isObj, hasAnim, useFileScale, humanoid; public float globalScale = 1f; public string stem; public int stage = 1;
    }

    internal sealed class AudioJob
    {
        public UnityEngine.AudioClipLoadType load; public float seconds;
    }

    internal sealed class TexJob
    {
        public TexRole role; public bool alpha, cutout, readTemp, metalSmooth;
    }

    internal static class Registry
    {
        public static readonly Dictionary<string, ModelJob> Models = new Dictionary<string, ModelJob>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, TexJob> Tex = new Dictionary<string, TexJob>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, AudioJob> Audio = new Dictionary<string, AudioJob>(StringComparer.OrdinalIgnoreCase);
        public static ConverterOptions Opts;
        public static void Clear() { Models.Clear(); Tex.Clear(); Audio.Clear(); Opts = null; }
        public static string N(string p) => p.Replace('\\', '/');
    }
}