using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Sol landing-terrain surfaces (t110u/t111u). Everything here is a one-off editor tool: nothing runs at load or at
    /// runtime, so terrain heights, painted splatmaps and TerrainLayer settings you edit are never overwritten.
    ///
    /// Tools/Xenoarch/Terrain/Apply Sol PBR Surface Layers
    ///   Mars/Moon excavation terrains: replaces the Creepy Cat surface layer (slot 0) with our PBR regolith, the next
    ///   vendor layer with a rock variant, appends a Mars cracked-crust variant, paints rock on slopes and crust patches
    ///   on flats (only the first time, i.e. while slot 0 is still the vendor layer), gives the dig
    ///   soil (Mars_Soil / Moon_Soil) the PBR soil set (only while it still uses {body}_Soil.png), then runs Digger's own "Sync with terrain(s) & Refresh".
    ///   TerrainLayer assets are created once in Data/Terrain/Layers and never overwritten. Scenes with unsaved
    ///   changes are skipped.
    /// Tools/Xenoarch/Terrain/Resync Digger (open scenes)
    ///   Same as the Digger Master inspector button "Sync with terrain(s) & Refresh": run it after you change terrain
    ///   layers, textures or heights.
    /// Textures: Art/Terrain/SolPBR (CC0 sources, see Data/Terrain/SOURCES.md).
    /// </summary>
    static class XenoarchTerrainSurface
    {
        public const string Trigger = "Temp/xenoarch_terrain.trigger";
        public const string ArtDir = "Assets/_Project/Art/Terrain/SolPBR";
        const string LayerDir = "Assets/_Project/Data/Terrain/Layers";
        const string DataDir = "Assets/_Project/Data/Terrain";
        const string Log = "Logs/xenoarch_terrain.txt";

        struct Spec { public string name; public float tile; public float smoothMax; }
        static Spec S(string n, float tile, float smooth) => new Spec { name = n, tile = tile, smoothMax = smooth };

        [MenuItem("Tools/Xenoarch/Terrain/Apply Sol PBR Surface Layers")]
        static void Menu()
        {
            if (!EditorUtility.DisplayDialog("Apply Sol PBR surface layers",
                    "Mars and Moon excavation terrains: swap the Creepy Cat surface layer for the PBR regolith/rock/crust layers " +
                    "(first time only: also paints rock on slopes and crust patches), then Digger Sync & Refresh.\n\n" +
                    "Scenes with unsaved changes are skipped. Already-applied terrains keep their painting.", "Apply", "Cancel")) return;
            Run();
        }

        [MenuItem("Tools/Xenoarch/Terrain/Resync Digger (open scenes)")]
        static void MenuResync()
        {
            var log = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                if (SyncDigger(sc, log)) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sc);
            }
            Debug.Log("[Xenoarch] Digger resync:\n" + log + "Save the scene(s) to keep it.");
        }

        [MenuItem("Tools/Xenoarch/Terrain/Report Terrain Layers")]
        static void MenuReport()
        {
            var log = new StringBuilder();
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
                Describe(t, log);
            Debug.Log("[Xenoarch] Terrain layers:\n" + log);
        }

        static void Describe(Terrain t, StringBuilder log)
        {
            var td = t.terrainData; if (td == null) return;
            log.AppendLine($"{t.gameObject.scene.name}/{t.name}: {AssetDatabase.GetAssetPath(td)} alphamap {td.alphamapResolution}");
            var ls = td.terrainLayers;
            for (int i = 0; i < ls.Length; i++)
                log.AppendLine($"  [{i}] {(ls[i] != null ? ls[i].name + "  " + AssetDatabase.GetAssetPath(ls[i]) + "  tile " + ls[i].tileSize.x : "(missing)")}");
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var log = new StringBuilder();
            log.AppendLine($"{System.DateTime.Now:o}");
            XenoarchPhase1CSetup.WithScene(SceneNames.MarsExcavation01, log, sc => Apply(sc, "Mars", log));
            XenoarchPhase1CSetup.WithScene(SceneNames.MoonExcavation01, log, sc => Apply(sc, "Moon", log));
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Log, log.ToString());
            Debug.Log("[Xenoarch] Sol terrain surfaces:\n" + log);
        }

        static Spec[] Specs(string body) => body == "Mars"
            ? new[] { S("Mars_Regolith", 5f, 0.30f), S("Mars_Rock", 9f, 0.35f), S("Mars_CrackedCrust", 7f, 0.28f) }
            : new[] { S("Moon_Regolith", 5f, 0.25f), S("Moon_Rock", 9f, 0.32f) };

        static bool IsOurs(TerrainLayer l) => l != null && AssetDatabase.GetAssetPath(l).StartsWith("Assets/_Project/");

        static bool Apply(Scene scene, string body, StringBuilder log)
        {
            var terrains = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true))
                .Where(t => t.terrainData != null && AssetDatabase.GetAssetPath(t.terrainData).StartsWith(DataDir)).ToList();
            if (terrains.Count == 0) { log.AppendLine($"{scene.name}: no Xenoarch terrain, skipped"); return false; }
            var specs = Specs(body);
            var made = specs.Select(EnsureLayer).ToArray();
            if (made.Any(l => l == null)) { log.AppendLine($"{scene.name}: textures missing in {ArtDir}, skipped"); return false; }
            bool changed = false;
            foreach (var t in terrains)
            {
                var td = t.terrainData;
                var layers = td.terrainLayers.ToList();
                var baseL = made[0]; var rockL = made[1]; var crustL = made.Length > 2 ? made[2] : null;
                bool first = layers.Count > 0 && !IsOurs(layers[0]);
                if (!first && layers.Contains(baseL)) { log.AppendLine($"{scene.name}/{t.name}: PBR surface already applied, layers/painting kept"); }
                else if (!first) { log.AppendLine($"{scene.name}/{t.name}: slot 0 is '{(layers.Count > 0 ? layers[0]?.name : "none")}' (not vendor), left alone"); }
                else
                {
                    int w = td.alphamapWidth, h = td.alphamapHeight;
                    var a = td.GetAlphamaps(0, 0, w, h);
                    int nOld = a.GetLength(2);
                    layers[0] = baseL;
                    int rockIdx = -1;
                    for (int i = 1; i < layers.Count; i++) if (!IsOurs(layers[i])) { layers[i] = rockL; rockIdx = i; break; }
                    if (rockIdx < 0) { layers.Add(rockL); rockIdx = layers.Count - 1; }
                    int crustIdx = -1;
                    if (crustL != null && !layers.Contains(crustL) && layers.Count < 8) { layers.Add(crustL); crustIdx = layers.Count - 1; }
                    int n = layers.Count;
                    var b = new float[h, w, n];
                    int seed = body.Length * 131;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            for (int l = 0; l < nOld && l < n; l++) b[y, x, l] = a[y, x, l];
                            float u = x / (float)(w - 1), v = y / (float)(h - 1);
                            float steep = td.GetSteepness(u, v);
                            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 34f, steep));
                            float w0 = b[y, x, 0];
                            float toRock = w0 * rock;
                            b[y, x, 0] -= toRock; b[y, x, rockIdx] += toRock;
                            if (crustIdx >= 0)
                            {
                                float nz = Mathf.PerlinNoise(seed + u * 9f, seed + v * 9f) * 0.7f + Mathf.PerlinNoise(seed * 2 + u * 31f, v * 31f) * 0.3f;
                                float crust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.68f, nz)) * (1f - rock) * 0.85f;
                                float toCrust = b[y, x, 0] * crust;
                                b[y, x, 0] -= toCrust; b[y, x, crustIdx] += toCrust;
                            }
                        }
                    td.terrainLayers = layers.ToArray();
                    td.SetAlphamaps(0, 0, b);
                    EditorUtility.SetDirty(td);
                    log.AppendLine($"{scene.name}/{t.name}: slot 0 -> {baseL.name}, slot {rockIdx} -> {rockL.name} (slopes 20-34 deg)" +
                                   (crustIdx >= 0 ? $", slot {crustIdx} -> {crustL.name} (noise patches)" : "") + $"; {n} layers");
                    changed = true;
                }
                changed |= UpgradeSoil(body, log);
                Describe(t, log);
            }
            changed |= SyncDigger(scene, log);
            return changed;
        }

        /// <summary>Dig-wall soil layer gets the PBR soil set (a different texture from the surface regolith so dug walls read clearly), only while it still uses {body}_Soil.png.</summary>
        static bool UpgradeSoil(string body, StringBuilder log)
        {
            var soil = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{DataDir}/{body}_Soil.terrainlayer");
            if (soil == null) return false;
            string gen = $"{DataDir}/{body}_Soil.png";
            if (soil.diffuseTexture != null && AssetDatabase.GetAssetPath(soil.diffuseTexture) != gen) return false;
            string n = $"{body}_SoilPBR";
            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{n}/{n}_Albedo.jpg");
            if (alb == null) return false;
            soil.diffuseTexture = alb;
            soil.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{n}/{n}_Normal.png");
            soil.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{n}/{n}_Mask.png");
            soil.maskMapRemapMin = Vector4.zero; soil.maskMapRemapMax = new Vector4(0f, 1f, 1f, 0.2f);
            soil.tileSize = new Vector2(2.5f, 2.5f); soil.normalScale = 1f;
            EditorUtility.SetDirty(soil);
            log.AppendLine($"{body}_Soil (dig walls): PBR soil textures assigned");
            return true;
        }

        static TerrainLayer EnsureLayer(Spec s)
        {
            string path = $"{LayerDir}/TL_{s.name}.terrainlayer";
            var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (existing != null) return existing; // never overwrite: you may have tuned it
            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{s.name}/{s.name}_Albedo.jpg");
            if (alb == null) return null;
            Directory.CreateDirectory(LayerDir);
            var l = new TerrainLayer
            {
                name = $"TL_{s.name}",
                diffuseTexture = alb,
                normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{s.name}/{s.name}_Normal.png"),
                maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{s.name}/{s.name}_Mask.png"),
                tileSize = new Vector2(s.tile, s.tile),
                normalScale = 1f,
                maskMapRemapMin = Vector4.zero,
                maskMapRemapMax = new Vector4(0f, 1f, 1f, s.smoothMax), // R metallic 0, G AO, B height, A smoothness
            };
            AssetDatabase.CreateAsset(l, path);
            return l;
        }

        /// <summary>Digger's own sync (what the Digger Master button "Sync with terrain(s) &amp; Refresh" does), per scene.</summary>
        static bool SyncDigger(Scene scene, StringBuilder log)
        {
            var sysT = FindType("Digger.Modules.Core.Sources.DiggerSystem");
            var edT = FindType("Digger.Modules.Core.Editor.DiggerSystemEditor");
            var init = edT?.GetMethod("Init", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { sysT, typeof(bool) }, null);
            if (sysT == null || init == null) { log.AppendLine($"{scene.name}: Digger not found, sync skipped"); return false; }
            var systems = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren(sysT, true)).ToList();
            if (systems.Count == 0) { log.AppendLine($"{scene.name}: no DiggerSystem, sync skipped"); return false; }
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var d in systems) init.Invoke(null, new object[] { d, true });
            }
            finally { AssetDatabase.StopAssetEditing(); }
            log.AppendLine($"{scene.name}: Digger Sync & Refresh ({systems.Count} system(s))");
            return true;
        }

        static System.Type FindType(string fullName) =>
            System.AppDomain.CurrentDomain.GetAssemblies().Select(asm => asm.GetType(fullName)).FirstOrDefault(t => t != null);
    }

    /// <summary>Import settings for Art/Terrain/SolPBR on first import only (later changes in the inspector are kept).</summary>
    sealed class XenoarchSolPbrImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(XenoarchTerrainSurface.ArtDir) || !assetImporter.importSettingsMissing) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 2048; ti.anisoLevel = 4; ti.mipmapEnabled = true;
            if (assetPath.EndsWith("_Normal.png")) ti.textureType = TextureImporterType.NormalMap;
            else if (assetPath.EndsWith("_Mask.png")) { ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; }
            else ti.sRGBTexture = true;
        }
    }
}
