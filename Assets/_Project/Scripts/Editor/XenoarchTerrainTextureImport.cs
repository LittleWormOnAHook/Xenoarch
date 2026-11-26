using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// CC0 soil textures (Data/Terrain, see SOURCES.md): imports the shared soil normal map as a normal map and assigns it
    /// to the Mars_Soil / Moon_Soil terrain layers when they have none. Never touches vendor terrain layers; skipped while playing.
    /// </summary>
    [InitializeOnLoad]
    sealed class XenoarchTerrainTextureImport : AssetPostprocessor
    {
        const string Normal = "Assets/_Project/Data/Terrain/Terrain_Soil_Normal.png";
        static readonly string[] Layers = { "Assets/_Project/Data/Terrain/Mars_Soil.terrainlayer", "Assets/_Project/Data/Terrain/Moon_Soil.terrainlayer" };

        static XenoarchTerrainTextureImport() => EditorApplication.delayCall += AssignNormals;

        void OnPreprocessTexture()
        {
            if (assetPath != Normal) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.NormalMap;
            ti.maxTextureSize = 2048;
        }

        [MenuItem("Xenoarch/Terrain/Assign Soil Normal Maps")]
        static void AssignNormals()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(Normal);
            if (n == null) return;
            bool any = false;
            foreach (var path in Layers)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null || l.normalMapTexture != null) continue;
                l.normalMapTexture = n; l.normalScale = 1f;
                EditorUtility.SetDirty(l); any = true;
                Debug.Log($"[Xenoarch] Soil normal map assigned to {path}");
            }
            if (any) AssetDatabase.SaveAssets();
        }
    }
}
