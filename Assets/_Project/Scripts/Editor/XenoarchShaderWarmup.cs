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
        public const string Trigger = "Temp/xenoarch_shaderwarm.trigger";

        [MenuItem("Tools/Xenoarch/Rendering/Rebuild Shader Warmup Cache")]
        static void RebuildMenu()
        {
            var log = new StringBuilder();
            Run(log);
            Debug.Log("[Xenoarch] " + log.ToString().TrimEnd());
        }

        // URP global keyword combinations the scene turns on at draw time (main-light cascade shadows, soft shadows,
        // fog, additional lights). Only the ones a shader declares are used; invalid combos are skipped.
        static readonly string[][] GlobalCombos =
        {
            new string[0],
            new[] { "_MAIN_LIGHT_SHADOWS_CASCADE" },
            new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT" },
            new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "FOG_EXP2" },
            new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "FOG_LINEAR" },
            new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "_ADDITIONAL_LIGHTS" },
            new[] { "FOG_EXP2" }, new[] { "FOG_LINEAR" },
        };

        // Shaders the game builds materials from in code (site markers / borders / flags, soil, artifacts, resource
        // nodes, FTL particles, sun disc, dig ring decal) with the keyword sets that code enables.
        static readonly (string shader, string[] kw)[] RuntimeShaders =
        {
            ("Universal Render Pipeline/Lit", new string[0]),
            ("Universal Render Pipeline/Lit", new[] { "_EMISSION" }),
            ("Universal Render Pipeline/Unlit", new string[0]),
            ("Universal Render Pipeline/Simple Lit", new string[0]),
            ("Universal Render Pipeline/Particles/Unlit", new string[0]),
            ("Universal Render Pipeline/Particles/Unlit", new[] { "_SURFACE_TYPE_TRANSPARENT", "_BLENDMODE_ADD" }),
            ("Xenoarch/SunDisc", new string[0]),
            ("Shader Graphs/Decal", new string[0]),
        };
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
            // everything Resources / project prefabs / definition assets can spawn mid-game (shuttle, caches, pickups, VFX)
            foreach (var folder in new[] { "Assets/_Project/Resources", "Assets/_Project/Prefabs", "Assets/_Project/Data" })
                if (AssetDatabase.IsValidFolder(folder))
                    foreach (var g in AssetDatabase.FindAssets("t:Prefab t:ScriptableObject", new[] { folder }))
                        foreach (var dep in AssetDatabase.GetDependencies(AssetDatabase.GUIDToAssetPath(g), true))
                            if (dep.EndsWith(".mat")) { var m = AssetDatabase.LoadAssetAtPath<Material>(dep); if (m != null) mats.Add(m); }
            var temp = new List<Material>();
            foreach (var (shaderName, kw) in RuntimeShaders)
            {
                var sh = Shader.Find(shaderName); if (sh == null) continue;
                var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, shaderKeywords = kw };
                temp.Add(m); mats.Add(m);
            }

            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetPath);
            if (svc == null) { svc = new ShaderVariantCollection(); AssetDatabase.CreateAsset(svc, AssetPath); }
            svc.Clear();
            int added = 0, skipped = 0;
            foreach (var m in mats)
            {
                if (m.shader == null || !m.shader.isSupported) { skipped++; continue; }
                var declared = new HashSet<string>(m.shader.keywordSpace.keywordNames);
                var valid = m.shaderKeywords.Where(declared.Contains).ToArray();
                foreach (var pass in Passes)
                {
                    foreach (var kw in new[] { m.shaderKeywords, valid, new string[0] })
                    {
                        // try the material's keywords, then only the ones the shader declares, then the base variant
                        try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(m.shader, pass, kw))) added++; break; }
                        catch (System.ArgumentException) { }
                    }
                    if (pass == PassType.ShadowCaster) continue;
                    // plus the scene-lighting variants actually drawn at runtime
                    foreach (var combo in GlobalCombos)
                    {
                        if (combo.Length == 0) continue;
                        var extra = combo.Where(declared.Contains).ToArray();
                        if (extra.Length == 0) continue;
                        try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(m.shader, pass, valid.Concat(extra).Distinct().ToArray()))) added++; }
                        catch (System.ArgumentException) { }
                    }
                }
            }
            foreach (var t in temp) Object.DestroyImmediate(t);
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
