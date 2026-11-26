using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using ArtifactDefinition = DMI.Xenoarch.Artifacts.ArtifactDefinition;
using ArtifactRarity = DMI.Xenoarch.Artifacts.ArtifactRarity;
using MaterialDefinition = DMI.Xenoarch.Materials.MaterialDefinition;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Corner accent colour of an item slot (ZEN-22). Derived from the item's category and, for artifacts,
    /// its rarity; both stay separate fields on the definitions (ItemDefinition.category, ArtifactDefinition.rarity).</summary>
    public enum ItemAccent { Common, Materials, Tech, Organic, Rare, Exceptional }

    /// <summary>
    /// Icon style (ZEN-22, t180u): items use the full-colour Xenoarch icon library
    /// (Assets/Xenoarch/UI/Icons, see Docs/XENOARCH_UI_Icons.md), shown untinted. Each item icon gets a separate
    /// corner-accent layer (Icons/Slot/slot_corner_accent.png: thin lower-left L, flush to the slot corner, max 20% alpha,
    /// soft falloff, stretched along the bottom and left edges), tinted per <see cref="ItemAccent"/>. Slot layers: slot
    /// background, icon, accent, then the slot's own selection/hover border. On .xa-slot / .xa-item-slot hosts the accent
    /// is parented to the slot so it sits flush in the frame. Menus, map markers and journal states use .xa-icon--{name}
    /// classes. Images, sizes and colours live in Resources/UI/Xenoarch.uss and XenoarchPalette.uss; this class only adds classes.
    /// </summary>
    public static class XenoarchIcons
    {
        public const string AccentName = "xa-item-accent";
        static StyleSheet _uss;
        static StyleSheet Uss => _uss != null ? _uss : (_uss = Resources.Load<StyleSheet>("UI/Xenoarch"));

        /// <summary>Make sure the Xenoarch stylesheet applies to <paramref name="e"/> (code-built panels may not load it).</summary>
        public static void EnsureStyles(VisualElement e)
        {
            var uss = Uss;
            if (e != null && uss != null && !e.styleSheets.Contains(uss)) e.styleSheets.Add(uss);
        }

        /// <summary>Accent for an item: artifacts by rarity (Rare purple, Exceptional and up gold, else gray), materials and
        /// valuables blue, tools / equipment / parts / ship modules / data teal, everything else gray.</summary>
        public static ItemAccent AccentFor(ItemDefinition d)
        {
            if (d == null) return ItemAccent.Common;
            if (d is ArtifactDefinition a)
                return a.rarity >= ArtifactRarity.Exceptional ? ItemAccent.Exceptional : a.rarity == ArtifactRarity.Rare ? ItemAccent.Rare : ItemAccent.Common;
            if (d is MaterialDefinition) return ItemAccent.Materials;
            switch (d.category)
            {
                case ItemCategory.Resource:
                case ItemCategory.Valuable: return ItemAccent.Materials;
                case ItemCategory.Tool:
                case ItemCategory.Equipment:
                case ItemCategory.ShipModule:
                case ItemCategory.DataRecord:
                case ItemCategory.Misc: return ItemAccent.Tech;
                default: return ItemAccent.Common;
            }
        }

        public static string AccentClass(ItemAccent a) => "xa-item-accent--" + a.ToString().ToLowerInvariant();

        /// <summary>Show <paramref name="d"/>'s icon on <paramref name="img"/> (untinted) with its corner accent layer.</summary>
        public static void ApplyItem(VisualElement img, ItemDefinition d)
        {
            if (img == null || d == null || d.icon == null) return;
            EnsureStyles(img);
            img.style.backgroundImage = new StyleBackground(d.icon);
            img.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            img.AddToClassList("xa-item-icon");
            var accent = img.Q<VisualElement>(AccentName);
            if (accent == null)
            {
                accent = new VisualElement { name = AccentName, pickingMode = PickingMode.Ignore };
                img.Add(accent);
                img.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    var a = img.Q<VisualElement>(AccentName);
                    if (a != null) PlaceAccent(img, a);
                });
            }
            accent.ClearClassList();
            accent.AddToClassList("xa-item-accent");
            accent.AddToClassList(AccentClass(AccentFor(d)));
            PlaceAccent(img, accent);
        }

        /// <summary>Park the accent on the slot frame (.xa-slot / .xa-item-slot) so the L sits flush in the lower-left
        /// corner. Fallback is the icon itself (detail previews, drag ghosts, puzzle pieces).</summary>
        static void PlaceAccent(VisualElement img, VisualElement accent)
        {
            if (img == null || accent == null) return;
            var host = img.parent;
            bool slot = host != null && (host.ClassListContains("xa-slot") || host.ClassListContains("xa-item-slot"));
            if (!slot) host = img;
            if (accent.parent == host) return;
            accent.RemoveFromHierarchy();
            if (host == img) { img.Add(accent); return; }
            int i = host.IndexOf(img);
            if (i >= 0) host.Insert(i + 1, accent);
            else host.Add(accent);
        }

        public static void ApplyItem(VisualElement img, string itemId)
        {
            var s = XenoarchRuntime.Session;
            if (s != null && !string.IsNullOrEmpty(itemId) && s.Items.TryGetItem(itemId, out var d)) ApplyItem(img, d);
        }

        /// <summary>A glyph element: classes xa-icon xa-icon--{name} plus any extra classes. Library icons (names starting
        /// "ui-", "nav-", "ship-", "research-") are full colour and also get xa-icon--pack, which turns the tint off.</summary>
        public static VisualElement Glyph(string name, params string[] classes)
        {
            var g = new VisualElement { pickingMode = PickingMode.Ignore };
            EnsureStyles(g);
            g.AddToClassList("xa-icon");
            g.AddToClassList("xa-icon--" + name);
            if (IsPack(name)) g.AddToClassList("xa-icon--pack");
            foreach (var c in classes) g.AddToClassList(c);
            return g;
        }

        public static bool IsPack(string name) => name != null && (name.StartsWith("ui-") || name.StartsWith("nav-") || name.StartsWith("ship-") || name.StartsWith("research-"));
    }
}
