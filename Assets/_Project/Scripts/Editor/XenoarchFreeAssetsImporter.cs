using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// First-import defaults for the CC0 pack in Assets/!GET_MORE_ASSETS_FOR_FREE/Xenoarch_Free (see its SOURCES.md):
    /// normal maps as Normal Map, roughness / AO / displacement linear, PBR textures max 2048 with mipmaps, UI PNGs as
    /// sprites without mipmaps, audio Vorbis (long engine loops streamed), models without camera/light import.
    /// Only runs when an asset has no import settings yet, so later hand changes are kept.
    /// </summary>
    public sealed class XenoarchFreeAssetsImporter : AssetPostprocessor
    {
        const string Root = "Assets/!GET_MORE_ASSETS_FOR_FREE/Xenoarch_Free/";
        bool Mine => assetPath.StartsWith(Root) && assetImporter.importSettingsMissing;

        void OnPreprocessTexture()
        {
            if (!Mine) return;
            var t = (TextureImporter)assetImporter;
            string p = assetPath.ToLowerInvariant();
            if (p.Contains("/ui/"))
            {
                t.textureType = TextureImporterType.Sprite; t.spriteImportMode = SpriteImportMode.Single;
                t.mipmapEnabled = false; t.alphaIsTransparency = true; t.maxTextureSize = 1024;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            t.maxTextureSize = 2048; t.mipmapEnabled = true; t.textureCompression = TextureImporterCompression.Compressed;
            if (p.Contains("normal") || p.Contains("_nor_")) t.textureType = TextureImporterType.NormalMap;
            else if (p.Contains("rough") || p.Contains("ambientocclusion") || p.Contains("displacement") || p.Contains("_disp_")) t.sRGBTexture = false;
            if (p.EndsWith("preview.png")) { t.maxTextureSize = 512; t.mipmapEnabled = false; }
        }

        void OnPreprocessAudio()
        {
            if (!Mine) return;
            var a = (AudioImporter)assetImporter;
            var s = a.defaultSampleSettings;
            bool loop = assetPath.Contains("engine") || assetPath.Contains("Engine") || assetPath.Contains("forceField");
            s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f;
            s.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            a.defaultSampleSettings = s;
            a.loadInBackground = loop;
        }

        void OnPreprocessModel()
        {
            if (!Mine) return;
            var m = (ModelImporter)assetImporter;
            m.importCameras = false; m.importLights = false; m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }
}
