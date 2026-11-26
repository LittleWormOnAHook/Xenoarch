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

        public static readonly Color Panel = Hex(0x761C27);          // panel / window background (t89u)
        public static readonly Color PanelDark = Hex(0x4E121A);      // darker shade: lists, insets
        public static readonly Color PanelDeep = Hex(0x28090E);      // darkest: backdrops, maps
        public static readonly Color Primary = Hex(0xFFA967);        // titles, headings, uppercase labels
        public static readonly Color Heading = Primary;
        public static readonly Color Bar = Hex(0xFFA967);            // sliders, progress bars
        public static readonly Color Button = Hex(0x68DFD6);         // buttons
        public static readonly Color OnButton = Hex(0x102022);       // dark text on buttons
        public static readonly Color OnPrimary = OnButton;
        public static readonly Color Highlight = Hex(0x68DFD6);      // selection, scanner, info
        public static readonly Color Secondary = Hex(0x5CA7A8);      // inactive
        public static readonly Color SecondaryText = Hex(0xE8E2D0, 0.72f); // dimmed bone white
        public static readonly Color Warn = Hex(0xB03B3B);           // warnings, damage, errors (fills)
        public static readonly Color WarnText = Hex(0xFF968C);       // warning text (lifted for contrast on crimson)
        public static readonly Color Frame = Hex(0xAA404E);          // frame border (lighter crimson, shows on the panel)
        public static readonly Color Text = Hex(0xE8E2D0);           // bone white

        public static Color A(Color c, float a) { c.a = a; return c; }
    }
}
