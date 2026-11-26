using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Base post-processing look (t108u), after ASH's runtime post stack (ASHPostProcessing: a global Volume created at
    /// runtime, camera post switched on, nothing saved into scenes). Tonemapping, gentle bloom, colour adjustments and a
    /// subtle vignette. The editable profile is Data/Rendering/VP_Xenoarch_Base.asset (XenoarchConfig.basePostProfile);
    /// <see cref="BuildDefault"/> is the fallback and what the editor uses to create that asset once.
    /// Per-body extras (environment volumeProfile, fog) stack on top via <see cref="EnvironmentDirector"/>.
    /// </summary>
    public static class XenoarchPostStack
    {
        public static VolumeProfile BuildDefault()
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "VP_Xenoarch_Base";
            var tm = p.Add<Tonemapping>(true);
            tm.mode.value = TonemappingMode.ACES;
            var bl = p.Add<Bloom>(true);
            bl.threshold.value = 1.05f; bl.intensity.value = 0.35f; bl.scatter.value = 0.6f; bl.highQualityFiltering.value = true;
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.15f; ca.contrast.value = 8f; ca.saturation.value = 4f;
            ca.hueShift.overrideState = false; ca.colorFilter.overrideState = false;
            var vg = p.Add<Vignette>(true);
            vg.intensity.value = 0.18f; vg.smoothness.value = 0.45f; vg.rounded.value = false; vg.color.overrideState = false; vg.center.overrideState = false;
            return p;
        }

        /// <summary>Turns on post-processing for the main camera (cameras built by our setup may have it off).</summary>
        public static void EnableCameraPost()
        {
            var c = Camera.main; if (c == null) return;
            var d = c.GetUniversalAdditionalCameraData();
            if (d != null && !d.renderPostProcessing) d.renderPostProcessing = true;
        }
    }
}
