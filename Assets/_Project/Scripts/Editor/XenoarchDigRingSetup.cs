using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Excavation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Dig aim ring assets (Phase 1C batch 3A): soft ring textures + URP decal materials in Resources/FX, and the URP Decal
    /// renderer feature on the project's own renderers (Assets/Settings/*_Renderer.asset). Idempotent.
    /// </summary>
    public static class XenoarchDigRingSetup
    {
        const string Folder = "Assets/_Project/Resources/FX";
        static readonly (string name, Color tint)[] Rings =
        {
            ("DigRing_Valid", new Color(1f, 0.78f, 0.3f, 0.85f)),
            ("DigRing_Invalid", new Color(1f, 0.25f, 0.2f, 0.8f)),
            ("DigRing_Neutral", new Color(1f, 1f, 1f, 0.35f)),
        };

        public static void Run(StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(Folder);
            var shader = Shader.Find("Shader Graphs/Decal");
            if (shader == null) { log.AppendLine("Dig ring: URP 'Shader Graphs/Decal' not found; skipped"); return; }
            foreach (var (name, tint) in Rings)
            {
                string png = $"{Folder}/{name}.png", matPath = $"{Folder}/{name}.mat";
                if (!File.Exists(png))
                {
                    var tex = DigAimRing.RingTexture(tint, 128);
                    File.WriteAllBytes(png, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(png);
                    var ti = (TextureImporter)AssetImporter.GetAtPath(png);
                    ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.sRGBTexture = true; ti.mipmapEnabled = true;
                    ti.SaveAndReimport();
                }
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = shader;
                mat.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(png));
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
            }
            foreach (var path in Renderers())
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;
                if (data.rendererFeatures.Any(f => f is DecalRendererFeature)) { log.AppendLine($"Dig ring: {Path.GetFileName(path)} already has a Decal feature"); continue; }
                var f = ScriptableObject.CreateInstance<DecalRendererFeature>();
                f.name = "Decal";
                AssetDatabase.AddObjectToAsset(f, data);
                data.rendererFeatures.Add(f);
                // keep URP's feature map in step (it pairs each feature with its local file id)
                var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (mapField?.GetValue(data) is System.Collections.Generic.List<long> map && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id))
                    map.Add(id);
                data.SetDirty();
                EditorUtility.SetDirty(data);
                log.AppendLine($"Dig ring: Decal renderer feature added to {Path.GetFileName(path)}");
            }
            AssetDatabase.SaveAssets();
        }

        static string[] Renderers() => AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();

        public static void Validate(System.Action<bool, string> check)
        {
            bool mats = Rings.All(r => Resources.Load<Material>("FX/" + r.name) is Material m && m.GetTexture("Base_Map") != null);
            var rs = Renderers().Select(AssetDatabase.LoadAssetAtPath<UniversalRendererData>).Where(d => d != null).ToList();
            bool decals = rs.Count > 0 && rs.All(d => d.rendererFeatures.Any(f => f is DecalRendererFeature && f.isActive));
            check(mats && decals, $"3A dig ring: decal materials in Resources/FX ({mats}), Decal feature on {rs.Count(d => d.rendererFeatures.Any(f => f is DecalRendererFeature))}/{rs.Count} URP renderers");
        }
    }
}
