using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Shared icon slot used by the inventory, storage crates and the refiner (approved slot-grid mockups).</summary>
    public static class ItemSlotUI
    {
        public const int Size = 80;

        public static VisualElement Grid(int columns = 6, int size = Size)
        {
            var g = new VisualElement();
            g.AddToClassList("xa-item-grid");
            g.style.flexDirection = FlexDirection.Row;
            g.style.flexWrap = Wrap.Wrap;
            g.style.width = (size + 4) * columns;
            return g;
        }

        public static VisualElement Create(int size = Size, bool selected = false)
        {
            var t = new VisualElement();
            t.AddToClassList("xa-item-slot");
            t.style.width = size; t.style.height = size;
            t.style.marginRight = 4; t.style.marginBottom = 4;
            t.style.paddingLeft = t.style.paddingRight = t.style.paddingTop = t.style.paddingBottom = 0;
            t.style.position = Position.Relative;
            t.style.overflow = Overflow.Hidden;
            t.style.backgroundColor = XenoarchPalette.PanelDark;
            t.style.borderTopWidth = t.style.borderBottomWidth = t.style.borderLeftWidth = t.style.borderRightWidth = 1;
            SetSelected(t, selected);
            return t;
        }

        public static void SetSelected(VisualElement t, bool selected)
        {
            var bc = selected ? XenoarchPalette.Highlight : XenoarchPalette.A(XenoarchPalette.Secondary, 0.4f);
            t.style.borderTopColor = t.style.borderBottomColor = t.style.borderLeftColor = t.style.borderRightColor = bc;
        }

        public static void Paint(VisualElement t, ItemDefinition d, ItemStack st, bool selected, string extraTip = null)
        {
            t.Clear();
            SetSelected(t, selected);
            if (st == null || st.IsEmpty)
            {
                t.tooltip = extraTip ?? "";
                return;
            }
            if (d != null && d.icon != null)
            {
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.style.flexGrow = 1;
                XenoarchIcons.ApplyItem(img, d);
                t.Add(img);
            }
            else
            {
                t.Add(new Label(d != null ? d.DisplayNameOrId : st.itemId)
                {
                    pickingMode = PickingMode.Ignore,
                    style = { fontSize = 10, whiteSpace = WhiteSpace.Normal }
                });
            }
            if (st.count > 1)
            {
                var n = new Label(st.count.ToString()) { pickingMode = PickingMode.Ignore };
                n.AddToClassList("xa-item-count");
                n.style.position = Position.Absolute; n.style.right = 3; n.style.bottom = 1;
                n.style.fontSize = 12; n.style.color = Color.white;
                t.Add(n);
            }
            t.tooltip = TooltipFor(d, st, extraTip);
        }

        public static string TooltipFor(ItemDefinition d, ItemStack st, string extra = null)
        {
            if (d == null && (st == null || st.IsEmpty)) return extra ?? "";
            string name = d != null ? d.DisplayNameOrId : st.itemId;
            string group = ResourceGroups.GroupLabel(d);
            var lines = new System.Text.StringBuilder();
            lines.Append(name);
            if (!string.IsNullOrEmpty(group)) lines.Append("\n").Append(group);
            if (st != null && st.count > 1)
            {
                int max = d != null ? d.EffectiveMaxStack : st.count;
                lines.Append($"\nStack  x{st.count} / {max}");
            }
            if (d != null)
            {
                float mass = d.mass;
                if (st != null && st.count > 1) lines.Append($"\nWeight  {mass:0.##} kg each · {mass * st.count:0.##} kg");
                else lines.Append($"\nWeight  {mass:0.##} kg");
                int val = d.baseValue;
                if (st != null && st.count > 1) lines.Append($"\nValue  {val} cr · {val * st.count} cr");
                else lines.Append($"\nValue  {val} cr");
            }
            if (!string.IsNullOrEmpty(extra)) lines.Append("\n").Append(extra);
            return lines.ToString();
        }

        public static VisualElement Icon(ItemDefinition d, int size)
        {
            var img = new VisualElement { pickingMode = PickingMode.Ignore };
            img.style.width = size; img.style.height = size;
            if (d != null && d.icon != null) XenoarchIcons.ApplyItem(img, d);
            return img;
        }
    }
}
