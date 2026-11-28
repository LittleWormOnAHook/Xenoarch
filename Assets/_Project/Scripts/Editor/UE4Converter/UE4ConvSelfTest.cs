using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>
    /// Synthetic end-to-end test: generates OBJ cubes (LOD0/LOD1), PNG/TGA texture sets and fake .uasset files in the OS temp folder,
    /// converts them into a scratch folder under Assets, asserts on the results, then deletes everything it created.
    /// Run from the menu or by creating Temp/xenoarch_ue4conv_test.trigger. Result: Logs/xenoarch_ue4conv_test.txt.
    /// FBX is not covered (no way to generate one here).
    /// </summary>
    [InitializeOnLoad]
    public static class UE4ConverterSelfTest
    {
        const string Trigger = "Temp/xenoarch_ue4conv_test.trigger";
        const string LogPath = "Logs/xenoarch_ue4conv_test.txt";
        const string DestRoot = "Assets/_Project/UE4ConvTest";
        const string UmDest = "Assets/_Project/UE4ConvTestUm";
        const string UmDest2 = "Assets/_Project/UE4ConvTestUm2";
        static double _next;

        static UE4ConverterSelfTest() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(Trigger)) { File.Delete(Trigger); Run(); }
        }

        static readonly StringBuilder Log = new StringBuilder();
        static int _fail;

        static void Check(bool ok, string what, string detail = "")
        {
            if (!ok) _fail++;
            Log.AppendLine((ok ? "PASS " : "FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
        }

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity Converter Self-Test")]
        public static void Run()
        {
            Log.Clear(); _fail = 0;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[UE4 Converter] self-test refused in Play mode"); return; }
            string tmp = Path.Combine(Path.GetTempPath(), "xenoarch_ue4conv_test");
            bool dirtyBefore = SceneManager.GetActiveScene().isDirty;
            ConversionReport real = null;
            try
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(Path.Combine(tmp, "Meshes")); Directory.CreateDirectory(Path.Combine(tmp, "Textures")); Directory.CreateDirectory(Path.Combine(tmp, "Content"));
                Generate(tmp);
                NameChecks();
                UModelChecks(tmp);
            PskChecks(tmp);

                var o = new ConverterOptions { sourceFolder = tmp, destRoot = "Assets/_Project", packName = "UE4ConvTest", overwrite = true, dryRun = true, useUModel = false };
                // pack name is also the destination folder name: Assets/_Project/UE4ConvTest
                Check(o.DestFolder == DestRoot, "destination path", o.DestFolder);

                var dry = UE4Converter.Run(o);
                Check(!AssetDatabase.IsValidFolder(DestRoot), "dry run writes nothing under Assets");
                Check(dry.Count(ReportKind.Planned) > 10, "dry run lists planned work", dry.Count(ReportKind.Planned) + " planned");
                Check(dry.Count(ReportKind.Unconvertible) == 3, ".uasset/.umap files reported as unconvertible", dry.Count(ReportKind.Unconvertible) + " found");
                Check(dry.items.Any(i => i.category.Contains("Blueprint")) && dry.items.Any(i => i.category.Contains("Landscape")) && dry.items.Any(i => i.category.Contains("Material graph")), "unconvertible items are categorised (Blueprint, Landscape, Material graph)");

                o.dryRun = false;
                real = UE4Converter.Run(o);
                Log.AppendLine("--- report summary ---\n" + real.Summary());

                Check(real.items.All(i => i.category != "Exception"), "no exception during conversion");
                Check(File.Exists(real.mdPath) && File.Exists(real.jsonPath), "markdown + JSON report written");

                // textures
                var nrm = ImageIO.Read(ImageIO.DiskPath(DestRoot + "/Textures/T_Test_N.png"), "", out _);
                Check(nrm != null && nrm.px[0].r == 128 && nrm.px[0].g == 55 && nrm.px[0].b == 255, "normal map green flipped (200 -> 55)", nrm == null ? "missing" : nrm.px[0].ToString());
                var ms = ImageIO.Read(ImageIO.DiskPath(DestRoot + "/Textures/Test_MetalSmooth.png"), "", out _);
                Check(ms != null && ms.px[0].r == 50 && ms.px[0].a == 155, "ORM -> metallic R=50, smoothness A=1-100/255 (155)", ms == null ? "missing" : ms.px[0].ToString());
                var ao = ImageIO.Read(ImageIO.DiskPath(DestRoot + "/Textures/Test_Occlusion.png"), "", out _);
                Check(ao != null && ao.px[0].r == 200, "ORM occlusion channel extracted (200)", ao == null ? "missing" : ao.px[0].ToString());
                var rm = ImageIO.Read(ImageIO.DiskPath(DestRoot + "/Textures/Rock_MetalSmooth.png"), "", out _);
                Check(rm != null && rm.px[0].r == 200 && rm.px[0].a == 191, "separate _R/_M combined (metal 200, smooth 255-64=191)", rm == null ? "missing" : rm.px[0].ToString());
                var leaf = ImageIO.Read(ImageIO.DiskPath(DestRoot + "/Textures/T_Leaf_D.png"), "", out _);
                if (leaf != null)
                {
                    var left = leaf.px[0]; var right = leaf.px[leaf.w - 1]; var bottomRow = leaf.px[0]; var topRow = leaf.px[(leaf.h - 1) * leaf.w + leaf.w - 1];
                    Check(left.a == 0 && right.a == 255, "_Mask merged into base colour alpha (left 0, right 255)", $"{left.a}/{right.a}");
                    Check(bottomRow.b == 255 && topRow.r == 255, "TGA top-left origin decoded upright (bottom row blue, top row red)", $"bottom {bottomRow}, top {topRow}");
                }
                else Check(false, "leaf base colour written");
                var ti = AssetImporter.GetAtPath(DestRoot + "/Textures/T_Test_N.png") as TextureImporter;
                Check(ti != null && ti.textureType == TextureImporterType.NormalMap, "normal map imported as NormalMap");
                var tm = AssetImporter.GetAtPath(DestRoot + "/Textures/Test_MetalSmooth.png") as TextureImporter;
                Check(tm != null && !tm.sRGBTexture, "metallic/smoothness map imported linear (sRGB off)");
                var tb = AssetImporter.GetAtPath(DestRoot + "/Textures/T_Test_D.png") as TextureImporter;
                Check(tb != null && tb.sRGBTexture, "base colour imported sRGB");

                // materials
                var mTest = AssetDatabase.LoadAssetAtPath<Material>(DestRoot + "/Materials/M_Test.mat");
                Check(mTest != null && mTest.shader.name == "Universal Render Pipeline/Lit", "M_Test is a URP Lit material");
                if (mTest != null)
                    Check(mTest.GetTexture("_BaseMap") != null && mTest.GetTexture("_BumpMap") != null && mTest.GetTexture("_MetallicGlossMap") != null && mTest.GetTexture("_OcclusionMap") != null && mTest.IsKeywordEnabled("_NORMALMAP"),
                        "M_Test has base/normal/metallic-smoothness/occlusion assigned");
                var mLeaf = AssetDatabase.LoadAssetAtPath<Material>(DestRoot + "/Materials/M_Leaf.mat");
                Check(mLeaf != null && mLeaf.GetFloat("_AlphaClip") == 1f && mLeaf.GetFloat("_Cull") == 0f, "M_Leaf is alpha-cutout + double sided");
                var mRock = AssetDatabase.LoadAssetAtPath<Material>(DestRoot + "/Materials/M_Rock.mat");
                Check(mRock != null && mRock.GetTexture("_MetallicGlossMap") != null, "M_Rock got the combined metallic/smoothness map");

                // prefabs
                var pTest = AssetDatabase.LoadAssetAtPath<GameObject>(DestRoot + "/Prefabs/SM_Test.prefab");
                Check(pTest != null, "SM_Test prefab exists");
                if (pTest != null)
                {
                    var lg = pTest.GetComponent<LODGroup>();
                    Check(lg != null && lg.lodCount == 2, "SM_Test has a LODGroup with 2 levels (_LOD0/_LOD1 merged)", lg == null ? "none" : lg.lodCount.ToString());
                    Check(pTest.GetComponentsInChildren<MeshCollider>(true).Length >= 1, "SM_Test has a MeshCollider");
                    var r = pTest.GetComponentInChildren<Renderer>(true);
                    Check(r != null && r.sharedMaterial == mTest, "SM_Test renderer uses M_Test");
                    var rends = pTest.GetComponentsInChildren<Renderer>(true);
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds; foreach (var x in rends) b.Encapsulate(x.bounds);
                        // LOD0 is a 100-unit cube (cm) and LOD1 a 50-unit one; union is 1 m
                        Check(Mathf.Abs(b.size.x - 1f) < 0.05f, "OBJ auto scale: 100-unit cube becomes ~1 m", b.size.ToString());
                    }
                }
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(DestRoot + "/Prefabs/SM_Leaf.prefab") != null, "SM_Leaf prefab exists");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(DestRoot + "/Prefabs/SM_Rock.prefab") != null, "SM_Rock prefab exists");

                // re-run without overwrite must skip
                o.overwrite = false;
                var again = UE4Converter.Run(o);
                Check(again.Count(ReportKind.Skipped) > 5 && again.created.Count == 0, "re-run without Overwrite skips existing assets and creates nothing", again.Count(ReportKind.Skipped) + " skipped");

                // revert removes what was created
                string msg = UE4Converter.Revert(real.jsonPath);
                Log.AppendLine("revert: " + msg);
                Check(!AssetDatabase.IsValidFolder(DestRoot) || AssetDatabase.FindAssets("", new[] { DestRoot }).Length == 0, "Revert last conversion removes the created assets");
                FakeUModelEndToEnd(tmp);
            }
            catch (Exception e) { Check(false, "test threw an exception", e.ToString()); }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (AssetDatabase.IsValidFolder(DestRoot)) AssetDatabase.DeleteAsset(DestRoot);
                if (AssetDatabase.IsValidFolder(UmDest)) AssetDatabase.DeleteAsset(UmDest);
                if (AssetDatabase.IsValidFolder(UmDest2)) AssetDatabase.DeleteAsset(UmDest2);
                try { var st = UModel.StagingDir("UE4ConvTestUm"); if (Directory.Exists(st)) Directory.Delete(st, true); } catch { }
                if (Directory.Exists(tmp)) try { Directory.Delete(tmp, true); } catch { }
                try
                {
                    string rd = UE4Converter.ReportFolder;
                    if (Directory.Exists(rd)) foreach (var f in Directory.GetFiles(rd, "UE4ConvTest*")) File.Delete(f);
                }
                catch { }
                AssetDatabase.Refresh();
                Check(SceneManager.GetActiveScene().isDirty == dirtyBefore, "active scene dirty flag unchanged", "before " + dirtyBefore + ", after " + SceneManager.GetActiveScene().isDirty);
                Check(!AssetDatabase.IsValidFolder(DestRoot) && !AssetDatabase.IsValidFolder(UmDest) && !AssetDatabase.IsValidFolder(UmDest2) && !Directory.Exists(tmp) && !Directory.Exists(UModel.StagingDir("UE4ConvTestUm")), "scratch folders cleaned up");
                Log.Insert(0, $"UE4 converter self-test {DateTime.Now:s}: {(_fail == 0 ? "ALL PASSED" : _fail + " FAILED")}\n");
                Directory.CreateDirectory("Logs");
                File.WriteAllText(LogPath, Log.ToString());
                Debug.Log("[UE4 Converter] self-test: " + (_fail == 0 ? "all passed" : _fail + " failed") + " -> " + LogPath);
            }
        }

        static void NameChecks()
        {
            var r = UE4Names.Classify("T_Wall_01_BaseColor", out var k, out var lay, out _, out _, out _);
            Check(r == TexRole.BaseColor && k == "Wall_01", "classify T_Wall_01_BaseColor", r + "/" + k);
            r = UE4Names.Classify("T_Wall_01_ORM", out k, out lay, out _, out _, out _);
            Check(r == TexRole.Packed && lay != null && lay[0] == 0 && lay[1] == 1 && lay[2] == 2 && k == "Wall_01", "classify _ORM (R=AO,G=rough,B=metal)");
            r = UE4Names.Classify("T_Wall_RMA", out k, out lay, out _, out _, out _);
            Check(r == TexRole.Packed && lay[0] == 2 && lay[1] == 0 && lay[2] == 1, "classify _RMA (R=rough,G=metal,B=AO)");
            r = UE4Names.Classify("Rock_Normal_GL", out k, out _, out _, out bool gl, out _);
            Check(r == TexRole.Normal && gl && k == "Rock", "classify Rock_Normal_GL as OpenGL normal");
            r = UE4Names.Classify("T_Pillar_Roughness_2k", out k, out _, out _, out _, out _);
            Check(r == TexRole.Roughness && k == "Pillar", "classify T_Pillar_Roughness_2k (resolution token skipped)");
            r = UE4Names.Classify("T_Pillar_Ambient_Occlusion", out k, out _, out _, out _, out _);
            Check(r == TexRole.AO && k == "Pillar", "classify two-word suffix Ambient_Occlusion");
            r = UE4Names.Classify("T_Moss_Mask", out k, out _, out _, out _, out _);
            Check(r == TexRole.Opacity, "classify _Mask as opacity");
            r = UE4Names.Classify("T_Lamp_E", out k, out _, out _, out _, out _);
            Check(r == TexRole.Emissive && k == "Lamp", "classify _E as emissive");
            r = UE4Names.Classify("T_Dune_H", out k, out _, out _, out _, out _);
            Check(r == TexRole.Height, "classify _H as height");
            Check(UE4Names.Norm("M_Wall_01") == UE4Names.Norm("T_Wall_01") && UE4Names.Norm("SM_Wall_01_LOD2") == "wall01" && UE4Names.Norm("MI_Wall_01_Inst") == "wall01", "material/mesh/texture names normalise to the same key");
            int lod; string b = UE4Names.StripLod("SM_Arch_LOD1", out lod);
            Check(b == "SM_Arch" && lod == 1, "StripLod SM_Arch_LOD1");
        }

        // ---- PSKX -> OBJ -------------------------------------------------------------------------------------

        static void Chunk(BinaryWriter w, string id, int size, int count, Action<BinaryWriter> body)
        {
            var name = new byte[20]; Encoding.ASCII.GetBytes(id).CopyTo(name, 0);
            w.Write(name); w.Write(0); w.Write(size); w.Write(count); body?.Invoke(w);
        }

        /// <summary>A 100 cm cube with deliberately INWARD winding, one material, one smoothing group per face.</summary>
        static byte[] CubePskx()
        {
            var pts = new float[][] { new[]{-50f,-50f,-50f}, new[]{50f,-50f,-50f}, new[]{50f,50f,-50f}, new[]{-50f,50f,-50f}, new[]{-50f,-50f,50f}, new[]{50f,-50f,50f}, new[]{50f,50f,50f}, new[]{-50f,50f,50f} };
            int[][] quads = { new[]{0,1,2,3}, new[]{4,5,6,7}, new[]{0,1,5,4}, new[]{1,2,6,5}, new[]{2,3,7,6}, new[]{3,0,4,7} };
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            Chunk(w, "ACTRHEAD", 0, 0, null);
            Chunk(w, "PNTS0000", 12, 8, b => { foreach (var p in pts) foreach (var v in p) b.Write(v); });
            var wedges = new List<int>(); var faces = new List<int[]>();
            foreach (var q0 in quads)
            {
                var q = (int[])q0.Clone();
                // make the quad outward under the standard cross product, then reverse it: the whole cube ends up consistently INWARD
                float[] A = pts[q[0]], B = pts[q[1]], C = pts[q[2]];
                float nx = (B[1]-A[1])*(C[2]-A[2]) - (B[2]-A[2])*(C[1]-A[1]), ny = (B[2]-A[2])*(C[0]-A[0]) - (B[0]-A[0])*(C[2]-A[2]), nz = (B[0]-A[0])*(C[1]-A[1]) - (B[1]-A[1])*(C[0]-A[0]);
                bool outward = nx * A[0] + ny * A[1] + nz * A[2] > 0;
                if (outward) Array.Reverse(q);
                int b0 = wedges.Count; foreach (int pi in q) wedges.Add(pi); faces.Add(new[] { b0, b0 + 1, b0 + 2 }); faces.Add(new[] { b0, b0 + 2, b0 + 3 }); }
            Chunk(w, "VTXW0000", 16, wedges.Count, b => { foreach (int pi in wedges) { b.Write((ushort)pi); b.Write((ushort)0); b.Write(0.25f); b.Write(0.75f); b.Write((byte)0); b.Write((byte)0); b.Write((ushort)0); } });
            Chunk(w, "FACE0000", 12, faces.Count, b => { int i = 0; foreach (var f in faces) { b.Write((ushort)f[0]); b.Write((ushort)f[1]); b.Write((ushort)f[2]); b.Write((byte)0); b.Write((byte)0); b.Write((uint)(1 << (i++ / 2))); } });
            Chunk(w, "MATT0000", 88, 1, b => { var n = new byte[64]; Encoding.ASCII.GetBytes("MI_Cube").CopyTo(n, 0); b.Write(n); b.Write(new byte[24]); });
            Chunk(w, "REFSKELT", 120, 0, null);
            Chunk(w, "RAWWEIGHTS", 12, 0, null);
            w.Flush(); return ms.ToArray();
        }

        static void PskChecks(string tmp)
        {
            string dir = Path.Combine(Path.GetTempPath(), "xenoarch_psk_test"); if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
            string psk = Path.Combine(dir, "SM_Cube.pskx"); File.WriteAllBytes(psk, CubePskx());
            var r = PskToObj.ConvertTree(dir);
            string obj = Path.Combine(dir, "SM_Cube.obj");
            Check(r.converted == 1 && r.failed == 0 && File.Exists(obj) && File.Exists(Path.Combine(dir, "SM_Cube.mtl")), "PSKX static mesh converts to OBJ + MTL", $"converted {r.converted}, failed {r.failed}");
            if (!File.Exists(obj)) return;
            var lines = File.ReadAllLines(obj); var ci = CultureInfo.InvariantCulture;
            Check(lines[0] == PskToObj.Marker, "OBJ carries the metres marker");
            var v = lines.Where(l => l.StartsWith("v ")).Select(l => l.Split(' ').Skip(1).Select(x => float.Parse(x, ci)).ToArray()).ToList();
            var faces = lines.Where(l => l.StartsWith("f ")).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(t => int.Parse(t.Split('/')[0]) - 1).ToArray()).ToList();
            Check(v.Count == 8 && faces.Count == 12, "cube has 8 points and 12 triangles", v.Count + " / " + faces.Count);
            float ext = v.Max(p => p[1]) - v.Min(p => p[1]);
            Check(Mathf.Abs(ext - 1f) < 0.001f && lines.Any(l => l == "usemtl MI_Cube"), "100 cm becomes 1 m and the PSK material name is used", ext.ToString(ci));
            // what Unity's OBJ importer will produce: x mirrored back, winding reversed back
            int outward = 0;
            foreach (var f in faces)
            {
                var a = new Vector3(-v[f[0]][0], v[f[0]][1], v[f[0]][2]); var b = new Vector3(-v[f[2]][0], v[f[2]][1], v[f[2]][2]); var c = new Vector3(-v[f[1]][0], v[f[1]][1], v[f[1]][2]);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) > 0) outward++;
            }
            Check(outward == 12, "inward-wound input is flipped so all faces point outward after Unity import", outward + " / 12");
            // a skeletal mesh must be skipped, not mangled
            string sk = Path.Combine(dir, "SK_Bone.psk"); var bytes = CubePskx();
            var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write(bytes); w.Flush();
            var m2 = new MemoryStream(); var w2 = new BinaryWriter(m2);
            Chunk(w2, "ACTRHEAD", 0, 0, null); Chunk(w2, "PNTS0000", 12, 1, b => { b.Write(0f); b.Write(0f); b.Write(0f); });
            Chunk(w2, "VTXW0000", 16, 1, b => b.Write(new byte[16])); Chunk(w2, "FACE0000", 12, 1, b => b.Write(new byte[12]));
            Chunk(w2, "REFSKELT", 120, 2, b => b.Write(new byte[240])); w2.Flush(); File.WriteAllBytes(sk, m2.ToArray());
            var r2 = PskToObj.ConvertTree(dir);
            Check(r2.skippedSkeletal == 1 && !File.Exists(Path.Combine(dir, "SK_Bone.obj")), "skeletal PSK is skipped and reported, not converted");
            try { Directory.Delete(dir, true); } catch { }
        }
        static void UModelChecks(string tmp)
        {
            var o = new ConverterOptions { ueGame = "ue4.26", meshFormat = UModelMesh.Gltf, exportPng = true, exportLods = false };
            string args = UModel.BuildArgs(o, @"C:\x y\Pack\", @"C:\st");
            Check(args == "-path=\"C:/x y/Pack\" -game=ue4.26 -export -out=\"C:/st\" -sounds -png -gltf \"*.uasset\"", "UModel args: glTF + PNG + sounds, quoted forward-slash paths", args);
            o.ueGame = "(auto-detect)"; o.meshFormat = UModelMesh.None; o.exportPng = false; o.exportLods = true;
            args = UModel.BuildArgs(o, @"C:\p", @"C:\st");
            Check(args == "-path=\"C:/p\" -export -out=\"C:/st\" -sounds -nomesh -nostat -noanim \"*.uasset\"", "UModel args: auto game, no meshes, TGA", args);
            o.soundsOnly = true; o.meshFormat = UModelMesh.None; o.exportLods = false;
            args = UModel.BuildArgs(o, @"C:\p", @"C:\st");
            Check(args == "-path=\"C:/p\" -export -out=\"C:/st\" -sounds -nomesh -nostat -noanim -notex \"*.uasset\"", "UModel args: sounds-only preset", args);
            o.soundsOnly = false; o.meshFormat = UModelMesh.Psk; o.exportLods = true;
            Check(UModel.BuildArgs(o, @"C:\p", @"C:\st").Contains("-lods") && !UModel.BuildArgs(o, @"C:\p", @"C:\st").Contains("-gltf"), "UModel args: PSK keeps -lods and omits -gltf");

            var r = new UModelResult();
            UModel.ParseLog("Loading package Foo\nExporting Texture2D T_A\nExporting SoundWave SW_B to x\nExporting SoundWave SW_C\nERROR: Unversioned UE4 packages are not supported.\nFailed to load Bar\nDone", r);
            Check(r.exported.TryGetValue("SoundWave", out int sw) && sw == 2 && r.exported["Texture2D"] == 1 && r.errors.Count == 2 && r.errors[0].Contains("Unversioned"), "UModel log parsing counts exports per class and collects error lines", $"{r.exported.Count} classes, {r.errors.Count} errors");

            string fd = Path.Combine(tmp, "tools"); Directory.CreateDirectory(fd);
            File.WriteAllBytes(Path.Combine(fd, "umodel.exe"), new byte[1]); File.WriteAllBytes(Path.Combine(fd, "umodel_64.exe"), new byte[1]);
            string found = UModel.Find(new ConverterOptions { umodelExe = "" }, new[] { fd });
            Check(found != null && Path.GetFileName(found) == "umodel_64.exe" && Path.GetDirectoryName(found) == fd || found != null && !found.StartsWith(tmp), "umodel.exe detection prefers umodel_64.exe in a searched folder", found);
            Check(UModel.Find(new ConverterOptions { umodelExe = Path.Combine(fd, "umodel.exe") }) == Path.Combine(fd, "umodel.exe"), "explicit umodel.exe path from settings wins");

            string w1 = Path.Combine(tmp, "t1.wav"), w2 = Path.Combine(tmp, "t10.wav"), w3 = Path.Combine(tmp, "t30.wav");
            Wav(w1, 1f); Wav(w2, 10f); Wav(w3, 30f);
            Check(Mathf.Abs(AudioProbe.Duration(w1) - 1f) < 0.02f && Mathf.Abs(AudioProbe.Duration(w3) - 30f) < 0.02f, "WAV duration probe", AudioProbe.Duration(w1) + " / " + AudioProbe.Duration(w3));
            Check(AudioProbe.Classify("a/b.wav", 1f).load == AudioClipLoadType.DecompressOnLoad && AudioProbe.Classify("a/b.wav", 10f).load == AudioClipLoadType.CompressedInMemory && AudioProbe.Classify("a/b.wav", 30f).load == AudioClipLoadType.Streaming
                  && AudioProbe.Classify("Audio/Menu/click.wav", 6f).load == AudioClipLoadType.DecompressOnLoad, "audio load type: short/UI decompress, mid compressed, 20 s+ streaming");
            Check(UE4Names.CleanClassDirs("Pack/SoundWave/UI") == "Pack/UI" && UE4Names.CleanClassDirs("Texture2D") == "", "UModel class folders are stripped");
        }

        static void Wav(string path, float seconds, int rate = 8000)
        {
            int n = (int)(seconds * rate);
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(new[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + n); bw.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rate); bw.Write(rate); bw.Write((short)1); bw.Write((short)8);
                bw.Write(new[] { 'd', 'a', 't', 'a' }); bw.Write(n);
                bw.Write(new byte[n]);
            }
        }

        /// <summary>Runs the whole extraction stage against a FAKE umodel (a .cmd that prints UModel-style lines and copies a prepared fixture into the staging folder). It proves the orchestration, not real UModel.</summary>
        static void FakeUModelEndToEnd(string tmp)
        {
            string fix = Path.Combine(tmp, "fixture"), src = Path.Combine(tmp, "umsrc", "Content");
            Directory.CreateDirectory(Path.Combine(fix, "Pack", "Texture2D")); Directory.CreateDirectory(Path.Combine(fix, "Pack", "SoundWave", "UI")); Directory.CreateDirectory(Path.Combine(fix, "Pack", "SoundWave", "Amb")); Directory.CreateDirectory(src);
            Png(Path.Combine(fix, "Pack", "Texture2D", "T_Fake_D.png"), 8, 8, (x, y) => new Color32(10, 20, 30, 255));
            Wav(Path.Combine(fix, "Pack", "SoundWave", "UI", "SW_Click.wav"), 0.5f); Wav(Path.Combine(fix, "Pack", "SoundWave", "Amb", "SW_Wind.wav"), 25f);
            bool gltfInstalled = Planner.GltfInstalled();
            if (!gltfInstalled) { Directory.CreateDirectory(Path.Combine(fix, "Pack", "StaticMesh")); File.WriteAllBytes(Path.Combine(fix, "Pack", "StaticMesh", "SM_Fake.glb"), new byte[] { 1, 2, 3, 4 }); }
            foreach (var n in new[] { "BP_Door.uasset", "SW_Click.uasset", "T_Fake_D.uasset" }) File.WriteAllBytes(Path.Combine(src, n), new byte[] { 0xC1, 0x83, 0x2A, 0x9E });

            string staging = UModel.StagingDir("UE4ConvTestUm");
            string cmd = Path.Combine(tmp, "fake_umodel.cmd");
            File.WriteAllText(cmd, "@echo off\r\necho Fake UModel %*\r\necho Exporting Texture2D T_Fake_D\r\necho Exporting SoundWave SW_Click\r\necho ERROR: fake warning line\r\nxcopy /E /I /Y \"" + fix + "\\*\" \"" + staging + "\\\" >nul\r\nexit /b 0\r\n");
            var o = new ConverterOptions
            {
                sourceFolder = Path.Combine(tmp, "umsrc"), destRoot = "Assets/_Project", packName = "UE4ConvTestUm", overwrite = true, useUModel = true,
                umodelExe = cmd, reuseStaging = false, keepStaging = false, makePrefabs = true
            };
            var rep = UE4Converter.Run(o);
            Log.AppendLine("--- fake umodel summary ---\n" + rep.Summary() + "\numodel: " + rep.umodelSummary);
            Check(rep.umodelSummary != null && rep.umodelSummary.Contains("files extracted") && rep.umodelSummary.Contains("exit code 0"), "fake UModel run: summary records exit code and extracted files", rep.umodelSummary);
            Check(rep.umodelLog != null && File.Exists(rep.umodelLog) && File.ReadAllText(rep.umodelLog).Contains("Exporting SoundWave SW_Click"), "UModel log captured to a file");
            Check(rep.items.Any(i => i.category == "UModel log" && i.message.Contains("fake warning")), "UModel error lines are copied into the report");
            var click = AssetImporter.GetAtPath(UmDest + "/Audio/Pack/UI/SW_Click.wav") as AudioImporter;
            var wind = AssetImporter.GetAtPath(UmDest + "/Audio/Pack/Amb/SW_Wind.wav") as AudioImporter;
            Check(click != null && click.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad, "short UI clip imported as DecompressOnLoad", click == null ? "missing" : click.defaultSampleSettings.loadType.ToString());
            Check(wind != null && wind.defaultSampleSettings.loadType == AudioClipLoadType.Streaming, "25 s ambience imported as Streaming", wind == null ? "missing" : wind.defaultSampleSettings.loadType.ToString());
            Check(AssetDatabase.LoadAssetAtPath<Texture2D>(UmDest + "/Textures/Pack/T_Fake_D.png") != null, "extracted PNG texture converted into Textures/");
            Check(rep.items.Any(i => i.kind == "Unconvertible" && i.category.Contains("Blueprint")), "Blueprint .uasset still reported as not convertible");
            if (!gltfInstalled)
            {
                Check(rep.items.Any(i => i.category == "glTF meshes" && i.message.Contains("com.unity.cloud.gltfast")), "glTF meshes reported with the glTFast install instruction");
                Check(Directory.Exists(staging), "staging kept while glTF meshes are pending");
            }
            else Check(!Directory.Exists(staging), "staging removed after a successful conversion");

            // sounds-only preset shape: exact destination, no Audio/ subfolder, nothing but clips
            o.destOverride = UmDest2; o.audioSubfolder = ""; o.soundsOnly = true; o.meshFormat = UModelMesh.None; o.reuseStaging = true; o.makePrefabs = false;
            var rep2 = UE4Converter.Run(o);
            Log.AppendLine("--- sounds-only run ---\n" + rep2.Summary());
            Check(AssetDatabase.LoadAssetAtPath<AudioClip>(UmDest2 + "/Pack/UI/SW_Click.wav") != null && AssetDatabase.LoadAssetAtPath<AudioClip>(UmDest2 + "/Pack/Amb/SW_Wind.wav") != null, "sounds-only: clips land directly under the destination with the folder structure kept");
            Check(!AssetDatabase.IsValidFolder(UmDest2 + "/Textures") && !AssetDatabase.IsValidFolder(UmDest2 + "/Models"), "sounds-only: no textures or meshes written");
            Check(rep2.items.Any(i => i.category == "Audio totals" && i.message.Contains("2 clips")) && rep2.items.Any(i => i.category == "Audio import" && i.message.Contains("all 2 clips")), "sounds-only: clip count, size and import verification reported");
        }
        // ---- fixtures -------------------------------------------------------------------------------------------

        static void Generate(string root)
        {
            Obj(Path.Combine(root, "Meshes/SM_Test_LOD0.obj"), 50, "M_Test");
            Obj(Path.Combine(root, "Meshes/SM_Test_LOD1.obj"), 25, "M_Test");
            Obj(Path.Combine(root, "Meshes/SM_Leaf.obj"), 50, "M_Leaf");
            Obj(Path.Combine(root, "Meshes/SM_Rock.obj"), 50, "M_Rock");
            Png(Path.Combine(root, "Textures/T_Test_D.png"), 8, 8, (x, y) => new Color32(200, 180, 150, 255));
            Png(Path.Combine(root, "Textures/T_Test_N.png"), 8, 8, (x, y) => new Color32(128, 200, 255, 255));
            Png(Path.Combine(root, "Textures/T_Test_ORM.png"), 8, 8, (x, y) => new Color32(200, 100, 50, 255));
            Tga(Path.Combine(root, "Textures/T_Leaf_D.tga"), 8, 8);
            Png(Path.Combine(root, "Textures/T_Leaf_Mask.png"), 8, 8, (x, y) => x < 4 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255));
            Png(Path.Combine(root, "Textures/T_Rock_D.png"), 8, 8, (x, y) => new Color32(120, 120, 120, 255));
            Png(Path.Combine(root, "Textures/T_Rock_R.png"), 8, 8, (x, y) => new Color32(64, 64, 64, 255));
            Png(Path.Combine(root, "Textures/T_Rock_M.png"), 8, 8, (x, y) => new Color32(200, 200, 200, 255));
            foreach (var n in new[] { "BP_Door.uasset", "M_Wall.uasset", "L_Ruins.umap" }) File.WriteAllBytes(Path.Combine(root, "Content", n), new byte[] { 0xC1, 0x83, 0x2A, 0x9E });
        }

        static void Png(string path, int w, int h, Func<int, int, Color32> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels32(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        /// <summary>Uncompressed 24-bit TGA with a TOP-LEFT origin: first 4 file rows red, last 4 blue.</summary>
        static void Tga(string path, int w, int h)
        {
            var b = new List<byte> { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)w, 0, (byte)h, 0, 24, 0x20 };
            for (int row = 0; row < h; row++)
                for (int x = 0; x < w; x++)
                {
                    if (row < h / 2) { b.Add(0); b.Add(0); b.Add(255); }   // BGR red
                    else { b.Add(255); b.Add(0); b.Add(0); }               // BGR blue
                }
            File.WriteAllBytes(path, b.ToArray());
        }

        static void Obj(string path, float h, string mat)
        {
            string F(float v) => v.ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            string mtl = Path.GetFileNameWithoutExtension(path) + ".mtl";
            File.WriteAllText(Path.ChangeExtension(path, ".mtl"), $"newmtl {mat}\nKd 0.8 0.8 0.8\n");
            sb.AppendLine("mtllib " + mtl).AppendLine("o Cube");
            foreach (var v in new[] { (-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1) })
                sb.AppendLine($"v {F(v.Item1 * h)} {F(v.Item2 * h)} {F(v.Item3 * h)}");
            sb.AppendLine("vt 0 0").AppendLine("vt 1 0").AppendLine("vt 1 1").AppendLine("vt 0 1");
            sb.AppendLine("vn 0 0 -1").AppendLine("vn 0 0 1").AppendLine("vn -1 0 0").AppendLine("vn 1 0 0").AppendLine("vn 0 -1 0").AppendLine("vn 0 1 0");
            sb.AppendLine("usemtl " + mat);
            string Face(int n, params int[] i) => $"f {i[0]}/1/{n} {i[1]}/2/{n} {i[2]}/3/{n} {i[3]}/4/{n}";
            sb.AppendLine(Face(1, 1, 4, 3, 2)).AppendLine(Face(2, 5, 6, 7, 8)).AppendLine(Face(3, 1, 5, 8, 4))
              .AppendLine(Face(4, 2, 3, 7, 6)).AppendLine(Face(5, 1, 2, 6, 5)).AppendLine(Face(6, 4, 8, 7, 3));
            File.WriteAllText(path, sb.ToString());
        }
    }
}