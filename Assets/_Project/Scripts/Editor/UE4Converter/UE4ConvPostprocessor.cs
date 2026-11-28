using System.IO;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>
    /// Applies the converter's import settings on first import (single import, no double-import). It only acts on paths
    /// registered by a running conversion, so it is inert for every other asset in the project.
    /// </summary>
    sealed class UE4ConvPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (Registry.Tex.Count == 0 || !Registry.Tex.TryGetValue(assetPath, out var job)) return;
            var ti = (TextureImporter)assetImporter;
            var o = Registry.Opts ?? new ConverterOptions();
            if (job.readTemp)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false; ti.isReadable = true; ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 16384; ti.npotScale = TextureImporterNPOTScale.None;
                return;
            }
            ti.maxTextureSize = o.maxTextureSize;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.isReadable = false;
            switch (job.role)
            {
                case TexRole.Normal:
                    ti.textureType = TextureImporterType.NormalMap;
                    break;
                case TexRole.BaseColor:
                case TexRole.Emissive:
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.alphaSource = job.alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                    ti.alphaIsTransparency = job.alpha && job.role == TexRole.BaseColor;
                    if (job.cutout) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; }
                    break;
                default:   // metallic/smoothness, occlusion, height: linear data
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = false;
                    ti.alphaSource = job.metalSmooth ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                    ti.alphaIsTransparency = false;
                    break;
            }
        }

        void OnPreprocessAudio()
        {
            if (Registry.Audio.Count == 0 || !Registry.Audio.TryGetValue(assetPath, out var job)) return;
            var ai = (AudioImporter)assetImporter;
            var st = ai.defaultSampleSettings;
            st.loadType = job.load;
            st.compressionFormat = AudioCompressionFormat.Vorbis;
            st.quality = 0.7f;
            st.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = st;
            st.preloadAudioData = job.load != AudioClipLoadType.Streaming;
            ai.defaultSampleSettings = st;
            ai.loadInBackground = job.load == AudioClipLoadType.Streaming || job.seconds > 8f;
        }

        void OnPreprocessModel()
        {
            if (Registry.Models.Count == 0 || !Registry.Models.TryGetValue(assetPath, out var job)) return;
            var mi = (ModelImporter)assetImporter;
            var o = Registry.Opts ?? new ConverterOptions();

            mi.globalScale = job.globalScale;
            mi.useFileScale = job.useFileScale;
            mi.bakeAxisConversion = o.bakeAxis;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = false;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            mi.weldVertices = true;
            mi.generateSecondaryUV = o.lightmapUVs;
            mi.importCameras = false;
            mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;

            if (job.kind == MeshKind.Static || job.isObj)
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
            else
            {
                mi.animationType = job.humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                mi.importAnimation = job.hasAnim && o.importAnimations;
                if (job.stage == 1) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
        }

        void OnPreprocessAnimation()
        {
            if (Registry.Models.Count == 0 || !Registry.Models.TryGetValue(assetPath, out var job)) return;
            if (job.stage != 1 || !job.hasAnim) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                string take = c.takeName ?? c.name;
                bool generic = take.StartsWith("Take", System.StringComparison.OrdinalIgnoreCase) || take.StartsWith("mixamo", System.StringComparison.OrdinalIgnoreCase)
                               || take.StartsWith("Default", System.StringComparison.OrdinalIgnoreCase) || take.Contains("|");
                if (clips.Length == 1 || generic) c.name = clips.Length == 1 ? job.stem : job.stem + "_" + (i + 1);
                bool loop = UE4Names.LoopRx.IsMatch(job.stem) || UE4Names.LoopRx.IsMatch(c.name);
                c.loopTime = loop; c.loopPose = loop;
                clips[i] = c;
            }
            mi.clipAnimations = clips;
        }
    }
}