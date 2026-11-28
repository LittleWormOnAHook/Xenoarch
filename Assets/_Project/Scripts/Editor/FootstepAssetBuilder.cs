using DMI.Xenoarch.Footsteps;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Creates or refreshes Assets/_Project/Resources/Footsteps/XA_FootstepSurfaceMap.asset
    /// and wires the boot-print textures after import. Safe to run repeatedly; does not overwrite
    /// already-assigned clip or texture references.
    /// </summary>
    [InitializeOnLoad]
    public static class FootstepAssetBuilder
    {
        const string AssetPath = "Assets/_Project/Resources/Footsteps/XA_FootstepSurfaceMap.asset";
        const string ArtDir = "Assets/_Project/Art/Decals/Footprints";

        static FootstepAssetBuilder()
        {
            EditorApplication.delayCall += Ensure;
        }

        [MenuItem("Xenoarch/Footsteps/Refresh Surface Map")]
        public static void RefreshMenu() => Ensure();

        static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var map = AssetDatabase.LoadAssetAtPath<FootstepSurfaceMap>(AssetPath);
            bool created = false;
            if (map == null)
            {
                var folder = "Assets/_Project/Resources/Footsteps";
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources"))
                    AssetDatabase.CreateFolder("Assets/_Project", "Resources");
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder("Assets/_Project/Resources", "Footsteps");
                map = ScriptableObject.CreateInstance<FootstepSurfaceMap>();
                AssetDatabase.CreateAsset(map, AssetPath);
                created = true;
            }

            map.EnsureDefaults();
            if (map.printAlbedoLeft == null)
                map.printAlbedoLeft = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Footprint_Boot_L.png");
            if (map.printAlbedoRight == null)
                map.printAlbedoRight = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Footprint_Boot_R.png");
            if (map.printNormalLeft == null)
                map.printNormalLeft = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Footprint_Boot_L_N.png");
            if (map.printNormalRight == null)
                map.printNormalRight = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/Footprint_Boot_R_N.png");

            EditorUtility.SetDirty(map);
            if (created) AssetDatabase.SaveAssets();

            FixTexture(ArtDir + "/Footprint_Boot_L.png", normal: false);
            FixTexture(ArtDir + "/Footprint_Boot_R.png", normal: false);
            FixTexture(ArtDir + "/Footprint_Boot_L_N.png", normal: true);
            FixTexture(ArtDir + "/Footprint_Boot_R_N.png", normal: true);
            FixTexture("Assets/_Project/Resources/Footsteps/Footprint_Boot_L.png", normal: false);
            FixTexture("Assets/_Project/Resources/Footsteps/Footprint_Boot_R.png", normal: false);
            FixTexture("Assets/_Project/Resources/Footsteps/Footprint_Boot_L_N.png", normal: true);
            FixTexture("Assets/_Project/Resources/Footsteps/Footprint_Boot_R_N.png", normal: true);
        }

        static void FixTexture(string path, bool normal)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            bool dirty = false;
            var wantType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType != wantType) { imp.textureType = wantType; dirty = true; }
            if (imp.wrapMode != TextureWrapMode.Clamp) { imp.wrapMode = TextureWrapMode.Clamp; dirty = true; }
            if (!normal && !imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
            if (imp.sRGBTexture == normal) { imp.sRGBTexture = !normal; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }
    }
}
