using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>
    /// One-click preset: extract the audio from "Assets/Pro Sound Collection v4.26" (.uasset sounds) with UModel into
    /// "Assets/Pro Sound Collection v4.26 Audio", keeping the package folder structure. The source pack is only read.
    /// Stops with a clear message when umodel.exe is missing. Menu, or create Temp/xenoarch_prosound.trigger.
    /// Both folders are gitignored (vendor content).
    /// </summary>
    [InitializeOnLoad]
    public static class UE4ConvProSound
    {
        public const string Source = "Assets/Pro Sound Collection v4.26";
        public const string Output = "Assets/Pro Sound Collection v4.26 Audio";
        const string Trigger = "Temp/xenoarch_prosound.trigger";
        public const string LogPath = "Logs/xenoarch_prosound.txt";
        const string RuinsTrigger = "Temp/xenoarch_ruins.trigger";
        public const string RuinsSource = @"A:\Unity Assets\Ruins Treasure Prop Pack v4.26\Ruins Treasure Prop Pack\RuinsTre7e7190869711V1\data\Content\RuinsTreasureProps";
        public const string RuinsOutput = "Assets/Ruins Treasure Prop Pack";
        public const string RuinsLog = "Logs/xenoarch_ruins.txt";
        const string EgyptTrigger = "Temp/xenoarch_egypt.trigger";
        public const string EgyptSource = @"A:\Xenoarch\Assets\UE4 Packs\Egypt_Ruins_Environment";
        public const string EgyptOutput = "Assets/Egypt_Ruins_Environment";
        public const string EgyptLog = "Logs/xenoarch_egypt.txt";
        const string RelocTrigger = "Temp/xenoarch_ue4conv_reloc.trigger";
        public const string RelocLog = "Logs/xenoarch_ue4conv_reloc.txt";
        const string LightmapTrigger = "Temp/xenoarch_egypt_dellightmaps.trigger";
        public const string LightmapLog = "Logs/xenoarch_egypt_dellightmaps.txt";
        static double _next;

        static UE4ConvProSound() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(Trigger)) { File.Delete(Trigger); Run(false); }
            else if (File.Exists(RuinsTrigger)) { File.Delete(RuinsTrigger); RunRuins(false); }
            else if (File.Exists(EgyptTrigger)) { File.Delete(EgyptTrigger); RunEgypt(false); }
            else if (File.Exists(RelocTrigger)) { File.Delete(RelocTrigger); RelocateConvertedPacks(); }
            else if (File.Exists(LightmapTrigger)) { File.Delete(LightmapTrigger); DeleteEgyptLightmaps(); }
            else if (File.Exists("Temp/xenoarch_verify_converted.trigger")) { var p = File.ReadAllText("Temp/xenoarch_verify_converted.trigger").Trim(); File.Delete("Temp/xenoarch_verify_converted.trigger"); VerifyPrefabs(string.IsNullOrEmpty(p) ? RuinsOutput : p); }
        }

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity: Extract Pro Sound Collection Audio")]
        static void Menu() => Run(true);

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity: Convert Ruins Treasure Prop Pack")]
        static void RuinsMenu() => RunRuins(true);

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity: Convert Egypt Ruins Environment")]
        static void EgyptMenu() => RunEgypt(true);

        /// <summary>
        /// Moves already-converted packs from Assets/RUINS/&lt;Pack&gt; to Assets/&lt;Pack&gt; with AssetDatabase.MoveAsset so .meta GUIDs stay the same.
        /// </summary>
        public static void RelocateConvertedPacks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(RelocLog, "Refused: the Editor is in Play mode.\n");
                return;
            }
            var moves = new[]
            {
                ("Assets/RUINS/Egypt_Ruins_Environment", EgyptOutput),
                ("Assets/RUINS/Ruins Treasure Prop Pack", RuinsOutput),
            };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("s"));
            foreach (var (src, dst) in moves)
            {
                bool srcOk = AssetDatabase.IsValidFolder(src) || Directory.Exists(src);
                if (!srcOk) { sb.AppendLine("skip, source missing: " + src); continue; }
                if (AssetDatabase.IsValidFolder(dst) || Directory.Exists(dst)) { sb.AppendLine("skip, destination already exists: " + dst); continue; }
                string err = AssetDatabase.MoveAsset(src, dst);
                if (string.IsNullOrEmpty(err)) sb.AppendLine("moved " + src + " -> " + dst);
                else sb.AppendLine("FAILED " + src + " -> " + dst + ": " + err);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(RelocLog, sb.ToString());
            Debug.Log("[UE4 Converter] relocate:\n" + sb);
        }

        /// <summary>Deletes only the baked demo-level lightmaps under Textures/Map/*_BuiltData. Pack material textures are left alone.</summary>
        public static void DeleteEgyptLightmaps()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("s"));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Directory.CreateDirectory("Logs"); File.WriteAllText(LightmapLog, "Refused: the Editor is in Play mode.\n"); return;
            }
            string[] folders =
            {
                "Assets/Egypt_Ruins_Environment/Textures/Map/lavel_1_BuiltData",
                "Assets/Egypt_Ruins_Environment/Textures/Map/map_1_BuiltData",
            };
            int deleted = 0; long bytes = 0;
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) { sb.AppendLine("missing " + folder); continue; }
                foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
                int n = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
                bool ok = AssetDatabase.DeleteAsset(folder);
                sb.AppendLine((ok ? "deleted folder " : "FAILED to delete ") + folder + " (" + n + " assets)");
                if (ok) deleted += n;
            }
            string map = "Assets/Egypt_Ruins_Environment/Textures/Map";
            if (Directory.Exists(map))
            {
                var leftover = Directory.GetFiles(map, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (leftover.Length == 0)
                {
                    bool ok = AssetDatabase.DeleteAsset(map);
                    sb.AppendLine((ok ? "deleted empty " : "left ") + map);
                }
                else sb.AppendLine("left " + map + " (" + leftover.Length + " files remain)");
            }
            AssetDatabase.Refresh();
            sb.AppendLine("deleted=" + deleted + " bytes=" + bytes);
            Directory.CreateDirectory("Logs"); File.WriteAllText(LightmapLog, sb.ToString());
            Debug.Log("[UE4 Converter] delete Egypt lightmaps:\n" + sb);
        }

        public static void RunEgypt(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish("Refused: the Editor is in Play mode.", false, EgyptLog); return; }
            if (!Directory.Exists(EgyptSource)) { Finish("Source folder not found: " + EgyptSource, interactive, EgyptLog); return; }
            var o = ConverterOptions.Load();
            o.sourceFolder = EgyptSource; o.destOverride = EgyptOutput; o.audioSubfolder = "Audio"; o.packName = "Egypt_Ruins_Environment";
            o.soundsOnly = false; o.useUModel = true; o.meshFormat = UModelMesh.Psk; o.ueGame = "(auto-detect)"; o.exportPng = true; o.exportLods = false;
            o.dryRun = false; o.overwrite = false; o.reuseStaging = true; o.keepStaging = false; o.makePrefabs = true; o.copyAudio = true;
            o.maskMIsMRA = false;   // this pack uses explicit _OcclusionRoughnessMetallic / _BaseColor / _Normal names
            o.batchFolders = new System.Collections.Generic.List<string>();
            string exe = UModel.Find(o);
            if (exe == null) { Finish("Stopped: umodel.exe not found. " + UModel.MissingMessage, interactive, EgyptLog); return; }
            var rep = UE4Converter.Run(o);
            Finish(rep.Summary() + "\nUModel: " + rep.umodelSummary + "\nReport: " + rep.mdPath, interactive, EgyptLog);
        }

        public static void RunRuins(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish("Refused: the Editor is in Play mode.", false, RuinsLog); return; }
            if (!Directory.Exists(RuinsSource)) { Finish("Source folder not found: " + RuinsSource, interactive, RuinsLog); return; }
            var o = ConverterOptions.Load();
            o.sourceFolder = RuinsSource; o.destOverride = RuinsOutput; o.audioSubfolder = "Audio"; o.packName = "RuinsTreasureProps";
            o.soundsOnly = false; o.useUModel = true; o.meshFormat = UModelMesh.Psk; o.ueGame = "(auto-detect)"; o.exportPng = true; o.exportLods = false;
            o.dryRun = false; o.overwrite = true; o.reuseStaging = true; o.keepStaging = false; o.makePrefabs = true; o.copyAudio = true; o.maskMIsMRA = true;
            o.batchFolders = new System.Collections.Generic.List<string>();
            string exe = UModel.Find(o);
            if (exe == null) { Finish("Stopped: umodel.exe not found. " + UModel.MissingMessage, interactive, RuinsLog); return; }
            var rep = UE4Converter.Run(o);
            Finish(rep.Summary() + "\nUModel: " + rep.umodelSummary + "\nReport: " + rep.mdPath, interactive, RuinsLog);
        }

        /// <summary>Read-only check of converted prefabs: size in metres, face direction, material textures. Result in Logs/xenoarch_verify_converted.txt.</summary>
        public static void VerifyPrefabs(string folder)
        {
            var sb = new System.Text.StringBuilder();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            int bad = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { sb.AppendLine("LOAD FAIL " + path); bad++; continue; }
                var rs = go.GetComponentsInChildren<Renderer>(true);
                Bounds b = default; bool first = true; int tex = 0, mats = 0, noTex = 0; double vote = 0; long tris = 0; var notes = new System.Collections.Generic.List<string>();
                foreach (var r in rs)
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    foreach (var m in r.sharedMaterials) { mats++; if (m == null) { notes.Add("null material"); continue; } if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) tex++; else noTex++; }
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var me = mf.sharedMesh; var c = me.bounds.center;
                        var mda = Mesh.AcquireReadOnlyMeshData(me); var md0 = mda[0];
                        var vn = new Unity.Collections.NativeArray<Vector3>(md0.vertexCount, Unity.Collections.Allocator.Temp); md0.GetVertices(vn);
                        var tn = new Unity.Collections.NativeArray<int>((int)md0.GetSubMesh(0).indexCount, Unity.Collections.Allocator.Temp);
                        var v = vn.ToArray(); int[] t;
                        { var all = new System.Collections.Generic.List<int>(); for (int sm = 0; sm < md0.subMeshCount; sm++) { var si = md0.GetSubMesh(sm); var tmp = new Unity.Collections.NativeArray<int>(si.indexCount, Unity.Collections.Allocator.Temp); md0.GetIndices(tmp, sm); all.AddRange(tmp.ToArray()); tmp.Dispose(); } t = all.ToArray(); }
                        vn.Dispose(); tn.Dispose(); mda.Dispose();
                        tris += t.Length / 3;
                        for (int i = 0; i + 2 < t.Length; i += 3) { var a = v[t[i]]; var bb = v[t[i + 1]]; var cc = v[t[i + 2]]; vote += Vector3.Dot(Vector3.Cross(bb - a, cc - a), (a + bb + cc) / 3f - c) > 0 ? 1 : -1; }
                    }
                }
                string dir = tris > 0 ? ((vote / tris) >= 0.5 ? "outward" : "INWARD?") : "n/a(not readable)";
                sb.AppendLine($"{path.Substring(folder.Length)}  size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m  renderers {rs.Length}  mats {mats} (baseTex {tex}, none {noTex})  faces {dir} {(tris > 0 ? (vote / tris).ToString("0.00") : "")} {string.Join(",", notes)}");
            }
            sb.Insert(0, $"{guids.Length} prefabs, {bad} failed to load\n");
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/xenoarch_verify_converted.txt", sb.ToString());
        }

        static void Finish(string msg, bool dialog, string log = null)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(log ?? LogPath, DateTime.Now.ToString("s") + "\n" + msg + "\n");
            Debug.Log("[UE4 Converter] Pro Sound Collection: " + msg);
            if (dialog) EditorUtility.DisplayDialog("Pro Sound Collection audio", msg, "OK");
        }

        public static void Run(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish("Refused: the Editor is in Play mode.", false); return; }
            var o = ConverterOptions.Load();
            string root = UModel.ProjectRoot;
            if (!Directory.Exists(Path.Combine(root, Source))) { Finish("Source folder not found: " + Source, interactive); return; }

            o.sourceFolder = Path.Combine(root, Source);
            o.destOverride = Output; o.audioSubfolder = ""; o.packName = "ProSoundCollection_v4.26";
            o.soundsOnly = true; o.useUModel = true; o.meshFormat = UModelMesh.None; // verified: -game=ue4.26 makes UModel report every clip as empty; auto-detect exports them
            o.ueGame = "(auto-detect)"; o.exportPng = false; o.exportLods = false;
            o.dryRun = false; o.overwrite = false; o.reuseStaging = true; o.keepStaging = false; o.makePrefabs = false; o.copyAudio = true;
            o.batchFolders = new System.Collections.Generic.List<string>();

            string exe = UModel.Find(o);
            if (exe == null)
            {
                Finish("Stopped: umodel.exe was not found, so nothing was extracted or written.\n" +
                       @"Put it at  " + UModel.RecommendedDir + "\\umodel.exe  (umodel_64.exe also works; or set the path in Xenoarch > Tools > UE4 \u2192 Unity Converter > UModel).\n" +
                       "Download: " + UModel.DownloadPage + " (Win32 zip, extract it there). Then run this menu item again.", interactive);
                return;
            }
            if (interactive && !EditorUtility.DisplayDialog("Pro Sound Collection audio",
                    $"Extract the sounds from\n{Source}\ninto\n{Output}\nusing\n{exe}?\n\nThe source folder is only read.", "Extract", "Cancel")) return;

            var rep = UE4Converter.Run(o);
            string msg = rep.Summary() + "\nUModel: " + rep.umodelSummary + "\nReport: " + rep.mdPath;
            Finish(msg, interactive);
        }
    }
}