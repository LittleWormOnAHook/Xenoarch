using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Code-side mirror of Resources/UI/XenoarchPalette.uss (the UI theme, t85u) for UI drawn or styled from C#
    /// (maps, loading screen, HUD widgets). Change both together.
    /// </summary>
    public static class XenoarchPalette
    {
        static Color Hex(int rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        public static readonly Color Panel = Hex(0x2B4E5E);          // panel background
        public static readonly Color PanelDark = Hex(0x1A303A);      // darker shade: lists, insets
        public static readonly Color PanelDeep = Hex(0x0E1B21);      // darkest: backdrops, maps
        public static readonly Color Primary = Hex(0xFFA967);        // primary buttons, headings
        public static readonly Color OnPrimary = Hex(0x1E140E);      // dark text on primary
        public static readonly Color Highlight = Hex(0x68DFD6);      // highlights, selection, scanner, info
        public static readonly Color Secondary = Hex(0x5CA7A8);      // borders, inactive
        public static readonly Color SecondaryText = Hex(0x8BC9C9);  // secondary text (lifted for contrast)
        public static readonly Color Warn = Hex(0xB03B3B);           // warnings, damage, errors (fills)
        public static readonly Color WarnText = Hex(0xE87878);       // warning text (lifted for contrast)
        public static readonly Color Frame = Hex(0x761C27);          // frames, accent borders, danger
        public static readonly Color Text = Hex(0xE4F0F1);

        public static Color A(Color c, float a) { c.a = a; return c; }
    }
}
