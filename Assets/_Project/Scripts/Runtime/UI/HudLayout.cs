using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Shared HUD regions (t108u), in PanelSettings reference pixels (1920x1080, scale-with-screen, so the same layout
    /// holds at 1080p and 4K). Top-right column: minimap, then the scanner panel stacked under it with a gap. Top-left
    /// and the lower corners are the helmet readout pods; the centre stays clear (crosshair, prompts, dig HUD).
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Safe margin from every screen edge.</summary>
        public const float Margin = 24f;
        /// <summary>Gap between stacked HUD panels.</summary>
        public const float Gap = 14f;
        /// <summary>Bottom edge (px from the top) of whatever occupies the top-right corner (the minimap), 0 = free.</summary>
        public static float TopRightBottom;
        /// <summary>Helmet visor up: fraction of the screen height kept free at the top (pods) / bottom (suit pod).</summary>
        public static float TopInset, BottomInset;
        /// <summary>Reference panel height (PanelSettings 1920x1080).</summary>
        public const float RefHeight = 1080f;
        /// <summary>Top of the top-right column (below the helmet pods when the visor is up).</summary>
        public static float TopStart => Mathf.Max(Margin, TopInset * RefHeight);
        /// <summary>Top for the next panel in the top-right column.</summary>
        public static float NextTopRight => TopRightBottom > 1f ? TopRightBottom + Gap : TopStart;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { TopRightBottom = 0f; TopInset = 0f; BottomInset = 0f; }
    }
}
