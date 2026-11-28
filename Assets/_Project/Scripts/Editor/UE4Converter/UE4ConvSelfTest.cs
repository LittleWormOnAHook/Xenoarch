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

                var o = new ConverterOptions { sourceFolder = tmp, destRoot = "Assets/_Project", packName = "UE4ConvTest", overwrite = true, dryRun = true };
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
            }
            catch (Exception e) { Check(false, "test threw an exception", e.ToString()); }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (AssetDatabase.IsValidFolder(DestRoot)) AssetDatabase.DeleteAsset(DestRoot);
                if (Directory.Exists(tmp)) try { Directory.Delete(tmp, true); } catch { }
                try
                {
                    string rd = UE4Converter.ReportFolder;
                    if (Directory.Exists(rd)) foreach (var f in Directory.GetFiles(rd, "UE4ConvTest_*")) File.Delete(f);
                }
                catch { }
                AssetDatabase.Refresh();
                Check(SceneManager.GetActiveScene().isDirty == dirtyBefore, "active scene dirty flag unchanged", "before " + dirtyBefore + ", after " + SceneManager.GetActiveScene().isDirty);
                Check(!AssetDatabase.IsValidFolder(DestRoot) && !Directory.Exists(tmp), "scratch folders cleaned up");
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