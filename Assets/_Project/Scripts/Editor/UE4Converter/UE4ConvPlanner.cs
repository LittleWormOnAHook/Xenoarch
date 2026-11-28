using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>Scans the source folder (read-only) and decides what will be converted and where it goes.</summary>
    internal static class Planner
    {
        static readonly HashSet<string> MeshExt = new HashSet<string> { ".fbx", ".obj" };
        static readonly HashSet<string> TexExt = new HashSet<string> { ".png", ".tga", ".jpg", ".jpeg", ".bmp", ".exr", ".tif", ".tiff", ".psd", ".hdr" };
        static readonly HashSet<string> AudioExt = new HashSet<string> { ".wav", ".ogg", ".mp3" };
        static readonly HashSet<string> UeExt = new HashSet<string> { ".uasset", ".umap", ".uexp", ".ubulk", ".uptnl", ".upk" };
        static readonly HashSet<string> UnsupportedExt = new HashSet<string> { ".blend", ".ma", ".mb", ".max", ".c4d", ".sbs", ".sbsar", ".spp", ".glb", ".gltf", ".abc", ".usd", ".usda", ".usdc", ".usdz", ".ztl", ".zpr" };
        static readonly Regex SkipRx = new Regex(@"(^|/)(\.git|Intermediate|Saved|__MACOSX|\.vs|Library|Temp)(/|$)", RegexOptions.IgnoreCase);

        public static string J(params string[] parts) => string.Join("/", parts.Where(x => !string.IsNullOrEmpty(x)).Select(x => x.Trim('/')));

        public static Plan Build(ConverterOptions o, ConversionReport rep)
        {
            var p = new Plan { o = o, src = Path.GetFullPath(o.sourceFolder), dest = o.DestFolder };
            var ignored = new Dictionary<string, int>();
            var texEntries = new List<TexEntry>();

            foreach (var f in Directory.EnumerateFiles(p.src, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string rel = f.Substring(p.src.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (SkipRx.IsMatch(rel)) continue;
                string ext = Path.GetExtension(f).ToLowerInvariant();
                string relDir = (Path.GetDirectoryName(rel) ?? "").Replace('\\', '/');
                string stem = Path.GetFileNameWithoutExtension(f);

                if (MeshExt.Contains(ext)) p.meshes.Add(MakeMesh(o, rep, f, rel, relDir, ext, stem));
                else if (TexExt.Contains(ext)) texEntries.Add(MakeTex(f, relDir, ext, stem));
                else if (AudioExt.Contains(ext))
                {
                    if (o.copyAudio) p.audio.Add((f, J(p.dest, "Audio", UE4Names.StripLead(relDir), Path.GetFileName(f))));
                }
                else if (ext == ".mtl") p.aux.Add((f, null));   // dest filled after meshes are placed
                else if (UeExt.Contains(ext))
                {
                    string cat = UE4Names.UeCategory(f, ext);
                    rep.Add(ReportKind.Unconvertible, "Unreal binary: " + cat, rel, null,
                        ".uasset/.umap cannot be read outside Unreal. In UE4 use Content Browser > Asset Actions > Export (or the pack's Source folder) and convert that export instead.");
                }
                else if (ext == ".uproject" || ext == ".uplugin")
                    rep.Add(ReportKind.Info, "Unreal project file", rel, null, "UE project/plugin descriptor; nothing to convert. Export the content from UE4.");
                else if (UnsupportedExt.Contains(ext))
                    rep.Add(ReportKind.Unconvertible, "Unsupported format " + ext, rel, null, "Not handled by this tool (export as FBX/OBJ + PNG/TGA, or import with a Unity package).");
                else { ignored.TryGetValue(ext, out int n); ignored[ext] = n + 1; }
            }
            foreach (var kv in ignored.OrderBy(k => k.Key))
                rep.Add(ReportKind.Skipped, "Ignored file type", null, null, $"{kv.Value} x '{(kv.Key.Length == 0 ? "(no extension)" : kv.Key)}'");

            // MTL files next to the OBJ that uses them
            for (int i = 0; i < p.aux.Count; i++)
            {
                var a = p.aux[i];
                var obj = p.meshes.FirstOrDefault(m => m.IsObj && string.Equals(Path.ChangeExtension(m.src, ".mtl"), a.src, StringComparison.OrdinalIgnoreCase));
                p.aux[i] = (a.src, obj != null ? Path.ChangeExtension(obj.destAsset, ".mtl") : null);
            }
            p.aux.RemoveAll(a => a.dest == null);

            // LOD files of one object must share a scale (decided from the largest one) or the LODs would not line up
            if (o.scaleMode == ConvScaleMode.Auto)
                foreach (var g in p.meshes.Where(m => m.IsObj).GroupBy(m => (m.relDir + "|" + (o.detectLods ? m.baseName : m.stem)).ToLowerInvariant()))
                {
                    var big = g.OrderByDescending(m => m.objExtent).First();
                    foreach (var m in g) { m.scale = big.scale; m.scaleNote = big.scaleNote + (m != big ? " (shared with the largest LOD)" : ""); }
                    if (big.objExtent > 20f && big.objExtent <= 200f)
                        rep.Add(ReportKind.Warning, "Scale", big.src, null, $"OBJ extent {big.objExtent:0.#} units is ambiguous (cm or m?). Chose x{big.scale}; set Scale to UE cm or None in the window if the prefab is the wrong size.");
                }
            BuildPrefabJobs(p, rep);
            BuildSets(p, rep, texEntries);
            return p;
        }

        // ---- meshes ---------------------------------------------------------------------------------------------

        static MeshEntry MakeMesh(ConverterOptions o, ConversionReport rep, string f, string rel, string relDir, string ext, string stem)
        {
            var m = new MeshEntry { src = f, relDir = relDir, ext = ext, stem = stem, baseName = stem };
            if (o.detectLods) m.baseName = UE4Names.StripLod(stem, out m.lod);
            m.nameIsAnim = UE4Names.AnimNameRx.IsMatch(stem);
            bool hasSkin = false, hasGeom = true;

            if (ext == ".fbx")
            {
                var fi = ProbeFbx(f);
                m.probed = fi.ok; m.hasAnim = fi.anim; m.hasUcx = fi.ucx; hasSkin = fi.skin; hasGeom = fi.geom || !fi.ok;
                if (!fi.ok) rep.Add(ReportKind.Warning, "Mesh probe", rel, null, "Could not inspect the FBX (too large or unreadable); treated as a static mesh.");
            }
            if (m.hasAnim && (!hasGeom || m.nameIsAnim)) m.kind = MeshKind.AnimOnly;
            else if (hasSkin) m.kind = MeshKind.Skeletal;
            else m.kind = MeshKind.Static;

            switch (o.scaleMode)
            {
                case ConvScaleMode.Auto:
                    if (ext == ".obj")
                    {
                        m.objExtent = ObjExtent(f);
                        m.useFileScale = false;
                        if (m.objExtent > 50f) { m.scale = 0.01f; m.scaleNote = $"OBJ extent {m.objExtent:0.#} units, treated as UE centimetres (x0.01)"; }
                        else { m.scale = 1f; m.scaleNote = $"OBJ extent {m.objExtent:0.##} units, treated as metres (x1)"; }

                    }
                    else { m.useFileScale = true; m.scale = 1f; m.scaleNote = "FBX unit scale read from the file"; }
                    break;
                case ConvScaleMode.UnrealCm: m.useFileScale = false; m.scale = 0.01f; m.scaleNote = "forced UE cm -> m (x0.01)"; break;
                case ConvScaleMode.None: m.useFileScale = false; m.scale = 1f; m.scaleNote = "no scaling"; break;
                default: m.useFileScale = false; m.scale = o.customScale; m.scaleNote = "custom x" + o.customScale; break;
            }

            string folder = m.kind == MeshKind.AnimOnly ? "Animations" : "Models";
            m.destAsset = J(p_dest(o), folder, UE4Names.StripLead(relDir), Path.GetFileName(f));
            return m;
        }

        static string p_dest(ConverterOptions o) => o.DestFolder;

        sealed class FbxInfo { public bool ok, anim, skin, geom, ucx; }

        static readonly byte[] TLimb = Encoding.ASCII.GetBytes("LimbNode"), TCluster = Encoding.ASCII.GetBytes("Cluster"), TSkin = Encoding.ASCII.GetBytes("Skin"),
            TAnim1 = Encoding.ASCII.GetBytes("AnimStack"), TAnim2 = Encoding.ASCII.GetBytes("AnimationStack"), TGeom = Encoding.ASCII.GetBytes("Geometry"), TUcx = Encoding.ASCII.GetBytes("UCX_");

        static FbxInfo ProbeFbx(string path)
        {
            var fi = new FbxInfo();
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length > 1500L * 1024 * 1024) return fi;
                    var buf = new byte[8 << 20];
                    int keep = 0;
                    bool limb = false, cluster = false, skin = false;
                    while (true)
                    {
                        int n = fs.Read(buf, keep, buf.Length - keep);
                        if (n <= 0) break;
                        int len = keep + n;
                        limb |= Has(buf, len, TLimb); cluster |= Has(buf, len, TCluster); skin |= Has(buf, len, TSkin);
                        fi.anim |= Has(buf, len, TAnim1) || Has(buf, len, TAnim2);
                        fi.geom |= Has(buf, len, TGeom);
                        fi.ucx |= Has(buf, len, TUcx);
                        keep = Math.Min(32, len);
                        Buffer.BlockCopy(buf, len - keep, buf, 0, keep);
                    }
                    fi.skin = cluster && (skin || limb);
                    fi.ok = true;
                }
            }
            catch { fi.ok = false; }
            return fi;
        }

        static bool Has(byte[] buf, int len, byte[] token)
        {
            int last = len - token.Length;
            for (int i = 0; i <= last; i++)
            {
                if (buf[i] != token[0]) continue;
                int k = 1;
                while (k < token.Length && buf[i + k] == token[k]) k++;
                if (k == token.Length) return true;
            }
            return false;
        }

        static float ObjExtent(string path)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            int count = 0;
            try
            {
                using (var sr = new StreamReader(path))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length < 8 || line[0] != 'v' || line[1] != ' ') continue;
                        var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length < 4) continue;
                        if (!float.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                            !float.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                            !float.TryParse(t[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
                        count++;
                    }
                }
            }
            catch { }
            return count == 0 ? 0f : Mathf.Max(maxX - minX, Mathf.Max(maxY - minY, maxZ - minZ));
        }

        static void BuildPrefabJobs(Plan p, ConversionReport rep)
        {
            if (!p.o.makePrefabs) return;
            var groups = new Dictionary<string, PrefabJob>();
            foreach (var m in p.meshes.Where(x => x.kind != MeshKind.AnimOnly))
            {
                string name = p.o.detectLods ? m.baseName : m.stem;
                string key = (m.relDir + "|" + name + "|" + m.ext).ToLowerInvariant();
                if (!groups.TryGetValue(key, out var job))
                {
                    job = new PrefabJob { name = name, relDir = m.relDir };
                    job.prefabPath = J(p.dest, "Prefabs", UE4Names.StripLead(m.relDir), name + ".prefab");
                    groups[key] = job; p.prefabs.Add(job);
                }
                job.lods.Add(m);
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in p.prefabs)
            {
                job.lods = job.lods.OrderBy(l => Math.Max(0, l.lod)).ToList();
                if (!used.Add(job.prefabPath))
                {
                    string alt = job.prefabPath.Replace(".prefab", "_" + job.lods[0].ext.TrimStart('.') + ".prefab");
                    rep.Add(ReportKind.Warning, "Name clash", job.lods[0].src, alt, "Two meshes map to the same prefab name; renamed.");
                    job.prefabPath = alt; used.Add(alt);
                }
                for (int i = 1; i < job.lods.Count; i++)
                    if (job.lods[i].lod == job.lods[i - 1].lod && job.lods[i].lod >= 0)
                        rep.Add(ReportKind.Warning, "LOD", job.lods[i].src, null, "Duplicate LOD index in the same group.");
            }
        }

        // ---- textures -------------------------------------------------------------------------------------------

        static TexEntry MakeTex(string f, string relDir, string ext, string stem)
        {
            var role = UE4Names.Classify(stem, out string key, out int[] layout, out bool invert, out bool gl, out bool dx);
            return new TexEntry { src = f, relDir = relDir, stem = stem, ext = ext, role = role, layout = layout, invert = invert, gl = gl, dx = dx, key = key, normKey = UE4Names.Norm(key) };
        }

        static void BuildSets(Plan p, ConversionReport rep, List<TexEntry> entries)
        {
            foreach (var g in entries.GroupBy(e => (e.relDir.ToLowerInvariant() + "|" + e.normKey)))
            {
                var first = g.First();
                var s = new SetPlan { relDir = first.relDir, key = first.key, normKey = first.normKey };
                s.all.AddRange(g);
                // a lone unclassified texture is almost always the colour map
                if (!s.all.Any(e => e.role == TexRole.BaseColor))
                {
                    var unk = s.all.Where(e => e.role == TexRole.Unknown).ToList();
                    if (unk.Count == 1) { unk[0].role = TexRole.BaseColor; rep.Add(ReportKind.Warning, "Texture naming", unk[0].src, null, "No UE suffix; assumed to be the base colour map."); }
                }
                foreach (var e in s.all)
                {
                    switch (e.role)
                    {
                        case TexRole.BaseColor: Assign(ref s.baseColor, e, rep); break;
                        case TexRole.Normal: Assign(ref s.normal, e, rep); break;
                        case TexRole.Packed: Assign(ref s.packed, e, rep); break;
                        case TexRole.Roughness: Assign(ref s.rough, e, rep); break;
                        case TexRole.Metallic: Assign(ref s.metal, e, rep); break;
                        case TexRole.AO: Assign(ref s.ao, e, rep); break;
                        case TexRole.Emissive: Assign(ref s.emissive, e, rep); break;
                        case TexRole.Height: Assign(ref s.height, e, rep); break;
                        case TexRole.Opacity: Assign(ref s.opacity, e, rep); break;
                        case TexRole.Specular: rep.Add(ReportKind.Warning, "Texture", e.src, null, "Specular maps are not used by URP Lit (metallic workflow); skipped."); break;
                        default: rep.Add(ReportKind.Warning, "Texture naming", e.src, null, "Unrecognised UE texture suffix; copied without a material slot."); break;
                    }
                }
                Layout(p, rep, s);
                p.sets.Add(s);
                if (!p.setsByNorm.TryGetValue(s.normKey, out var l)) p.setsByNorm[s.normKey] = l = new List<SetPlan>();
                l.Add(s);
            }
        }

        static void Assign(ref TexEntry slot, TexEntry e, ConversionReport rep)
        {
            if (slot == null) { slot = e; return; }
            rep.Add(ReportKind.Warning, "Texture duplicate", e.src, null, $"Another {e.role} map ({Path.GetFileName(slot.src)}) is already used for this set; this one is ignored.");
        }

        static void Layout(Plan p, ConversionReport rep, SetPlan s)
        {
            string td(string relDir) => J(p.dest, "Textures", UE4Names.StripLead(relDir));
            string Out(TexEntry e, bool png) => J(td(e.relDir), e.stem + (png ? ".png" : e.ext));

            if (s.baseColor != null)
            {
                s.mergeOpacity = s.opacity != null;
                s.outBase = Out(s.baseColor, s.mergeOpacity);
            }
            else if (s.opacity != null) rep.Add(ReportKind.Warning, "Texture", s.opacity.src, null, "Opacity/mask map has no base colour map to merge into; ignored.");

            if (s.normal != null)
            {
                s.convertNormal = p.o.flipNormalGreen && !s.normal.gl;
                s.outNormal = Out(s.normal, s.convertNormal);
            }
            if (s.packed != null && (s.rough != null || s.metal != null))
                rep.Add(ReportKind.Warning, "Texture", s.packed.src, null, "Set has both a packed map and separate roughness/metallic maps; the packed map wins.");
            if (s.packed != null || s.rough != null || s.metal != null)
            {
                s.packMS = true;
                var any = s.packed ?? s.rough ?? s.metal;
                s.outMetalSmooth = J(td(any.relDir), s.key + "_MetalSmooth.png");
            }
            if (s.packed != null) { s.packAO = true; s.outAO = J(td(s.packed.relDir), s.key + "_Occlusion.png"); }
            else if (s.ao != null) s.outAO = Out(s.ao, false);
            if (s.emissive != null) s.outEmissive = Out(s.emissive, false);
            if (s.height != null) s.outHeight = Out(s.height, false);
        }

        // ---- material <-> texture matching ----------------------------------------------------------------------

        static int Common(string a, string b)
        {
            var x = a.Split('/'); var y = b.Split('/'); int n = 0;
            while (n < x.Length && n < y.Length && string.Equals(x[n], y[n], StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        public static SetPlan Resolve(Plan p, string matName, MeshEntry m, out string how)
        {
            how = null;
            string nm = UE4Names.Norm(matName), nmesh = UE4Names.Norm(m.baseName);
            SetPlan Best(string key)
            {
                if (string.IsNullOrEmpty(key) || !p.setsByNorm.TryGetValue(key, out var l)) return null;
                return l.OrderByDescending(s => Common(s.relDir, m.relDir)).First();
            }
            var r = Best(nm); if (r != null) { how = "material name"; return r; }
            r = Best(nmesh); if (r != null) { how = "mesh name"; return r; }

            SetPlan fz = null; int fl = 0;
            foreach (var kv in p.setsByNorm)
            {
                string k = kv.Key;
                if (k.Length < 3 || k.Length <= fl) continue;
                foreach (var target in new[] { nm, nmesh })
                    if (target.Length >= 3 && (target.StartsWith(k) || k.StartsWith(target))) { fl = k.Length; fz = Best(k); break; }
            }
            if (fz != null) { how = "name prefix"; return fz; }

            string lead = UE4Names.StripLead(m.relDir);
            var sib = p.sets.Where(s => string.Equals(UE4Names.StripLead(s.relDir), lead, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sib.Count == 1) { how = "only texture set next to the mesh"; return sib[0]; }
            return null;
        }
    }
}