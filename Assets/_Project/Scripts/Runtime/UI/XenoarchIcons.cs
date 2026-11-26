using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using MaterialDefinition = DMI.Xenoarch.Materials.MaterialDefinition;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// The one icon style (t151u-t155u, approved mockups in Docs/XENOARCH_UI_Icons.md):
    /// items are flat white silhouettes (Art/Icons, glyph padded to 78% of a square canvas) tinted by category;
    /// menus, map markers and journal states use Kenney Game Icons (White 2x). Images, sizes and tints live in
    /// Resources/UI/Xenoarch.uss (classes .xa-icon / .xa-item-icon / .xa-map-icon / .xa-journal-icon) and
    /// XenoarchPalette.uss (--xa-icon-* variables); this class only adds the classes.
    /// </summary>
    public static class XenoarchIcons
    {
        static StyleSheet _uss;
        static StyleSheet Uss => _uss != null ? _uss : (_uss = Resources.Load<StyleSheet>("UI/Xenoarch"));

        /// <summary>Make sure the Xenoarch stylesheet applies to <paramref name="e"/> (code-built panels may not load it).</summary>
        public static void EnsureStyles(VisualElement e)
        {
            var uss = Uss;
            if (e != null && uss != null && !e.styleSheets.Contains(uss)) e.styleSheets.Add(uss);
        }

        /// <summary>Category modifier: tools/other bone white, artifacts orange, parts teal, materials light material colour.</summary>
        public static string CategoryClass(ItemDefinition d)
        {
            if (d is MaterialDefinition) return "xa-item-icon--material";
            if (d == null) return "xa-item-icon--tool";
            switch (d.category)
            {
                case ItemCategory.Artifact: return "xa-item-icon--artifact";
                case ItemCategory.Misc: return "xa-item-icon--part";
                default: return "xa-item-icon--tool";
            }
        }

        /// <summary>Show <paramref name="d"/>'s icon on <paramref name="img"/> in the shared item style.</summary>
        public static void ApplyItem(VisualElement img, ItemDefinition d)
        {
            if (img == null || d == null || d.icon == null) return;
            EnsureStyles(img);
            img.style.backgroundImage = new StyleBackground(d.icon);
            img.AddToClassList("xa-item-icon");
            img.AddToClassList(CategoryClass(d));
            // Material tint is per item (its own colour), so it is the one inline value: light material colour over bone white.
            if (d is MaterialDefinition m) img.style.unityBackgroundImageTintColor = Color.Lerp(XenoarchPalette.Text, m.tint, 0.35f);
        }

        public static void ApplyItem(VisualElement img, string itemId)
        {
            var s = XenoarchRuntime.Session;
            if (s != null && !string.IsNullOrEmpty(itemId) && s.Items.TryGetItem(itemId, out var d)) ApplyItem(img, d);
        }

        /// <summary>A Kenney glyph element: classes xa-icon xa-icon--{name} plus any extra classes.</summary>
        public static VisualElement Glyph(string name, params string[] classes)
        {
            var g = new VisualElement { pickingMode = PickingMode.Ignore };
            EnsureStyles(g);
            g.AddToClassList("xa-icon");
            g.AddToClassList("xa-icon--" + name);
            foreach (var c in classes) g.AddToClassList(c);
            return g;
        }
    }
}
