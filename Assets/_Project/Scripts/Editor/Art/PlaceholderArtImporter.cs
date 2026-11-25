using UnityEditor;

namespace DMI.Xenoarch.EditorTools.Art
{
    /// <summary>
    /// Import rules for placeholder art copied from Genesis/ASH (Assets/_Project/Art/Placeholder/...):
    /// UI sprites, no mipmaps, clamped. Item icons are capped at 256 px (sources are up to 1024 px).
    /// Only applies on first import so later manual tweaks in the Inspector are kept.
    /// </summary>
    sealed class PlaceholderArtImporter : AssetPostprocessor
    {
        const string Root = "Assets/_Project/Art/Placeholder/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) || !assetImporter.importSettingsMissing) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.maxTextureSize = assetPath.Contains("/Items/") ? 256 : 512;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
