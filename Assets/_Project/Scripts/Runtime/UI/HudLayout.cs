using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Shared HUD regions (t108u), in PanelSettings reference pixels (1920x1080, scale-with-screen, so the same layout
    /// holds at 1080p and 4K). Top-right column: minimap, then the scanner panel stacked under it with a gap. Top-left
    /// and the lower corners are the helmet readout pods; the centre stays clear (crosshair, prompts, dig HUD).
    /// t170u: when the helmet visor is up, the hotbar splits 4+4 into the dark lower corners and the minimap sits
    /// mid-right inside the glass; the scanner reading panel stays under the top-right pods (it does not follow the minimap).
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Safe margin from every screen edge.</summary>
        public const float Margin = 24f;
        /// <summary>Top-right column (minimap, scanner) margin from the top and right edges (t135u: tight to the corner).</summary>
        public const float CornerMargin = 20f;
        /// <summary>Gap between stacked HUD panels.</summary>
        public const float Gap = 14f;
        /// <summary>Bottom edge (px from the top) of whatever occupies the top-right corner (the minimap), 0 = free.</summary>
        public static float TopRightBottom;
        /// <summary>Helmet visor up: fraction of the screen height kept free at the top (pods) / bottom (suit pod).</summary>
        public static float TopInset, BottomInset;
        /// <summary>Reference panel size (PanelSettings 1920x1080).</summary>
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;
        /// <summary>Top of the top-right column (below the helmet pods when the visor is up).</summary>
        public static float TopStart => TopInset > 0f ? TopInset * RefHeight + Gap : CornerMargin;
        /// <summary>Top for the scanner reading panel. Stays under the helmet pods; does not follow the mid-height minimap.</summary>
        public static float ScannerTop => TopStart;
        /// <summary>Top of the minimap. Mid-right inside the visor when the helmet is on (t169u/t170u).</summary>
        public static float MinimapTop => HelmetHud.Visible ? RefHeight * 0.40f : TopStart;
        /// <summary>Right margin of the minimap (a little extra when the helmet is on so it clears the thin side frame).</summary>
        public static float MinimapRight => HelmetHud.Visible ? 28f : CornerMargin;
        /// <summary>Top for the next panel in the top-right column (kept for any leftover callers).</summary>
        public static float NextTopRight => TopRightBottom > 1f ? TopRightBottom + Gap : TopStart;

        /// <summary>Width of the lower-left (and, symmetrically, lower-right) dark exterior, as a fraction of screen
        /// width, at a given height-from-the-bottom (fraction of screen height). Measured from Helmet_Visor.png.</summary>
        public static float CornerWidthFrac(float heightFromBottomFrac)
        {
            // (height-from-bottom / texture H, width / texture W) along the inner side of the lower-left exterior.
            float[] h = { 0f, 0.0212f, 0.0425f, 0.0638f, 0.0850f, 0.1063f, 0.1275f, 0.1488f, 0.1700f, 0.1913f, 0.2125f, 0.2338f, 0.2550f, 0.2763f };
            float[] w = { 0.4007f, 0.3278f, 0.2506f, 0.2033f, 0.1681f, 0.1411f, 0.1196f, 0.1029f, 0.0879f, 0.0748f, 0.0634f, 0.0526f, 0.0425f, 0.0335f };
            float t = Mathf.Clamp(heightFromBottomFrac, 0f, h[h.Length - 1]);
            for (int i = 1; i < h.Length; i++)
            {
                if (t <= h[i])
                {
                    float u = (t - h[i - 1]) / (h[i] - h[i - 1]);
                    return Mathf.Lerp(w[i - 1], w[i], u);
                }
            }
            return w[w.Length - 1];
        }

        public struct CornerHotbar
        {
            public float slot, gap, padEdge, padFrame;
            public bool showNames;
            public float xLeft, xRight, y, width, height;
            public float hintX, hintY, hintMaxWidth;
        }

        /// <summary>4+4 hotbar geometry in reference pixels, packed into the dark lower corners of the visor.
        /// Names are kept only when a readable size still fits inside the crescent.</summary>
        public static CornerHotbar HelmetHotbar()
        {
            const float padE = 8f, padF = 12f, gap = 5f;
            const float minNamed = 52f, minIcon = 48f, maxSlot = 76f;
            bool names = true;
            float slot = FitSlot(true, padE, padF, gap, minNamed, maxSlot);
            if (slot < minNamed)
            {
                names = false;
                slot = FitSlot(false, padE, padF, gap, minIcon, maxSlot);
            }
            float nameH = names ? Mathf.Round(slot * 0.40f) : 0f;
            float width = 4f * slot + 3f * gap;
            float height = slot + nameH;
            float y = RefHeight - padE - height;
            float xLeft = padE;
            float xRight = RefWidth - padE - width;
            // Hints sit in the wide bottom of the left crescent, to the right of the left cluster.
            float hintY = RefHeight - padE - 32f;
            float hintX = xLeft + width + 10f;
            float avail = CornerWidthFrac((RefHeight - hintY) / RefHeight) * RefWidth - padF;
            return new CornerHotbar
            {
                slot = slot, gap = gap, padEdge = padE, padFrame = padF, showNames = names,
                xLeft = xLeft, xRight = xRight, y = y, width = width, height = height,
                hintX = hintX, hintY = hintY, hintMaxWidth = Mathf.Max(40f, avail - hintX)
            };
        }

        static float FitSlot(bool names, float padE, float padF, float gap, float min, float max)
        {
            float best = min;
            for (float s = max; s >= min; s -= 1f)
            {
                float nameH = names ? Mathf.Round(s * 0.40f) : 0f;
                float width = 4f * s + 3f * gap;
                float height = s + nameH;
                float fromBottom = padE + height;
                float avail = CornerWidthFrac(fromBottom / RefHeight) * RefWidth - padE - padF;
                if (width <= avail) { best = s; break; }
            }
            return best;
        }

        /// <summary>Bottom of the detector display, px from the panel bottom. Clears the suit pod when the visor is up,
        /// and the normal hotbar when it is down.</summary>
        public static float DetectorBottom => HelmetHud.Visible ? 0.128f * RefHeight : 150f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { TopRightBottom = 0f; TopInset = 0f; BottomInset = 0f; }
    }
}
