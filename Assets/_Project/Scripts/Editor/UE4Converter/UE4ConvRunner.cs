using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>
    /// Runs a conversion: plan (read-only) -> write/convert files -> batched import -> animation/avatar fix-ups -> URP materials + prefabs.
    /// Never touches scenes, never enters Play mode, writes only under the chosen destination folder (plus Logs/UE4Converter reports).
    /// </summary>
    public static class UE4Converter
    {
        public const string ReportFolder = "Logs/UE4Converter";
        public const string LastReportPref = "Xenoarch.UE4Converter.LastJson";
        static readonly Regex UcxRx = new Regex(@"^(UCX|UBX|USP|UCP)_", RegexOptions.IgnoreCase);
        static readonly Regex HumanoidRx = new Regex(@"(mannequin|humanoid|character|player|human|npc|biped|manny|quinn|astronaut|crew)", RegexOptions.IgnoreCase);

        internal static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Disk(string asset) => ImageIO.DiskPath(asset);

        public static string Validate(ConverterOptions o)
        {
            if (string.IsNullOrWhiteSpace(o.sourceFolder) || !Directory.Exists(o.sourceFolder)) return "Source folder does not exist.";
            if (string.IsNullOrWhiteSpace(o.packName)) return "Pack name is empty.";
            string d = o.DestFolder.ToLowerInvariant() + "/";
            if (!d.StartsWith("assets/")) return "Destination must be inside the project's Assets folder.";
            foreach (var bad in new[] { "/plugins/", "/digger/", "/diggerdata/", "/_creepy_cat/", "/_vendor/", "/planet forge", "/planetforge" })
                if (d.Contains(bad)) return "Refusing to write into a vendor folder (" + bad.Trim('/') + ").";
            if (!o.dryRun && EditorApplication.isPlayingOrWillChangePlaymode) return "Editor is in (or entering) Play mode; conversion writes assets and is refused.";
            return null;
        }

        public static ConversionReport Run(ConverterOptions o)
        {
            var rep = new ConversionReport { pack = o.packName, sourceFolder = o.sourceFolder, destFolder = o.DestFolder, dryRun = o.dryRun, startedAt = DateTime.Now.ToString("s") };
            bool disallowed = false;
            try
            {
                string err = Validate(o);
                if (err != null) { rep.Add(ReportKind.Warning, "Setup", null, null, err); return Finish(rep, o); }
                Registry.Clear(); Registry.Opts = o;
                string ign = GitIgnoreWarning(o.DestFolder);
                if (ign != null) rep.Add(ReportKind.Warning, "Git", null, o.DestFolder, ign);
                string staging = StageUModel(o, rep);
                if (rep.cancelled) return Finish(rep, o);
                if (staging != null && o.useUModel && o.meshFormat == UModelMesh.Psk && !o.soundsOnly) ConvertPsk(staging, rep);
                var plan = Planner.Build(o, rep, staging);
                if (o.dryRun) DryReport(plan, rep);
                else
                {
                    AssetDatabase.DisallowAutoRefresh(); disallowed = true;
                    Execute(plan, rep);
                }
            }
            catch (Exception e)
            {
                rep.Add(ReportKind.Warning, "Exception", null, null, e.ToString());
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Registry.Clear();
                if (disallowed) AssetDatabase.AllowAutoRefresh();
            }
            if (!o.dryRun) AssetDatabase.Refresh();
            return Finish(rep, o);
        }

        static ConversionReport Finish(ConversionReport rep, ConverterOptions o)
        {
            rep.finishedAt = DateTime.Now.ToString("s");
            try
            {
                rep.Write(ReportFolder);
                if (!rep.dryRun && rep.created.Count + rep.overwritten.Count > 0) EditorPrefs.SetString(LastReportPref, rep.jsonPath);
            }
            catch (Exception e) { Debug.LogWarning("[UE4 Converter] could not write the report: " + e.Message); }
            return rep;
        }

        static bool Prog(string title, string info, float f) => !EditorUtility.DisplayCancelableProgressBar("UE4 -> Unity: " + title, info, Mathf.Clamp01(f));

        // ---- UModel extraction ------------------------------------------------------------------------------------

        /// <summary>Runs UModel over a .uasset source into Library/UE4Converter/staging/&lt;pack&gt;. Returns the staging folder, or null.</summary>
        /// <summary>UModel PSKX static meshes -> OBJ/MTL in the staging folder (Unity imports those natively).</summary>
        static void ConvertPsk(string staging, ConversionReport rep)
        {
            if (!Directory.Exists(staging)) return;
            var r = PskToObj.ConvertTree(staging, name => Prog("Converting PSK meshes", name, 0.5f));
            EditorUtility.ClearProgressBar();
            if (r.converted + r.skippedSkeletal + r.failed == 0) return;
            rep.Add(ReportKind.Info, "PSK -> OBJ", null, null, $"{r.converted} static mesh(es) converted from PSKX to OBJ ({r.tris} new triangles). Winding was set per mesh so faces point outward; normals come from the smoothing groups.");
            foreach (var s in r.skeletal.Take(30)) rep.Add(ReportKind.Unconvertible, "Skeletal PSK", s.Replace(staging, "").TrimStart('\\', '/'), null, "Skeletal meshes (bones/weights) cannot be converted without glTF. Re-run with mesh format glTF and glTFast installed.");
            foreach (var e in r.errors) rep.Add(ReportKind.Warning, "PSK -> OBJ", null, null, "failed: " + e);
        }

        static string StageUModel(ConverterOptions o, ConversionReport rep)
        {
            if (!o.useUModel) return null;
            int pkgs;
            try { pkgs = Directory.EnumerateFiles(o.sourceFolder, "*.uasset", SearchOption.AllDirectories).Count(); } catch { pkgs = 0; }
            if (pkgs == 0) return null;

            string exe = UModel.Find(o);
            string staging = UModel.StagingDir(o.packName);
            if (exe == null)
            {
                rep.Add(ReportKind.Warning, "UModel", null, null, UModel.MissingMessage);
                rep.umodelSummary = "umodel.exe not found; .uasset files were only listed";
                return null;
            }
            string args = UModel.BuildArgs(o, o.sourceFolder, staging);
            if (o.dryRun)
            {
                rep.Add(ReportKind.Info, "UModel", null, null, $"would extract {pkgs} .uasset packages with: \"{exe}\" {args}");
                rep.umodelSummary = $"dry run: would extract {pkgs} packages with {exe}";
                return null;
            }
            if (o.reuseStaging && UModel.CountFiles(staging) > 0)
            {
                rep.umodelSummary = "reused an earlier extraction (" + UModel.Describe(staging) + ")";
                rep.Add(ReportKind.Info, "UModel", null, staging, "reusing the earlier extraction; turn off 'Reuse staging' to run UModel again");
                return staging;
            }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); Directory.CreateDirectory(staging); }
            catch (Exception e) { rep.Add(ReportKind.Warning, "UModel", null, staging, "could not prepare staging folder: " + e.Message); return null; }

            string logPath = Path.Combine(ReportFolder, ConverterOptions.Sanitize(o.packName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_umodel.log");
            var r = UModel.Run(exe, args, logPath, (line, n) =>
                EditorUtility.DisplayCancelableProgressBar("UE4 -> Unity: UModel extracting " + o.packName,
                    $"{n} objects exported ({pkgs} .uasset packages)  {(line != null && line.Length > 90 ? line.Substring(0, 90) : line)}",
                    Mathf.PingPong((float)EditorApplication.timeSinceStartup * 0.15f, 1f)));
            rep.umodelLog = logPath;
            int files = UModel.CountFiles(staging);
            rep.umodelSummary = $"exit code {r.exitCode}, {files} files extracted ({UModel.Describe(staging)}); exported objects: " +
                                (r.exported.Count == 0 ? "none logged" : string.Join(", ", r.exported.OrderByDescending(k => k.Value).Select(k => k.Value + " " + k.Key)));
            if (!r.started) { rep.Add(ReportKind.Warning, "UModel", null, null, r.summary); return null; }
            if (r.cancelled) { rep.cancelled = true; rep.Add(ReportKind.Warning, "Cancelled", null, null, "UModel extraction cancelled; nothing was converted. Files extracted so far stay in " + staging); return null; }
            if (r.exitCode != 0) rep.Add(ReportKind.Warning, "UModel", null, null, "UModel exited with code " + r.exitCode + "; see the log.");
            if (r.errors.Any(e => e.IndexOf("Unversioned", StringComparison.OrdinalIgnoreCase) >= 0))
                rep.Add(ReportKind.Warning, "UModel", null, null, "UModel says these UE4 packages are unversioned: pick the pack's engine version (for example ue4.26 for 'v4.26' packs) in the UModel section and run again.");
            foreach (var e in r.errors.Take(15)) rep.Add(ReportKind.Warning, "UModel log", null, null, e);
            if (r.errors.Count > 15) rep.Add(ReportKind.Warning, "UModel log", null, null, (r.errors.Count - 15) + " more error/warning lines in the log");
            if (files == 0)
            {
                rep.Add(ReportKind.Warning, "UModel", null, null, "UModel produced no files. Usual causes: wrong engine version (-game), packs that need an AES key, or the folder is not a 'Content' folder with .uasset/.uexp pairs.");
                return null;
            }
            return staging;
        }

        /// <summary>Vendor-derived output must stay out of git. Returns a warning if the destination is NOT ignored (null if ignored or git is unavailable).</summary>
        public static string GitIgnoreWarning(string destAsset)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "check-ignore -q -- \"" + destAsset + "/x\"")
                { WorkingDirectory = UModel.ProjectRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return null; }
                    if (p.ExitCode == 1) return "The destination is NOT covered by .gitignore, so vendor-derived assets could be committed. Use Assets/_Vendor/Converted (ignored) or add an ignore pattern.";
                }
            }
            catch { }
            return null;
        }

        // ---- batch --------------------------------------------------------------------------------------------------

        [Serializable] sealed class BatchManifest { public List<string> reports = new List<string>(); }

        /// <summary>Converts every folder in o.batchFolders as its own pack (pack name = folder name) under o.destRoot.</summary>
        public static List<ConversionReport> RunBatch(ConverterOptions o)
        {
            var list = new List<ConversionReport>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in o.batchFolders.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var c = JsonUtility.FromJson<ConverterOptions>(JsonUtility.ToJson(o));
                c.batchFolders = new List<string>();
                c.sourceFolder = f;
                string n = ConverterOptions.Sanitize(new DirectoryInfo(f.TrimEnd('\\', '/')).Name), u = n;
                for (int k = 2; !names.Add(u); k++) u = n + "_" + k;
                c.packName = u;
                var r = Run(c);
                list.Add(r);
                if (r.cancelled) break;
            }
            var changed = list.Where(r => !r.dryRun && r.jsonPath != null && r.created.Count + r.overwritten.Count > 0).Select(r => r.jsonPath).ToList();
            if (changed.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(ReportFolder);
                    string path = Path.Combine(ReportFolder, "batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
                    File.WriteAllText(path, JsonUtility.ToJson(new BatchManifest { reports = changed }, true));
                    EditorPrefs.SetString(LastReportPref, path);
                }
                catch { }
            }
            return list;
        }
        // ---- dry run ----------------------------------------------------------------------------------------------

        static void DryReport(Plan p, ConversionReport rep)
        {
            foreach (var s in p.sets)
            {
                if (s.outBase != null) rep.Add(ReportKind.Planned, "Texture", s.baseColor.src, s.outBase, s.mergeOpacity ? "base colour + opacity mask merged into alpha (cutout)" : "copy (sRGB)");
                if (s.outNormal != null) rep.Add(ReportKind.Planned, "Texture", s.normal.src, s.outNormal, s.convertNormal ? "normal map, green channel flipped (DirectX -> OpenGL)" : "normal map copy" + (s.normal.gl ? " (already OpenGL)" : ""));
                if (s.outMetalSmooth != null) rep.Add(ReportKind.Planned, "Texture", (s.packed ?? s.rough ?? s.metal).src, s.outMetalSmooth, s.packed != null ? "repacked: R=metallic, A=smoothness (1 - roughness)" : "roughness/metallic combined into R=metallic, A=smoothness");
                if (s.outAO != null) rep.Add(ReportKind.Planned, "Texture", (s.packed ?? s.ao).src, s.outAO, s.packAO ? "occlusion extracted from packed map (skipped if all white)" : "occlusion map copy");
                if (s.outEmissive != null) rep.Add(ReportKind.Planned, "Texture", s.emissive.src, s.outEmissive, "emissive copy (sRGB)");
                if (s.outHeight != null) rep.Add(ReportKind.Planned, "Texture", s.height.src, s.outHeight, "height copy (linear, parallax)");
            }
            foreach (var m in p.meshes)
                rep.Add(ReportKind.Planned, m.kind == MeshKind.AnimOnly ? "Animation" : (m.kind == MeshKind.Skeletal ? "Skeletal mesh" : "Mesh"), m.src, m.destAsset,
                    $"{m.kind}{(m.lod >= 0 ? ", LOD" + m.lod : "")}; scale: {m.scaleNote}{(m.hasUcx ? "; has UCX/UBX collision shells" : "")}{(m.hasAnim ? "; has animation takes" : "")}");
            foreach (var j in p.prefabs)
                rep.Add(ReportKind.Planned, "Prefab", null, j.prefabPath, $"{j.lods.Count} LOD level(s){(j.lods.Count > 1 ? " -> LODGroup" : "")}{(p.o.generateColliders ? ", colliders" : "")}");
            if (p.audio.Count <= 200) foreach (var a in p.audio) rep.Add(ReportKind.Planned, "Audio", a.src, a.dest, "copy; load type chosen from clip length (short/UI decompress on load, long streamed)");
            else foreach (var g in p.audio.GroupBy(a => Path.GetDirectoryName(a.dest).Replace('\\', '/'))) rep.Add(ReportKind.Planned, "Audio", null, g.Key, g.Count() + " clips");
            if (p.pendingGltf.Count > 0) rep.Add(ReportKind.Warning, "glTF meshes", null, null, GltfMessage(p.pendingGltf.Count));
            rep.Add(ReportKind.Info, "Materials", null, null, "URP Lit materials are matched to texture sets after the meshes are imported (slot names come from the imported FBX/OBJ), so they are not listed in a dry run.");
        }

        // ---- execution --------------------------------------------------------------------------------------------

        sealed class Ctx
        {
            public Plan p; public ConversionReport rep; public List<string> texImports = new List<string>(), otherImports = new List<string>(); public bool cancelled;
        }

        static bool CanWrite(Ctx c, string destAsset, string src, string cat, out bool existed)
        {
            existed = File.Exists(Disk(destAsset));
            if (existed && !c.p.o.overwrite)
            {
                c.rep.Add(ReportKind.Skipped, cat, src, destAsset, "already exists (enable Overwrite to replace)");
                return false;
            }
            EnsureFolder(Path.GetDirectoryName(destAsset));
            return true;
        }

        static void Wrote(Ctx c, string destAsset, bool existed, bool texture)
        {
            (existed ? c.rep.overwritten : c.rep.created).Add(destAsset);
            (texture ? c.texImports : c.otherImports).Add(destAsset);
        }

        static void Execute(Plan p, ConversionReport rep)
        {
            var o = p.o;
            var c = new Ctx { p = p, rep = rep };
            EnsureFolder(p.dest);
            int total = p.sets.Count + p.meshes.Count + p.audio.Count + p.aux.Count + 1, step = 0;

            foreach (var s in p.sets)
            {
                if (!Prog("Textures", s.key, step++ / (float)total)) { c.cancelled = true; break; }
                try { WriteSet(c, s); }
                catch (Exception e) { rep.Add(ReportKind.Warning, "Texture", s.all.FirstOrDefault()?.src, null, "failed: " + e.Message); }
            }

            if (!c.cancelled)
                foreach (var a in p.aux)
                {
                    if (CanWrite(c, a.dest, a.src, "OBJ material library", out bool ex)) { File.Copy(a.src, Disk(a.dest), true); (ex ? rep.overwritten : rep.created).Add(a.dest); c.otherImports.Add(a.dest); rep.Add(ReportKind.Converted, "OBJ material library", a.src, a.dest, "copied"); }
                }

            var written = new List<MeshEntry>();
            if (!c.cancelled)
                foreach (var m in p.meshes)
                {
                    if (!Prog("Meshes", m.stem, step++ / (float)total)) { c.cancelled = true; break; }
                    if (!CanWrite(c, m.destAsset, m.src, "Mesh", out bool ex)) continue;
                    File.Copy(m.src, Disk(m.destAsset), true);
                    if (m.IsGltf && m.ext == ".gltf")
                    {
                        string bin = Path.ChangeExtension(m.src, ".bin");
                        if (File.Exists(bin)) { string bd = Path.ChangeExtension(m.destAsset, ".bin"); File.Copy(bin, Disk(bd), true); Wrote(c, bd, false, false); }
                    }
                    bool human = o.rig == RigMode.Humanoid || (o.rig == RigMode.Auto && (HumanoidRx.IsMatch(m.stem) || HumanoidRx.IsMatch(m.relDir)));
                    Registry.Models[Registry.N(m.destAsset)] = new ModelJob
                    {
                        kind = m.kind, isObj = m.IsObj, hasAnim = m.hasAnim, useFileScale = m.useFileScale, globalScale = m.scale, humanoid = human && m.kind != MeshKind.Static, stem = m.stem
                    };
                    Wrote(c, m.destAsset, ex, false);
                    written.Add(m);
                    string cat = m.kind == MeshKind.AnimOnly ? "Animation" : (m.kind == MeshKind.Skeletal ? "Skeletal mesh" : "Mesh");
                    rep.Add(ReportKind.Converted, cat, m.src, m.destAsset, $"scale: {m.scaleNote}{(m.lod >= 0 ? "; LOD" + m.lod : "")}");
                }

            if (!c.cancelled)
            {
                int skippedAudio = 0, n = 0, audioCount = 0; long audioBytes = 0; var failedAudio = new List<string>(); var perDir = new Dictionary<string, int>();
                foreach (var a in p.audio)
                {
                    if ((n++ % 25) == 0 && !Prog("Audio", Path.GetFileName(a.src), step / (float)total)) { c.cancelled = true; break; }
                    string disk = Disk(a.dest); bool exA = File.Exists(disk);
                    if (exA && !o.overwrite) { skippedAudio++; continue; }
                    try { EnsureFolder(Path.GetDirectoryName(a.dest).Replace('\\', '/')); File.Copy(a.src, disk, true); }
                    catch (Exception e) { failedAudio.Add(a.src + ": " + e.Message); continue; }
                    audioBytes += new FileInfo(disk).Length; audioCount++;
                    var job = AudioProbe.Classify(a.dest, AudioProbe.Duration(a.src));
                    Registry.Audio[Registry.N(a.dest)] = job;
                    Wrote(c, a.dest, exA, false);
                    string dir = Path.GetDirectoryName(a.dest).Replace('\\', '/'); perDir.TryGetValue(dir, out int cnt); perDir[dir] = cnt + 1;
                    if (p.audio.Count <= 200) rep.Add(ReportKind.Converted, "Audio", a.src, a.dest, $"{job.seconds:0.0#} s, load type {job.load}");
                }
                step += p.audio.Count;
                if (p.audio.Count > 200) foreach (var kv in perDir) rep.Add(ReportKind.Converted, "Audio", null, kv.Key, kv.Value + " clips (short/UI clips decompress on load, medium compressed in memory, 20 s+ streamed)");
                if (skippedAudio > 0) rep.Add(ReportKind.Skipped, "Audio", null, null, skippedAudio + " clips already exist (enable Overwrite to replace)");
                foreach (var fa in failedAudio.Take(30)) rep.Add(ReportKind.Warning, "Audio", null, null, "copy failed: " + fa);
                if (audioCount > 0 || failedAudio.Count > 0) rep.Add(ReportKind.Info, "Audio totals", null, p.dest, $"{audioCount} clips written, {audioBytes / 1048576.0:0.0} MB, {failedAudio.Count} copy failures");
            }
            // batched import (textures first so materials can reference them)
            var imports = c.texImports.Concat(c.otherImports).Distinct().ToList();
            const int chunk = 24;
            for (int i = 0; i < imports.Count; i += chunk)
            {
                if (!Prog("Importing", $"{i}/{imports.Count}", 0.5f + 0.3f * i / Math.Max(1, imports.Count))) { c.cancelled = true; break; }
                AssetDatabase.StartAssetEditing();
                try { for (int k = i; k < Math.Min(imports.Count, i + chunk); k++) AssetDatabase.ImportAsset(imports[k], ImportAssetOptions.ForceUpdate); }
                finally { AssetDatabase.StopAssetEditing(); }
            }

            if (AssetDatabase.IsValidFolder(p.TmpFolder)) AssetDatabase.DeleteAsset(p.TmpFolder);
            if (!c.cancelled && p.audio.Count > 0)
            {
                int bad = 0, checkedN = 0; var badList = new List<string>();
                foreach (var a in p.audio)
                {
                    if (!Registry.Audio.ContainsKey(Registry.N(a.dest))) continue;
                    if ((checkedN++ % 200) == 0 && !Prog("Verifying clips", $"{checkedN}", 0.8f)) break;
                    if (AssetDatabase.LoadAssetAtPath<AudioClip>(a.dest) == null) { bad++; if (badList.Count < 30) badList.Add(a.dest); }
                }
                foreach (var b in badList) rep.Add(ReportKind.Warning, "Audio import", null, b, "Unity could not load this clip after import");
                if (bad > 0) rep.Add(ReportKind.Warning, "Audio import", null, null, bad + " of " + checkedN + " clips failed to import");
                else rep.Add(ReportKind.Info, "Audio import", null, null, "all " + checkedN + " clips loaded as AudioClip after import");
            }
            rep.cancelled = c.cancelled;
            if (c.cancelled) { rep.Add(ReportKind.Warning, "Cancelled", null, null, "Cancelled by user; textures/meshes written so far were imported, materials and prefabs were not built."); return; }

            Prog("Animations", "rigs, avatars, loops", 0.82f);
            FixAnimations(c, written);
            if (o.makePrefabs) BuildPrefabs(c);

            if (p.pendingGltf.Count > 0) rep.Add(ReportKind.Warning, "glTF meshes", null, null, GltfMessage(p.pendingGltf.Count));
            if (p.stagingRoot != null)
            {
                if (o.keepStaging || p.pendingGltf.Count > 0) rep.Add(ReportKind.Info, "UModel", null, p.stagingRoot, "extraction kept (reused on the next run of this pack while 'Reuse staging' is on)");
                else try { Directory.Delete(p.stagingRoot, true); } catch (Exception e) { rep.Add(ReportKind.Warning, "UModel", null, p.stagingRoot, "could not delete staging: " + e.Message); }
            }
        }

        static string GltfMessage(int n) =>
            n + " mesh(es) were exported by UModel as glTF/GLB, but Unity cannot import glTF without a glTF package. In Window > Package Manager choose + > Add package by name... > com.unity.cloud.gltfast (Unity's glTFast), then run this pack again; the extraction is kept and reused, so UModel does not run twice. Textures and sounds were converted now.";

        // ---- textures ---------------------------------------------------------------------------------------------

        static void WriteSet(Ctx c, SetPlan s)
        {
            var p = c.p; var o = p.o; var rep = c.rep; string tmp = p.TmpFolder;
            var cache = new Dictionary<TexEntry, Img>();
            Img Load(TexEntry e)
            {
                if (e == null) return null;
                if (cache.TryGetValue(e, out var im)) return im;
                im = ImageIO.Read(e.src, tmp, out string err);
                if (im == null) rep.Add(ReportKind.Warning, "Texture", e.src, null, "could not read: " + err);
                cache[e] = im; return im;
            }

            if (s.baseColor != null && s.outBase != null)
            {
                bool merged = false;
                if (CanWrite(c, s.outBase, s.baseColor.src, "Texture", out bool ex))
                {
                    if (s.mergeOpacity)
                    {
                        var b = Load(s.baseColor); var m = Load(s.opacity);
                        if (b != null && m != null)
                        {
                            ImageIO.SetAlphaFromGray(b, m);
                            File.WriteAllBytes(Disk(s.outBase), ImageIO.EncodePng(b, true));
                            merged = true; s.baseAlpha = true; s.cutout = o.cutoutFoliage; s.alphaKnown = true;
                            rep.Add(ReportKind.Converted, "Texture", s.baseColor.src, s.outBase, "base colour + opacity mask (" + Path.GetFileName(s.opacity.src) + ") merged into alpha");
                        }
                    }
                    if (!merged)
                    {
                        if (s.mergeOpacity)
                        {
                            s.outBase = Path.ChangeExtension(s.outBase, s.baseColor.ext).Replace('\\', '/');
                            s.mergeOpacity = false;
                            rep.Add(ReportKind.Warning, "Texture", s.opacity.src, null, "opacity mask could not be merged; base colour copied without alpha.");
                            if (!CanWrite(c, s.outBase, s.baseColor.src, "Texture", out ex)) goto afterBase;
                        }
                        File.Copy(s.baseColor.src, Disk(s.outBase), true);
                        if (o.cutoutFoliage && (s.baseColor.ext == ".png" || s.baseColor.ext == ".tga"))
                        {
                            var b = Load(s.baseColor);
                            if (b != null) { s.baseAlpha = b.hasAlpha; s.cutout = b.hasAlpha; }
                            s.alphaKnown = true;
                        }
                        rep.Add(ReportKind.Converted, "Texture", s.baseColor.src, s.outBase, "base colour copied (sRGB)" + (s.cutout ? ", alpha detected -> cutout" : ""));
                    }
                    Registry.Tex[Registry.N(s.outBase)] = new TexJob { role = TexRole.BaseColor, alpha = s.baseAlpha, cutout = s.cutout };
                    Wrote(c, s.outBase, ex, true);
                }
            }
        afterBase:

            if (s.normal != null && s.outNormal != null)
            {
                Img nImg = s.convertNormal ? Load(s.normal) : null;
                if (s.convertNormal && nImg == null) { s.convertNormal = false; s.outNormal = Path.ChangeExtension(s.outNormal, s.normal.ext).Replace('\\', '/'); }
                if (CanWrite(c, s.outNormal, s.normal.src, "Texture", out bool exN))
                {
                    if (s.convertNormal)
                    {
                        ImageIO.FlipGreen(nImg);
                        File.WriteAllBytes(Disk(s.outNormal), ImageIO.EncodePng(nImg, nImg.hasAlpha));
                        rep.Add(ReportKind.Converted, "Texture", s.normal.src, s.outNormal, "normal map, green channel flipped (UE DirectX -> Unity OpenGL)");
                    }
                    else
                    {
                        File.Copy(s.normal.src, Disk(s.outNormal), true);
                        rep.Add(ReportKind.Converted, "Texture", s.normal.src, s.outNormal, "normal map copied" + (s.normal.gl ? " (already OpenGL)" : " (green flip disabled or unreadable)"));
                    }
                    Registry.Tex[Registry.N(s.outNormal)] = new TexJob { role = TexRole.Normal };
                    Wrote(c, s.outNormal, exN, true);
                }
            }

            if (s.packMS && s.outMetalSmooth != null && CanWrite(c, s.outMetalSmooth, (s.packed ?? s.rough ?? s.metal).src, "Texture", out bool exM))
            {
                var pk = Load(s.packed);
                var rg = s.packed == null ? Load(s.rough) : null;
                var mt = s.packed == null ? Load(s.metal) : null;
                if (pk != null || rg != null || mt != null)
                {
                    var ms = ImageIO.PackMetalSmooth(pk, s.packed?.layout, rg, s.rough != null && s.rough.invert, mt);
                    File.WriteAllBytes(Disk(s.outMetalSmooth), ImageIO.EncodePng(ms, true));
                    Registry.Tex[Registry.N(s.outMetalSmooth)] = new TexJob { role = TexRole.Packed, alpha = true, metalSmooth = true };
                    Wrote(c, s.outMetalSmooth, exM, true);
                    rep.Add(ReportKind.Converted, "Texture", (s.packed ?? s.rough ?? s.metal).src, s.outMetalSmooth,
                        s.packed != null ? "packed map repacked: R=metallic, A=smoothness (1 - roughness)" : "roughness/metallic combined: R=metallic, A=smoothness");
                }
                else s.outMetalSmooth = null;
            }
            else if (!s.packMS) s.outMetalSmooth = null;

            if (s.outAO != null)
            {
                if (s.packAO)
                {
                    if (CanWrite(c, s.outAO, s.packed.src, "Texture", out bool exA))
                    {
                        var pk = Load(s.packed);
                        var ao = pk == null ? null : ImageIO.PackAO(pk, s.packed.layout[0]);
                        if (ao != null)
                        {
                            File.WriteAllBytes(Disk(s.outAO), ImageIO.EncodePng(ao, false));
                            Registry.Tex[Registry.N(s.outAO)] = new TexJob { role = TexRole.AO };
                            Wrote(c, s.outAO, exA, true);
                            rep.Add(ReportKind.Converted, "Texture", s.packed.src, s.outAO, "occlusion channel extracted from packed map");
                        }
                        else { s.outAO = null; if (pk != null) rep.Add(ReportKind.Info, "Texture", s.packed.src, null, "packed occlusion channel is all white; no occlusion map written"); }
                    }
                }
                else if (CanWrite(c, s.outAO, s.ao.src, "Texture", out bool exA2))
                {
                    File.Copy(s.ao.src, Disk(s.outAO), true);
                    Registry.Tex[Registry.N(s.outAO)] = new TexJob { role = TexRole.AO };
                    Wrote(c, s.outAO, exA2, true);
                    rep.Add(ReportKind.Converted, "Texture", s.ao.src, s.outAO, "occlusion map copied (linear)");
                }
            }

            CopyTex(c, s.emissive, s.outEmissive, TexRole.Emissive, "emissive map copied (sRGB)");
            CopyTex(c, s.height, s.outHeight, TexRole.Height, "height map copied (linear)");

            foreach (var e in s.all.Where(x => x.role == TexRole.Unknown))
            {
                string dst = Planner.J(p.dest, "Textures", UE4Names.StripLead(e.relDir), e.stem + e.ext);
                if (CanWrite(c, dst, e.src, "Texture", out bool exU))
                {
                    File.Copy(e.src, Disk(dst), true); Wrote(c, dst, exU, true);
                    rep.Add(ReportKind.Converted, "Texture", e.src, dst, "copied with default import settings (unrecognised suffix)");
                }
            }
        }

        static void CopyTex(Ctx c, TexEntry e, string dst, TexRole role, string msg)
        {
            if (e == null || dst == null) return;
            if (!CanWrite(c, dst, e.src, "Texture", out bool ex)) return;
            File.Copy(e.src, Disk(dst), true);
            Registry.Tex[Registry.N(dst)] = new TexJob { role = role };
            Wrote(c, dst, ex, true);
            c.rep.Add(ReportKind.Converted, "Texture", e.src, dst, msg);
        }

        // ---- rigs / animation -------------------------------------------------------------------------------------

        static Avatar LoadAvatar(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();

        static void FixAnimations(Ctx c, List<MeshEntry> written)
        {
            var p = c.p; var rep = c.rep;
            var skeletons = p.meshes.Where(m => m.kind == MeshKind.Skeletal).ToList();
            var queue = new List<ModelImporter>();
            foreach (var m in written.Where(x => x.kind != MeshKind.Static && !x.IsObj))
            {
                var mi = AssetImporter.GetAtPath(m.destAsset) as ModelImporter;
                if (mi == null || !Registry.Models.TryGetValue(Registry.N(m.destAsset), out var job)) continue;
                bool changed = false;
                job.stage = 2;

                if (m.kind == MeshKind.AnimOnly)
                {
                    var sk = skeletons.FirstOrDefault(x => x.relDir == m.relDir) ?? (skeletons.Count == 1 ? skeletons[0] : null);
                    var av = sk != null ? LoadAvatar(sk.destAsset) : null;
                    if (av != null && av.isValid)
                    {
                        bool skHuman = Registry.Models.TryGetValue(Registry.N(sk.destAsset), out var sj) && sj.humanoid;
                        job.humanoid = skHuman;
                        mi.animationType = skHuman ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                        mi.sourceAvatar = av; mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        changed = true;
                        rep.Add(ReportKind.Info, "Animation", m.src, null, "uses the avatar of " + Path.GetFileName(sk.destAsset));
                    }
                    else rep.Add(ReportKind.Warning, "Animation", m.src, null, "no matching skeletal mesh found in the pack; the clips keep their own avatar. Assign a Source Avatar on the FBX if the rig differs.");
                }
                else if (job.humanoid)
                {
                    var av = LoadAvatar(m.destAsset);
                    if (av == null || !av.isValid || !av.isHuman)
                    {
                        job.humanoid = false; mi.animationType = ModelImporterAnimationType.Generic; changed = true;
                        rep.Add(ReportKind.Warning, "Rig", m.src, null, "Humanoid mapping failed for this skeleton; switched to Generic.");
                    }
                }

                if (m.hasAnim && p.o.importAnimations)
                {
                    var clips = AssetDatabase.LoadAllAssetsAtPath(m.destAsset).OfType<AnimationClip>().Where(a => !a.name.StartsWith("__preview__")).ToList();
                    var ca = mi.clipAnimations;
                    if (ca == null || ca.Length == 0) ca = mi.defaultClipAnimations;
                    for (int i = 0; i < ca.Length; i++)
                    {
                        var clip = clips.FirstOrDefault(x => x.name == ca[i].name);
                        bool looks = clip != null && LooksLooping(clip);
                        if (looks && !ca[i].loopTime) { ca[i].loopTime = true; ca[i].loopPose = true; changed = true; }
                        rep.Add(ReportKind.Converted, "Animation clip", m.src, m.destAsset + "#" + ca[i].name,
                            $"{(clip != null ? clip.length.ToString("0.00") + "s" : "?")}, loop={(ca[i].loopTime ? "yes" : "no")}{(looks ? " (detected from matching first/last pose)" : "")}");
                    }
                    if (changed) mi.clipAnimations = ca;
                }
                if (changed) queue.Add(mi);
            }
            if (queue.Count == 0) return;
            AssetDatabase.StartAssetEditing();
            try { foreach (var mi in queue) mi.SaveAndReimport(); }
            finally { AssetDatabase.StopAssetEditing(); }
        }

        static bool LooksLooping(AnimationClip clip)
        {
            if (clip.length < 0.05f) return false;
            int ok = 0, tot = 0;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length < 2) continue;
                float a = curve.Evaluate(0f), z = curve.Evaluate(clip.length), mn = float.MaxValue, mx = float.MinValue;
                foreach (var k in curve.keys) { mn = Mathf.Min(mn, k.value); mx = Mathf.Max(mx, k.value); }
                tot++;
                if (Mathf.Abs(a - z) <= Mathf.Max(0.02f * (mx - mn), 0.001f)) ok++;
            }
            return tot > 0 && ok >= tot * 0.9f;
        }

        // ---- materials + prefabs ----------------------------------------------------------------------------------

        sealed class MatCtx
        {
            public Dictionary<string, Material> cache = new Dictionary<string, Material>();
            public Dictionary<string, string> pathOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<Material> toSave = new List<Material>();
        }

        static void BuildPrefabs(Ctx c)
        {
            var p = c.p; var rep = c.rep;
            foreach (var j in p.prefabs) { EnsureFolder(Path.GetDirectoryName(j.prefabPath)); }
            EnsureFolder(Planner.J(p.dest, "Materials"));
            var activeBefore = SceneManager.GetActiveScene();
            bool dirtyBefore = activeBefore.isDirty;
            var sc = EditorSceneManager.NewPreviewScene();
            var mc = new MatCtx();
            int n = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var j in p.prefabs)
                {
                    if (!Prog("Prefabs", j.name, 0.85f + 0.15f * n++ / Math.Max(1, p.prefabs.Count))) { rep.cancelled = true; rep.Add(ReportKind.Warning, "Cancelled", null, null, "Cancelled while building prefabs; the rest were not built."); break; }
                    try { BuildPrefab(c, mc, j, sc); }
                    catch (Exception e) { rep.Add(ReportKind.Warning, "Prefab", j.lods[0].src, j.prefabPath, "failed: " + e.Message); Debug.LogException(e); }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorSceneManager.ClosePreviewScene(sc);
            }
            foreach (var m in mc.toSave) if (m != null) AssetDatabase.SaveAssetIfDirty(m);
            if (SceneManager.GetActiveScene().isDirty && !dirtyBefore)
                Debug.LogWarning("[UE4 Converter] the active scene reports as modified; it was NOT saved. Check it before saving.");
        }

        static void BuildPrefab(Ctx c, MatCtx mc, PrefabJob job, Scene sc)
        {
            var p = c.p; var o = p.o; var rep = c.rep;
            if (File.Exists(Disk(job.prefabPath)) && !o.overwrite) { rep.Add(ReportKind.Skipped, "Prefab", null, job.prefabPath, "already exists (enable Overwrite to replace)"); return; }
            bool existed = File.Exists(Disk(job.prefabPath));

            var insts = new List<GameObject>(); var instMeshes = new List<MeshEntry>();
            foreach (var m in job.lods)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(m.destAsset);
                if (asset == null) { rep.Add(ReportKind.Warning, "Prefab", m.src, null, "mesh asset " + m.destAsset + " is not available (import failed?); skipped"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, sc);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                insts.Add(go); instMeshes.Add(m);
                if (m.IsGltf)
                {
                    float f = m.scale;
                    if (o.scaleMode == ConvScaleMode.Auto)
                    {
                        var rs = go.GetComponentsInChildren<Renderer>(true);
                        if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); if (Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z)) > 100f) { f = 0.01f; rep.Add(ReportKind.Info, "Scale", m.src, null, "glTF larger than 100 m, treated as UE centimetres (x0.01)"); } }
                    }
                    if (f != 1f) go.transform.localScale *= f;
                }
            }
            if (insts.Count == 0) return;

            GameObject root = null;
            try
            {
                bool objZ = job.lods[0].IsObj && o.objIsZUp;
                bool wrap = insts.Count > 1 || objZ || o.extraYaw != 0;
                if (wrap)
                {
                    root = EditorUtility.CreateGameObjectWithHideFlags(job.name, HideFlags.HideAndDontSave);
                    SceneManager.MoveGameObjectToScene(root, sc);
                    root.hideFlags = HideFlags.None;
                    var rot = Quaternion.Euler(0, o.extraYaw, 0) * (objZ ? Quaternion.Euler(-90, 0, 0) : Quaternion.identity);
                    for (int i = 0; i < insts.Count; i++)
                    {
                        insts[i].name = insts.Count > 1 ? "LOD" + i : job.name;
                        insts[i].transform.SetParent(root.transform, false);
                        insts[i].transform.localRotation = rot * insts[i].transform.localRotation;
                    }
                }
                else { root = insts[0]; root.name = job.name; }

                // UE collision shells (UCX_/UBX_/USP_/UCP_ nodes): LOD0's become convex colliders, the rest are removed
                int ucx = 0;
                for (int i = 0; i < insts.Count; i++)
                    foreach (var tr in insts[i].GetComponentsInChildren<Transform>(true).Where(t => UcxRx.IsMatch(t.name)).ToArray())
                    {
                        if (tr == null) continue;
                        var g = tr.gameObject; var mf = g.GetComponent<MeshFilter>();
                        if (i == 0 && o.generateColliders && mf != null && mf.sharedMesh != null)
                        {
                            var col = g.AddComponent<MeshCollider>(); col.sharedMesh = mf.sharedMesh; col.convex = true;
                            var mr = g.GetComponent<MeshRenderer>(); if (mr != null) UnityEngine.Object.DestroyImmediate(mr);
                            UnityEngine.Object.DestroyImmediate(mf); ucx++;
                        }
                        else UnityEngine.Object.DestroyImmediate(g);
                    }

                // materials
                int missing = 0;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    var src = r.sharedMaterials; var dst = new Material[src.Length];
                    for (int i = 0; i < src.Length; i++)
                    {
                        dst[i] = GetMaterial(c, mc, job, src[i], out bool matched);
                        if (!matched) missing++;
                    }
                    r.sharedMaterials = dst;
                }

                // collider from the visible mesh when the FBX had no UCX shells
                int cols = ucx;
                if (o.generateColliders && ucx == 0 && job.lods[0].kind == MeshKind.Static)
                    foreach (var mf in insts[0].GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null) { mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; cols++; }

                if (insts.Count > 1)
                {
                    var lg = root.AddComponent<LODGroup>();
                    var lods = new LOD[insts.Count];
                    for (int i = 0; i < insts.Count; i++)
                    {
                        float h = i == insts.Count - 1 ? 0.02f : 0.5f * Mathf.Pow(0.4f, i);
                        lods[i] = new LOD(h, insts[i].GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray());
                    }
                    lg.SetLODs(lods); lg.RecalculateBounds();
                }

                // size sanity check
                var rends = root.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
                string size = "";
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds; foreach (var r in rends.Skip(1)) b.Encapsulate(r.bounds);
                    size = $"{b.size.x:0.##} x {b.size.y:0.##} x {b.size.z:0.##} m";
                    float mx = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (mx > 300f || mx < 0.05f)
                        rep.Add(ReportKind.Warning, "Scale", job.lods[0].src, job.prefabPath, $"prefab is {size}, which looks wrong for a prop. Re-run with Scale = UE cm (x0.01) or None.");
                }

                PrefabUtility.SaveAsPrefabAsset(root, job.prefabPath);
                (existed ? rep.overwritten : rep.created).Add(job.prefabPath);
                rep.Add(ReportKind.Converted, "Prefab", job.lods[0].src, job.prefabPath,
                    $"{insts.Count} LOD level(s){(insts.Count > 1 ? " (LODGroup)" : "")}, colliders: {cols}{(ucx > 0 ? " (from UCX shells)" : "")}, size {size}{(missing > 0 ? ", " + missing + " slot(s) without textures" : "")}");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static string SafeName(string n) => Regex.Replace(n, @"[^A-Za-z0-9_\-\.]", "_");

        static Material GetMaterial(Ctx c, MatCtx mc, PrefabJob job, Material srcMat, out bool matched)
        {
            var p = c.p; var o = p.o; var rep = c.rep;
            string name = srcMat != null ? srcMat.name : job.name;
            if (name.EndsWith(" (Instance)")) name = name.Substring(0, name.Length - 11);
            var set = Planner.Resolve(p, name, job.lods[0], out string how);
            matched = set != null;
            string key = name.ToLowerInvariant() + "|" + (set != null ? set.relDir.ToLowerInvariant() + "/" + set.normKey : "none");
            if (mc.cache.TryGetValue(key, out var cached)) return cached;

            string path = Planner.J(p.dest, "Materials", SafeName(name) + ".mat");
            for (int i = 2; mc.pathOwner.TryGetValue(path, out var owner) && owner != key; i++) path = Planner.J(p.dest, "Materials", SafeName(name) + "_" + i + ".mat");
            mc.pathOwner[path] = key;

            bool existed = File.Exists(Disk(path));
            if (existed && !o.overwrite)
            {
                var ex = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (ex != null) { mc.cache[key] = ex; rep.Add(ReportKind.Skipped, "Material", null, path, "already exists (enable Overwrite to replace)"); return ex; }
            }

            Texture2D T(string ap) => string.IsNullOrEmpty(ap) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(ap);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            var colour = Color.white;
            if (srcMat != null && set == null)
            {
                if (srcMat.HasProperty("_BaseColor")) colour = srcMat.GetColor("_BaseColor");
                else if (srcMat.HasProperty("_Color")) colour = srcMat.GetColor("_Color");
                colour.a = 1f;
                if (colour == Color.white) colour = new Color(0.6f, 0.6f, 0.6f, 1f);
            }
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", 0.25f); m.SetFloat("_Metallic", 0f);
            var notes = new List<string>();
            if (set != null)
            {
                var bt = T(set.outBase); if (bt != null) { m.SetTexture("_BaseMap", bt); notes.Add("base"); }
                var nt = T(set.outNormal); if (nt != null) { m.SetTexture("_BumpMap", nt); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); notes.Add("normal"); }
                var mt = T(set.outMetalSmooth); if (mt != null) { m.SetTexture("_MetallicGlossMap", mt); m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); notes.Add("metallic/smoothness"); }
                var at = T(set.outAO); if (at != null) { m.SetTexture("_OcclusionMap", at); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP"); notes.Add("occlusion"); }
                var et = T(set.outEmissive); if (et != null) { m.SetTexture("_EmissionMap", et); m.SetColor("_EmissionColor", Color.white); m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; notes.Add("emissive"); }
                var ht = T(set.outHeight); if (ht != null) { m.SetTexture("_ParallaxMap", ht); m.SetFloat("_Parallax", 0.02f); m.EnableKeyword("_PARALLAXMAP"); notes.Add("height"); }

                bool cut = set.cutout;
                if (!set.alphaKnown && o.cutoutFoliage && set.outBase != null)
                {
                    var ti = AssetImporter.GetAtPath(set.outBase) as TextureImporter;
                    cut = ti != null && ti.DoesSourceTextureHaveAlpha() && ti.alphaIsTransparency && UE4Names.FoliageRx.IsMatch(name + " " + set.key);
                }
                if (cut)
                {
                    m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                    m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = (int)RenderQueue.AlphaTest;
                    m.SetFloat("_Cull", 0f);
                    notes.Add("alpha-cutout, double sided");
                }
            }

            if (existed)
            {
                var ex = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (ex != null) { EditorUtility.CopySerialized(m, ex); EditorUtility.SetDirty(ex); mc.toSave.Add(ex); UnityEngine.Object.DestroyImmediate(m); m = ex; rep.overwritten.Add(path); }
                else { AssetDatabase.CreateAsset(m, path); rep.created.Add(path); }
            }
            else { AssetDatabase.CreateAsset(m, path); rep.created.Add(path); }
            mc.cache[key] = m;

            if (set != null) rep.Add(ReportKind.Converted, "Material", set.baseColor?.src ?? set.all[0].src, path, $"URP Lit from texture set '{set.key}' (matched by {how}): {string.Join(", ", notes)}");
            else rep.Add(ReportKind.Warning, "Material", null, path, $"no texture set matched material '{name}' on {job.name}; created a plain URP Lit (assign textures by hand).");
            return m;
        }

        // ---- revert -----------------------------------------------------------------------------------------------

        public static string Revert(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) return "No previous conversion report found.";
            if (Path.GetFileName(jsonPath).StartsWith("batch_"))
            {
                var bm = JsonUtility.FromJson<BatchManifest>(File.ReadAllText(jsonPath));
                return "Batch revert: " + string.Join(" | ", bm.reports.Select(r => Revert(r)));
            }
            var rep = JsonUtility.FromJson<ConversionReport>(File.ReadAllText(jsonPath));
            int del = 0;
            AssetDatabase.StartAssetEditing();
            try { foreach (var a in Enumerable.Reverse(rep.created)) if (a.StartsWith("Assets/") && AssetDatabase.DeleteAsset(a)) del++; }
            finally { AssetDatabase.StopAssetEditing(); }
            RemoveEmptyDirs(rep.destFolder);
            AssetDatabase.Refresh();
            return $"Reverted: deleted {del} asset(s) created by '{rep.pack}'. {rep.overwritten.Count} overwritten asset(s) were not restored.";
        }

        static void RemoveEmptyDirs(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder)) return;
            foreach (var sub in AssetDatabase.GetSubFolders(assetFolder)) RemoveEmptyDirs(sub);
            string disk = Disk(assetFolder);
            if (Directory.Exists(disk) && Directory.GetFileSystemEntries(disk).All(e => e.EndsWith(".meta"))) AssetDatabase.DeleteAsset(assetFolder);
        }
    }
}