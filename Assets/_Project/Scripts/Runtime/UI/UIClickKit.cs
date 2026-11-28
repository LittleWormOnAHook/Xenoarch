using DMI.Xenoarch.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Click robustness shared by every UI Toolkit document (attached from <see cref="XenoarchUI.FillRoot"/>):
    /// 1) decorative children of buttons / slots / rows never take picking, so the whole control is one hit area and
    ///    :hover lands on the control itself (right-click registers on the full element too);
    /// 2) <see cref="Busy"/> tells timed refreshes (refiner, research, inventory crafting) to skip while a mouse button
    ///    is held or a context menu is open, because a rebuild between press and release destroys the pressed element
    ///    and drops the click.
    /// </summary>
    public static class UIClickKit
    {
        public const string KeepPicking = "xa-pick-keep";

        /// <summary>True while a mouse button is down (a click or drag is in progress).</summary>
        public static bool PointerHeld
        {
            get
            {
                var m = Mouse.current;
                return m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed);
            }
        }

        /// <summary>Skip timed rebuilds while this is true.</summary>
        public static bool Busy => PointerHeld || ItemContextMenu.AnyOpen;

        public static void Attach(VisualElement root)
        {
            if (root == null || root.ClassListContains("xa-clickkit")) return;
            root.AddToClassList("xa-clickkit");
            root.schedule.Execute(() =>
            {
                if (root.panel == null || root.resolvedStyle.display == DisplayStyle.None) return;
                Walk(root, false);
            }).Every(250);
        }

        /// <summary>Apply immediately to a subtree (used right after building a window).</summary>
        public static void Apply(VisualElement subtree) { if (subtree != null) Walk(subtree, false); }

        static bool IsClickable(VisualElement e) =>
            e is Button || e.ClassListContains("xa-item-slot") || e.ClassListContains("xa-term__row")
            || e.ClassListContains("xa-recipe") || e.ClassListContains("xa-refiner-mat");

        static bool IsComposite(VisualElement e) =>
            e is Scroller || e is ScrollView || e is Slider || e is SliderInt || e is Toggle || e is TextField
            || e is DropdownField || e is Foldout || e is MapView3D || e.ClassListContains(KeepPicking);

        static void Walk(VisualElement e, bool underClickable)
        {
            if (IsComposite(e)) return;
            bool clickable = underClickable || IsClickable(e);
            if (underClickable && IsDecor(e) && e.pickingMode != PickingMode.Ignore)
                e.pickingMode = PickingMode.Ignore;
            int n = e.childCount;
            for (int i = 0; i < n; i++)
            {
                var c = e[i];
                // A nested Button is its own control: it keeps picking and starts a new clickable scope.
                Walk(c, clickable && !IsClickable(c));
            }
        }

        // Labels, images and plain wrapper elements inside a control are decoration: the control itself takes the pointer.
        // Nested controls (Button / slot / row) keep their own picking; mark a child with xa-pick-keep to opt out.
        static bool IsDecor(VisualElement e) =>
            !IsClickable(e) && (e is Label || e is Image || e.GetType() == typeof(VisualElement));

        /// <summary>Right-click on <paramref name="ve"/> opens (or retargets) the docked item inspector for the stack.</summary>
        public static void InspectOnRightClick(VisualElement ve, System.Func<ItemStack> stack)
        {
            if (ve == null) return;
            ve.AddToClassList(KeepPicking);
            ve.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                var st = stack();
                if (st == null || st.IsEmpty) return;
                e.StopPropagation();
                ItemInspector.Open(st, e.position);
            });
        }
    }
}