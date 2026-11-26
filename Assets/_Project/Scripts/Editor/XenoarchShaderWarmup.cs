using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Builds Resources/Shaders/XenoarchVariants.shadervariants from the materials the planet and ship scenes use (plus the
    /// Digger-generated terrain materials in Assets/DiggerData). The SceneLoader warms it up behind the loading screen in
    /// builds, so the first frames on a planet don't hitch on shader compiles. (In the Editor the loading screen instead
    /// waits for async shader compilation to finish.)
    /// </summary>
    public static class XenoarchShaderWarmup
    {
        const string Folder = "Assets/_Project/Resources/Shaders";
        const string AssetPath = Folder + "/XenoarchVariants.shadervariants";

        static readonly PassType[] Passes = { PassType.ScriptableRenderPipeline, PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ShadowCaster };

        public static void Run(StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(Folder);
            var mats = new HashSet<Material>();
            foreach (var scene in new[] { SceneNames.ShipStarter, SceneNames.MoonExcavation01, SceneNames.MarsExcavation01, SceneNames.TradeHubSol })
            {
                string path = XenoarchSetup.ScenePath(scene);
                if (!File.Exists(path)) continue;
                foreach (var dep in AssetDatabase.GetDependencies(path, true))
                    if (dep.EndsWith(".mat")) { var m = AssetDatabase.LoadAssetAtPath<Material>(dep); if (m != null) mats.Add(m); }
            }
            if (AssetDatabase.IsValidFolder("Assets/DiggerData"))
                foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/DiggerData" }))
                { var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)); if (m != null) mats.Add(m); }
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Resources" }))
            { var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)); if (m != null) mats.Add(m); }

            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetPath);
            if (svc == null) { svc = new ShaderVariantCollection(); AssetDatabase.CreateAsset(svc, AssetPath); }
            svc.Clear();
            int added = 0, skipped = 0;
            foreach (var m in mats)
            {
                if (m.shader == null || !m.shader.isSupported) { skipped++; continue; }
                var valid = m.shaderKeywords.Where(k => m.shader.keywordSpace.keywordNames.Contains(k)).ToArray();
                foreach (var pass in Passes)
                    foreach (var kw in new[] { m.shaderKeywords, valid, new string[0] })
                    {
                        // try the material's keywords, then only the ones the shader declares, then the base variant
                        try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(m.shader, pass, kw))) added++; break; }
                        catch (System.ArgumentException) { }
                    }
            }
            EditorUtility.SetDirty(svc);
            AssetDatabase.SaveAssets();
            log.AppendLine($"Shader warm-up: {svc.shaderCount} shaders / {svc.variantCount} variants from {mats.Count} materials ({skipped} unsupported skipped) -> {AssetPath}");
        }

        public static void Validate(System.Action<bool, string> check)
        {
            var svc = Resources.Load<ShaderVariantCollection>("Shaders/XenoarchVariants");
            check(svc != null && svc.shaderCount >= 3 && svc.variantCount >= svc.shaderCount, $"3A shader warm-up collection present ({(svc != null ? svc.shaderCount : 0)} shaders, {(svc != null ? svc.variantCount : 0)} variants)");
        }
    }
}
