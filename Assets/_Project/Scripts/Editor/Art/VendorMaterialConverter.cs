using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.EditorTools.Art
{
    /// <summary>
    /// Converts vendor materials copied from Genesis (HDRP / Built-in / legacy particles / custom CG shaders) to URP.
    /// Reads the original property values straight from the .mat YAML (works even when the HDRP shader is missing),
    /// so it can be re-run safely. HDRP Lit: BaseColorMap->_BaseMap, MaskMap->_MetallicGlossMap (+ occlusion, URP reads
    /// R=metal, G=AO, A=smooth like the HDRP mask), NormalMap->_BumpMap, Emissive->_Emission. Writes a report to
    /// Logs/xenoarch_material_conversion.txt. Only touches .mat files under the given folder.
    /// </summary>
    public static class VendorMaterialConverter
    {
        public const string CreepyCatFolder = "Assets/_Creepy_Cat";
        public const string ReportPath = "Logs/xenoarch_material_conversion.txt";
        const string HdrpMaterialDataScriptGuid = "da692e001514ec24dbc4cca1949ff7e8";

        [MenuItem("Xenoarch/Art/Convert Creepy Cat Materials to URP", priority = 40)]
        public static void ConvertCreepyCat() => ConvertFolder(CreepyCatFolder);

        public static (int converted, int skipped, int failed) ConvertFolder(string folder)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var report = new StringBuilder();
            var counts = new Dictionary<string, int>();
            int converted = 0, skipped = 0, failed = 0;
            if (lit == null || particles == null || unlit == null) { Debug.LogError("[Xenoarch] MatConvert: URP shaders not found"); return (0, 0, 1); }

            var guids = AssetDatabase.FindAssets("t:Material", new[] { folder });
            AssetDatabase.StartAssetEditing();
            var stripPaths = new List<string>();
            try
            {
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".mat")) continue;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) { failed++; report.AppendLine($"FAIL  {path}  (cannot load)"); continue; }
                    string from = mat.shader != null ? mat.shader.name : "(null)";
                    if (from.StartsWith("Universal Render Pipeline/")) { skipped++; continue; }
                    string yaml = File.ReadAllText(path);
                    var p = MatYaml.Parse(yaml);
                    string kind;
                    try
                    {
                        kind = Classify(from, p);
                        switch (kind)
                        {
                            case "HDRP Lit": ConvertHdrpLit(mat, lit, p); break;
                            case "HDRP Unlit": ConvertUnlit(mat, unlit, p, "_UnlitColorMap", "_UnlitColor"); break;
                            case "Standard": ConvertStandard(mat, lit, p); break;
                            case "Particles Additive": ConvertParticles(mat, particles, p, true); break;
                            case "Particles Alpha": ConvertParticles(mat, particles, p, false); break;
                            case "Unlit": ConvertUnlit(mat, unlit, p, "_MainTex", "_Color"); break;
                            case "Glass": ConvertGlass(mat, lit, p); break;
                            default: ConvertStandard(mat, lit, p); kind = "Fallback->Lit"; break;
                        }
                        EditorUtility.SetDirty(mat);
                        converted++;
                        counts[kind] = counts.TryGetValue(kind, out var c) ? c + 1 : 1;
                        string warn = MissingTextureNote(p);
                        report.AppendLine($"OK    {path}  [{from}] -> {mat.shader.name} ({kind}){warn}");
                        if (yaml.Contains(HdrpMaterialDataScriptGuid)) stripPaths.Add(path);
                    }
                    catch (System.Exception e)
                    {
                        failed++;
                        report.AppendLine($"FAIL  {path}  [{from}] {e.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
            }

            // Remove the HDRP per-material MonoBehaviour (missing script in URP) from the saved files.
            int stripped = 0;
            foreach (var path in stripPaths)
            {
                string text = File.ReadAllText(path);
                string cleaned = Regex.Replace(text, @"--- !u!114 &-?\d+\r?\nMonoBehaviour:(?:(?!\n--- ).)*?" + HdrpMaterialDataScriptGuid + @"(?:(?!\n--- ).)*\r?\n?", "", RegexOptions.Singleline);
                if (cleaned != text) { File.WriteAllText(path, cleaned); AssetDatabase.ImportAsset(path); stripped++; }
            }

            var head = new StringBuilder();
            head.AppendLine($"[Xenoarch] Material conversion {System.DateTime.Now:yyyy-MM-dd HH:mm} folder={folder}");
            head.AppendLine($"materials={guids.Length} converted={converted} alreadyURP={skipped} failed={failed} hdrpDataStripped={stripped}");
            foreach (var kv in counts) head.AppendLine($"  {kv.Key}: {kv.Value}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, head.ToString() + report);
            Debug.Log(head.ToString().TrimEnd());
            return (converted, skipped, failed);
        }

        static string Classify(string shaderName, MatYaml p)
        {
            string n = shaderName ?? "";
            if (n.StartsWith("HDRP/Unlit") || (p.Tex("_UnlitColorMap") != null && p.Tex("_BaseColorMap") == null)) return "HDRP Unlit";
            if (n.StartsWith("HDRP/") || p.HasTexSlot("_BaseColorMap") || p.HasTexSlot("_MaskMap")) return "HDRP Lit";
            string l = n.ToLowerInvariant();
            if (l.Contains("glass")) return "Glass";
            if (l.Contains("hologram") || (l.Contains("particle") && (l.Contains("additive") || l.Contains("add"))) || l.Contains("additive")) return "Particles Additive";
            if (l.Contains("particle") || l.Contains("surfaceshader_vc") || l.Contains("alpha blended")) return "Particles Alpha";
            if (l.StartsWith("unlit/") || l.Contains("/unlit")) return "Unlit";
            return "Standard"; // Standard, Standard (Specular), Legacy Shaders/*, Creepy Cat/Planet_*, error shader
        }

        static void ConvertHdrpLit(Material m, Shader lit, MatYaml p)
        {
            m.shader = lit;
            var baseMap = p.Tex("_BaseColorMap");
            SetTex(m, "_BaseMap", baseMap, p.St("_BaseColorMap"));
            m.SetColor("_BaseColor", p.Col("_BaseColor", Color.white));
            var mask = p.Tex("_MaskMap");
            if (mask != null)
            {
                m.SetTexture("_MetallicGlossMap", mask); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetTexture("_OcclusionMap", mask); m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_OcclusionStrength", 1f);
                m.SetFloat("_Smoothness", p.F("_SmoothnessRemapMax", 1f));
            }
            else
            {
                m.SetFloat("_Metallic", p.F("_Metallic", 0f));
                m.SetFloat("_Smoothness", p.F("_Smoothness", 0.5f));
            }
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            var normal = p.Tex("_NormalMap");
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", p.F("_NormalScale", 1f)); m.EnableKeyword("_NORMALMAP"); }
            var emisMap = p.Tex("_EmissiveColorMap");
            Color ldr = p.Col("_EmissiveColorLDR", p.Col("_EmissiveColor", Color.black));
            bool emissive = emisMap != null || ldr.maxColorComponent > 0.001f;
            if (emissive)
            {
                if (emisMap != null && ldr.maxColorComponent < 0.001f) ldr = Color.white;
                SetEmission(m, emisMap, ldr * 2f);
            }
            else m.DisableKeyword("_EMISSION");
            if (p.F("_DoubleSidedEnable", 0f) > 0.5f) m.SetFloat("_Cull", (float)CullMode.Off);
            if (p.F("_AlphaCutoffEnable", 0f) > 0.5f) SetAlphaClip(m, p.F("_AlphaCutoff", 0.5f));
            if (p.F("_SurfaceType", 0f) > 0.5f) SetTransparent(m, additive: false, lit: true);
        }

        static void ConvertStandard(Material m, Shader lit, MatYaml p)
        {
            m.shader = lit;
            SetTex(m, "_BaseMap", p.Tex("_MainTex"), p.St("_MainTex"));
            m.SetColor("_BaseColor", p.Col("_Color", Color.white));
            var mg = p.Tex("_MetallicGlossMap");
            if (mg != null) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", p.F("_GlossMapScale", 1f)); }
            else { m.SetFloat("_Metallic", p.F("_Metallic", 0f)); m.SetFloat("_Smoothness", p.F("_Glossiness", 0.5f)); }
            var n = p.Tex("_BumpMap");
            if (n != null) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", p.F("_BumpScale", 1f)); m.EnableKeyword("_NORMALMAP"); }
            var occ = p.Tex("_OcclusionMap");
            if (occ != null) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", p.F("_OcclusionStrength", 1f)); m.EnableKeyword("_OCCLUSIONMAP"); }
            var em = p.Tex("_EmissionMap");
            var ec = p.Col("_EmissionColor", Color.black);
            if (em != null || ec.maxColorComponent > 0.001f) SetEmission(m, em, ec); else m.DisableKeyword("_EMISSION");
            float mode = p.F("_Mode", 0f); // Standard: 0 opaque, 1 cutout, 2 fade, 3 transparent
            if (mode == 1f) SetAlphaClip(m, p.F("_Cutoff", 0.5f));
            else if (mode >= 2f) SetTransparent(m, false, true);
        }

        static void ConvertGlass(Material m, Shader lit, MatYaml p)
        {
            m.shader = lit;
            SetTex(m, "_BaseMap", p.Tex("_MainTex") ?? p.Tex("_BaseColorMap"), p.St("_MainTex"));
            var c = p.Col("_Color", new Color(0.6f, 0.75f, 0.85f, 0.25f));
            c.a = Mathf.Clamp(c.a, 0.12f, 0.45f);
            m.SetColor("_BaseColor", c);
            var n = p.Tex("_BumpMap"); if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.95f);
            SetTransparent(m, false, true);
        }

        static void ConvertParticles(Material m, Shader particles, MatYaml p, bool additive)
        {
            m.shader = particles;
            var tex = p.Tex("_MainTex") ?? p.Tex("_BaseColorMap");
            SetTex(m, "_BaseMap", tex, p.St("_MainTex"));
            var col = p.Col("_TintColor", p.Col("_Color", Color.white));
            if (p.HasColor("_TintColor")) col *= 2f; // legacy particle shaders double the tint
            m.SetColor("_BaseColor", col);
            SetTransparent(m, additive, lit: false);
        }

        static void ConvertUnlit(Material m, Shader unlit, MatYaml p, string texName, string colName)
        {
            m.shader = unlit;
            SetTex(m, "_BaseMap", p.Tex(texName), p.St(texName));
            m.SetColor("_BaseColor", p.Col(colName, Color.white));
        }

        // ------------------------------------------------------------------ URP state helpers

        static void SetTex(Material m, string prop, Texture t, Vector4 st)
        {
            m.SetTexture(prop, t);
            m.SetTextureScale(prop, new Vector2(st.x, st.y));
            m.SetTextureOffset(prop, new Vector2(st.z, st.w));
            if (prop == "_BaseMap" && m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", t); m.SetTextureScale("_MainTex", new Vector2(st.x, st.y)); }
        }

        static void SetEmission(Material m, Texture map, Color c)
        {
            m.SetTexture("_EmissionMap", map);
            m.SetColor("_EmissionColor", c);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        static void SetAlphaClip(Material m, float cutoff)
        {
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
        }

        static void SetTransparent(Material m, bool additive, bool lit)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.SrcAlpha : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (m.HasProperty("_SrcBlendAlpha")) { m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha)); }
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            if (additive || !lit) m.SetShaderPassEnabled("ShadowCaster", false);
        }

        static string MissingTextureNote(MatYaml p)
        {
            var missing = new List<string>();
            foreach (var kv in p.texGuids)
                if (!string.IsNullOrEmpty(kv.Value) && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(kv.Value)))
                    missing.Add($"{kv.Key}:{kv.Value}");
            return missing.Count == 0 ? "" : "  MISSING TEX " + string.Join(", ", missing);
        }

        /// <summary>Minimal reader for the m_SavedProperties block of a .mat file.</summary>
        sealed class MatYaml
        {
            public readonly Dictionary<string, string> texGuids = new Dictionary<string, string>();
            readonly Dictionary<string, Vector4> st = new Dictionary<string, Vector4>();
            readonly Dictionary<string, float> floats = new Dictionary<string, float>();
            readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
            static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

            public static MatYaml Parse(string yaml)
            {
                var p = new MatYaml();
                foreach (Match m in Regex.Matches(yaml, @"- (\w+):\s*\r?\n\s*m_Texture: \{fileID: (-?\d+)(?:, guid: (\w+))?[^}]*\}\s*\r?\n\s*m_Scale: \{x: ([-\d.eE]+), y: ([-\d.eE]+)\}\s*\r?\n\s*m_Offset: \{x: ([-\d.eE]+), y: ([-\d.eE]+)\}"))
                {
                    string name = m.Groups[1].Value;
                    p.texGuids[name] = m.Groups[2].Value == "0" ? "" : m.Groups[3].Value;
                    p.st[name] = new Vector4(F(m.Groups[4].Value), F(m.Groups[5].Value), F(m.Groups[6].Value), F(m.Groups[7].Value));
                }
                int fi = yaml.IndexOf("m_Floats:");
                int ci = yaml.IndexOf("m_Colors:");
                if (fi >= 0)
                {
                    string block = yaml.Substring(fi, (ci > fi ? ci : yaml.Length) - fi);
                    foreach (Match m in Regex.Matches(block, @"- (\w+): ([-\d.eE]+)\s*$", RegexOptions.Multiline)) p.floats[m.Groups[1].Value] = F(m.Groups[2].Value);
                }
                if (ci >= 0)
                    foreach (Match m in Regex.Matches(yaml.Substring(ci), @"- (\w+): \{r: ([-\d.eE]+), g: ([-\d.eE]+), b: ([-\d.eE]+), a: ([-\d.eE]+)\}"))
                        p.colors[m.Groups[1].Value] = new Color(F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value), F(m.Groups[5].Value));
                return p;
            }

            static float F(string s) => float.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : 0f;

            public bool HasTexSlot(string n) => texGuids.ContainsKey(n);
            public bool HasColor(string n) => colors.ContainsKey(n);
            public Texture Tex(string n)
            {
                if (!texGuids.TryGetValue(n, out var g) || string.IsNullOrEmpty(g)) return null;
                string path = AssetDatabase.GUIDToAssetPath(g);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture>(path);
            }
            public Vector4 St(string n) => st.TryGetValue(n, out var v) ? v : new Vector4(1, 1, 0, 0);
            public float F(string n, float def) => floats.TryGetValue(n, out var v) ? v : def;
            public Color Col(string n, Color def) => colors.TryGetValue(n, out var v) ? v : def;
        }
    }
}

// reload marker
