using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>UE naming conventions: texture suffixes, LOD suffixes, material/mesh name normalisation.</summary>
    internal static class UE4Names
    {
        static readonly Regex LodRx = new Regex(@"^(?<b>.*?)[_\.\-]?LOD[_\-]?(?<n>\d+)$", RegexOptions.IgnoreCase);
        static readonly Regex ResRx = new Regex(@"^(\d+k|\d{3,5}|\d+x\d+|png|tga|exr|jpg|jpeg)$", RegexOptions.IgnoreCase);
        static readonly Regex PrefixRx = new Regex(@"^(t|tex|texture|sm|s|sk|skm|m|mi|mic|mat|material|ms|mm)_", RegexOptions.IgnoreCase);
        static readonly Regex SuffixRx = new Regex(@"(_mat|_material|_inst|_instance|_mi|_lod\d+|\.\d{3}|_\d{3})$", RegexOptions.IgnoreCase);
        public static readonly Regex LoopRx = new Regex(@"(idle|walk|run|jog|sprint|loop|cycle|breath|hover|spin|rotate|sway|wind|flap|swim|fly|crouchwalk|strafe)", RegexOptions.IgnoreCase);
        public static readonly Regex FoliageRx = new Regex(@"(leaf|leaves|foliage|grass|fern|frond|plant|vine|ivy|bush|branch|moss|weed|flower|shrub|vegetation|cutout|mask)", RegexOptions.IgnoreCase);
        public static readonly Regex AnimNameRx = new Regex(@"^(a|anim|animation|am|as)_|_anim(ation)?$", RegexOptions.IgnoreCase);

        // {AO, roughness, metallic} channel indices (0=R,1=G,2=B)
        static readonly Dictionary<string, int[]> Packed = new Dictionary<string, int[]>
        {
            { "orm", new[] { 0, 1, 2 } }, { "arm", new[] { 0, 1, 2 } }, { "occlusionroughnessmetallic", new[] { 0, 1, 2 } },
            { "occlusionroughnessmetalness", new[] { 0, 1, 2 } },
            { "rma", new[] { 2, 0, 1 } }, { "roughnessmetallicao", new[] { 2, 0, 1 } }, { "roughnessmetallicocclusion", new[] { 2, 0, 1 } },
            { "mra", new[] { 2, 1, 0 } }, { "metallicroughnessao", new[] { 2, 1, 0 } },
        };

        static readonly Dictionary<string, TexRole> Roles = new Dictionary<string, TexRole>
        {
            { "d", TexRole.BaseColor }, { "basecolor", TexRole.BaseColor }, { "albedo", TexRole.BaseColor }, { "diffuse", TexRole.BaseColor },
            { "diff", TexRole.BaseColor }, { "col", TexRole.BaseColor }, { "color", TexRole.BaseColor }, { "colour", TexRole.BaseColor },
            { "bc", TexRole.BaseColor }, { "base", TexRole.BaseColor }, { "basemap", TexRole.BaseColor },
            { "n", TexRole.Normal }, { "normal", TexRole.Normal }, { "nrm", TexRole.Normal }, { "nor", TexRole.Normal }, { "norm", TexRole.Normal },
            { "normalmap", TexRole.Normal }, { "nm", TexRole.Normal },
            { "r", TexRole.Roughness }, { "rough", TexRole.Roughness }, { "roughness", TexRole.Roughness },
            { "gloss", TexRole.Roughness }, { "glossiness", TexRole.Roughness }, { "smoothness", TexRole.Roughness },
            { "m", TexRole.Metallic }, { "metal", TexRole.Metallic }, { "metallic", TexRole.Metallic }, { "metalness", TexRole.Metallic },
            { "ao", TexRole.AO }, { "occlusion", TexRole.AO }, { "ambientocclusion", TexRole.AO }, { "occ", TexRole.AO }, { "o", TexRole.AO },
            { "e", TexRole.Emissive }, { "emissive", TexRole.Emissive }, { "emission", TexRole.Emissive }, { "emit", TexRole.Emissive }, { "glow", TexRole.Emissive },
            { "h", TexRole.Height }, { "height", TexRole.Height }, { "disp", TexRole.Height }, { "displacement", TexRole.Height }, { "bump", TexRole.Height }, { "parallax", TexRole.Height },
            { "mask", TexRole.Opacity }, { "opacity", TexRole.Opacity }, { "alpha", TexRole.Opacity }, { "opacitymask", TexRole.Opacity }, { "transparency", TexRole.Opacity },
            { "s", TexRole.Specular }, { "spec", TexRole.Specular }, { "specular", TexRole.Specular },
        };
        static readonly HashSet<string> InvertTokens = new HashSet<string> { "gloss", "glossiness", "smoothness" };

        /// <summary>Strips _LODn. Returns the base name; lod = -1 if none.</summary>
        public static string StripLod(string stem, out int lod)
        {
            var m = LodRx.Match(stem);
            if (m.Success && m.Groups["b"].Value.Length > 0) { lod = int.Parse(m.Groups["n"].Value); return m.Groups["b"].Value; }
            lod = -1; return stem;
        }

        public static TexRole Classify(string stem, out string key, out int[] layout, out bool invert, out bool gl, out bool dx)
        {
            layout = null; invert = false; gl = false; dx = false;
            var tokens = stem.Replace('-', '_').Replace('.', '_').Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            int end = tokens.Count;
            while (end > 1)
            {
                string t = tokens[end - 1].ToLowerInvariant();
                if (t == "gl" || t == "opengl") { gl = true; end--; }
                else if (t == "dx" || t == "directx") { dx = true; end--; }
                else if (ResRx.IsMatch(t)) end--;
                else break;
            }
            TexRole role = TexRole.Unknown; int used = 0;
            for (int n = Math.Min(3, end - 1); n >= 1 && role == TexRole.Unknown; n--)
            {
                string joined = string.Concat(tokens.Skip(end - n).Take(n)).ToLowerInvariant();
                if (Packed.TryGetValue(joined, out var lay)) { role = TexRole.Packed; layout = lay; used = n; }
                else if (Roles.TryGetValue(joined, out var r)) { role = r; used = n; invert = InvertTokens.Contains(joined); }
            }
            int keyEnd = role == TexRole.Unknown ? end : end - used;
            var keyTokens = tokens.Take(keyEnd).ToList();
            if (keyTokens.Count > 1 && Regex.IsMatch(keyTokens[0], "^(t|tex|texture)$", RegexOptions.IgnoreCase)) keyTokens.RemoveAt(0);
            key = keyTokens.Count == 0 ? stem : string.Join("_", keyTokens);
            return role;
        }

        /// <summary>Lower-case alphanumeric key for fuzzy name matching (M_Wall_01 == T_Wall_01 == SM_Wall_01 == wall01).</summary>
        public static string Norm(string name)
        {
            string s = name ?? "";
            s = StripLod(s, out _);
            for (int i = 0; i < 3; i++)
            {
                var a = PrefixRx.Replace(s, "", 1);
                var b = SuffixRx.Replace(a, "", 1);
                if (a == s && b == s) break;
                s = b;
            }
            return Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
        }

        static readonly HashSet<string> LeadStrip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "meshes", "mesh", "models", "model", "staticmeshes", "skeletalmeshes", "animations", "anims", "textures", "texture", "fbx", "source", "export", "exports" };

        public static string StripLead(string relDir)
        {
            if (string.IsNullOrEmpty(relDir)) return "";
            var parts = relDir.Split('/').Where(p => p.Length > 0).ToList();
            if (parts.Count > 0 && LeadStrip.Contains(parts[0])) parts.RemoveAt(0);
            return string.Join("/", parts);
        }

        public static string UeCategory(string fileName, string ext)
        {
            string n = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            if (ext == ".umap" || n.Contains("landscape") || n.StartsWith("l_") || n.Contains("heightmap_")) return "Level / Landscape (export heightmap + weightmaps and rebuild as a Unity Terrain)";
            if (n.StartsWith("abp_")) return "Animation Blueprint (rebuild as an Animator Controller)";
            if (n.StartsWith("bp_") || n.StartsWith("wbp_") || n.StartsWith("bpi_") || n.StartsWith("bpfl_")) return "Blueprint (logic must be rewritten as C# components)";
            if (n.StartsWith("ns_") || n.StartsWith("fx_") || n.StartsWith("ps_") || n.StartsWith("p_")) return "Niagara/Cascade effect (rebuild as VFX Graph or a Particle System)";
            if (n.StartsWith("m_") || n.StartsWith("mi_") || n.StartsWith("mic_") || n.StartsWith("mf_") || n.StartsWith("mpc_")) return "Material graph / instance (rebuilt as URP Lit from matching textures where possible)";
            if (n.StartsWith("sk_") || n.StartsWith("skm_")) return "Skeletal mesh (export FBX from UE4)";
            if (n.StartsWith("sm_") || n.StartsWith("s_")) return "Static mesh (export FBX from UE4)";
            if (n.StartsWith("t_") || n.StartsWith("tex_")) return "Texture (export PNG/TGA from UE4)";
            if (n.StartsWith("a_") || n.StartsWith("anim_") || n.StartsWith("am_") || n.StartsWith("as_") || n.StartsWith("bs_")) return "Animation (export FBX with animation from UE4)";
            if (n.StartsWith("sw_") || n.StartsWith("sc_") || n.StartsWith("sound")) return "Sound (export WAV from UE4)";
            if (n.StartsWith("phys_") || n.StartsWith("pa_")) return "Physics asset (rebuild colliders/joints by hand)";
            if (ext == ".uexp" || ext == ".ubulk" || ext == ".uptnl") return "UE companion data file (belongs to a .uasset)";
            return "Other Unreal asset";
        }
    }

    /// <summary>Pixel buffers: PNG/JPG/TGA decoded directly, other formats read through a temporary editor import.</summary>
    internal sealed class Img
    {
        public int w, h; public Color32[] px; public bool hasAlpha;
    }

    internal static class ImageIO
    {
        public static Img Read(string path, string tmpAssetFolder, out string err)
        {
            err = null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    if (!ImageConversion.LoadImage(t, File.ReadAllBytes(path), false)) { UnityEngine.Object.DestroyImmediate(t); err = "could not decode image"; return null; }
                    var img = new Img { w = t.width, h = t.height, px = t.GetPixels32() };
                    UnityEngine.Object.DestroyImmediate(t);
                    FinishAlpha(img);
                    return img;
                }
                if (ext == ".tga")
                {
                    var img = DecodeTga(File.ReadAllBytes(path), out err);
                    if (img != null) FinishAlpha(img);
                    return img;
                }
                return ReadViaImport(path, tmpAssetFolder, out err);
            }
            catch (Exception e) { err = e.Message; return null; }
        }

        static void FinishAlpha(Img img)
        {
            bool allZero = true, anyLess = false;
            foreach (var c in img.px) { if (c.a != 0) allZero = false; if (c.a < 255) anyLess = true; if (!allZero && anyLess) break; }
            if (allZero) { for (int i = 0; i < img.px.Length; i++) img.px[i].a = 255; img.hasAlpha = false; }   // exporters that write alpha=0 for opaque
            else img.hasAlpha = anyLess;
        }

        static Img DecodeTga(byte[] d, out string err)
        {
            err = null;
            if (d.Length < 18) { err = "TGA too short"; return null; }
            int idLen = d[0], cmType = d[1], type = d[2];
            int cmLen = d[5] | (d[6] << 8), cmDepth = d[7];
            int w = d[12] | (d[13] << 8), h = d[14] | (d[15] << 8), bpp = d[16], desc = d[17];
            if (type != 2 && type != 3 && type != 10 && type != 11) { err = "unsupported TGA type " + type; return null; }
            if (bpp != 8 && bpp != 24 && bpp != 32) { err = "unsupported TGA depth " + bpp; return null; }
            int bytes = bpp / 8;
            int pos = 18 + idLen + (cmType == 1 ? cmLen * ((cmDepth + 7) / 8) : 0);
            bool rle = type >= 9, topLeft = (desc & 0x20) != 0;
            var file = new Color32[w * h];
            int i = 0;
            while (i < w * h && pos < d.Length)
            {
                int count = 1; bool run = false;
                if (rle) { int hdr = d[pos++]; count = (hdr & 0x7F) + 1; run = (hdr & 0x80) != 0; }
                Color32 c = default;
                for (int k = 0; k < count && i < w * h; k++)
                {
                    if (k == 0 || !run)
                    {
                        if (pos + bytes > d.Length) break;
                        c = bytes == 1 ? new Color32(d[pos], d[pos], d[pos], 255)
                          : bytes == 3 ? new Color32(d[pos + 2], d[pos + 1], d[pos], 255)
                          : new Color32(d[pos + 2], d[pos + 1], d[pos], d[pos + 3]);
                        pos += bytes;
                    }
                    file[i++] = c;
                }
            }
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int dst = topLeft ? h - 1 - y : y;
                Array.Copy(file, y * w, px, dst * w, w);
            }
            return new Img { w = w, h = h, px = px };
        }

        static Img ReadViaImport(string path, string tmpAssetFolder, out string err)
        {
            err = null;
            UE4Converter.EnsureFolder(tmpAssetFolder);
            string asset = tmpAssetFolder + "/" + Guid.NewGuid().ToString("N") + Path.GetExtension(path);
            string disk = DiskPath(asset);
            File.Copy(path, disk, true);
            Registry.Tex[asset] = new TexJob { readTemp = true };
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
            Img img = null;
            if (t == null) err = "Unity could not import this format";
            else
            {
                try
                {
                    var c = t.GetPixels();
                    var px = new Color32[c.Length];
                    for (int i = 0; i < c.Length; i++) px[i] = c[i];
                    img = new Img { w = t.width, h = t.height, px = px };
                    FinishAlpha(img);
                }
                catch (Exception e) { err = e.Message; }
            }
            AssetDatabase.DeleteAsset(asset);
            Registry.Tex.Remove(asset);
            return img;
        }

        public static string DiskPath(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath).Replace('/', Path.DirectorySeparatorChar);

        public static byte[] EncodePng(Img img, bool alpha)
        {
            var t = new Texture2D(img.w, img.h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, true);
            t.SetPixels32(img.px);
            var b = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return b;
        }

        public static Color32 Sample(Img i, int x, int y, int w, int h)
        {
            if (i.w == w && i.h == h) return i.px[y * w + x];
            int sx = Math.Min(i.w - 1, (int)((x + 0.5f) * i.w / w)), sy = Math.Min(i.h - 1, (int)((y + 0.5f) * i.h / h));
            return i.px[sy * i.w + sx];
        }

        public static byte Ch(Color32 c, int ch) => ch == 0 ? c.r : ch == 1 ? c.g : c.b;

        /// <summary>UE DirectX normal (green down) to Unity/OpenGL (green up).</summary>
        public static void FlipGreen(Img n) { for (int i = 0; i < n.px.Length; i++) n.px[i].g = (byte)(255 - n.px[i].g); }

        public static void SetAlphaFromGray(Img colour, Img mask)
        {
            int w = colour.w, h = colour.h;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var m = Sample(mask, x, y, w, h);
                    var c = colour.px[y * w + x];
                    c.a = m.r;
                    colour.px[y * w + x] = c;
                }
            colour.hasAlpha = true;
        }

        /// <summary>URP Lit metallic map: R = metallic, A = smoothness (1 - roughness). Any input may be null.</summary>
        public static Img PackMetalSmooth(Img packed, int[] layout, Img rough, bool invertRough, Img metal)
        {
            Img first = packed ?? rough ?? metal;
            int w = first.w, h = first.h;
            var o = new Img { w = w, h = h, px = new Color32[w * h], hasAlpha = true };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte met = 0, smooth = 128;
                    if (packed != null) { var p = Sample(packed, x, y, w, h); met = Ch(p, layout[2]); smooth = (byte)(255 - Ch(p, layout[1])); }
                    else if (rough != null) { byte v = Sample(rough, x, y, w, h).r; smooth = invertRough ? v : (byte)(255 - v); }
                    if (metal != null) met = Sample(metal, x, y, w, h).r;
                    o.px[y * w + x] = new Color32(met, met, met, smooth);
                }
            return o;
        }

        /// <summary>Grayscale occlusion map (R=G=B). Returns null when the source is effectively all white.</summary>
        public static Img PackAO(Img src, int channel)
        {
            var o = new Img { w = src.w, h = src.h, px = new Color32[src.px.Length] };
            int min = 255;
            for (int i = 0; i < src.px.Length; i++) { byte v = Ch(src.px[i], channel); if (v < min) min = v; o.px[i] = new Color32(v, v, v, 255); }
            return min >= 250 ? null : o;
        }
    }
}