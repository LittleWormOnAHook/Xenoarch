using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Slot-grid stash for the shuttle or rover, same UI as ship crates (approved storage_slots_v1).</summary>
    public sealed class VehicleStorageTerminal : TerminalWindow
    {
        protected override string PanelId => "terminal.vehicle-storage";
        InventoryModel _hold;
        string _title = "Vehicle storage";

        static VehicleStorageTerminal _inst;

        public static void Show(InventoryModel hold, string title)
        {
            if (_inst == null)
            {
                var go = new GameObject("Xenoarch Vehicle Storage");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<VehicleStorageTerminal>();
            }
            _inst.Bind(hold, title);
            _inst.Open();
        }

        public void Bind(InventoryModel hold, string title)
        {
            _hold = hold;
            _title = string.IsNullOrEmpty(title) ? "Vehicle storage" : title;
            terminalName = _title;
        }

        protected override void Build(VisualElement body)
        {
            var inv = Session.Inventory;
            var hold = _hold;
            if (hold == null) { Text(body, "No storage on this vehicle."); return; }
            int used = hold.Capacity - hold.FreeSlots;
            Status.text = $"{_title.ToUpperInvariant()}  {used}/{hold.Capacity} SLOTS";
            Hint.text = "LMB: move / drag   |   RMB: Inspect, Move, Split, Drop, Destroy   |   Esc / Tab / B: close";

            var left = ScrollCol(body, 0, true);
            Text(left, $"BACKPACK  ({inv.Capacity - inv.FreeSlots}/{inv.Capacity})", "xa-term__h");
            Text(left, "Click or drag a stack to stash it here. You can also Send to ship from the right-click menu.", "xa-term__dim");
            Grid(left, inv, hold, "backpack", "vehicle", 7);

            var right = ScrollCol(body, 1, true);
            Text(right, $"{_title.ToUpperInvariant()}  ({used}/{hold.Capacity})", "xa-term__h");
            Text(right, "Click or drag a stack to take it. Right-click any item to inspect.", "xa-term__dim");
            Grid(right, hold, inv, "vehicle", "backpack", 8);
        }

        void Grid(VisualElement col, InventoryModel from, InventoryModel to, string kind, string otherKind, int columns)
        {
            var grid = ItemSlotUI.Grid(columns);
            col.Add(grid);
            DragDrop.Target(grid, p => p.kind == otherKind && p.container is InventoryModel, p => TransferPayload(p, from));
            for (int i = 0; i < from.Slots.Count; i++)
                grid.Add(Slot(from, i, to, kind, otherKind));
        }

        void TransferPayload(DragDrop.Payload p, InventoryModel dest)
        {
            if (p.container is not InventoryModel src) return;
            if (p.index < 0 || p.index >= src.Slots.Count || src.Slots[p.index].IsEmpty) return;
            var st = src.Slots[p.index].Clone();
            if (VehicleCargo.Send(src, dest, st) == 0) Toast.Show(dest == _hold ? $"{_title} is full" : "Backpack is full");
            else Refresh();
        }

        VisualElement Slot(InventoryModel from, int index, InventoryModel to, string kind, string otherKind)
        {
            var st = from.Slots[index];
            Session.Items.TryGetItem(st.itemId, out var d);
            var t = ItemSlotUI.Create();
            ItemSlotUI.Paint(t, d, st, false, "Right-click: Inspect, Move, Split, Drop, Destroy");
            if (!st.IsEmpty)
                Drag.Source(t, () =>
                {
                    var cur = from.Slots[index];
                    if (cur.IsEmpty) return null;
                    Session.Items.TryGetItem(cur.itemId, out var def);
                    return new DragDrop.Payload { kind = kind, index = index, itemId = cur.itemId, container = from, icon = def != null ? def.icon : null };
                });
            DragDrop.Target(t, p => p.kind == otherKind && p.container is InventoryModel, p => TransferPayload(p, from));
            int slot = index;
            t.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || Drag.Dragging || from.Slots[slot].IsEmpty) return;
                var cur = from.Slots[slot].Clone();
                if (VehicleCargo.Send(from, to, cur) == 0) Toast.Show(to == _hold ? $"{_title} is full" : "Backpack is full");
                else Refresh();
            });
            t.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || from.Slots[slot].IsEmpty) return;
                e.StopPropagation();
                ItemContextMenu.Show(new ItemContextMenu.Request
                {
                    Session = Session,
                    Host = Body,
                    Stack = from.Slots[slot].Clone(),
                    Inventory = from,
                    Index = slot,
                    Remove = () => from.TakeAt(slot),
                    Refresh = Refresh,
                    MoveLabel = kind == "backpack" ? $"Move to {_title}" : "Move to Backpack",
                    OnMove = () =>
                    {
                        var stack = from.Slots[slot].Clone();
                        if (stack.IsEmpty) return;
                        if (VehicleCargo.Send(from, to, stack) == 0) Toast.Show(to == _hold ? $"{_title} is full" : "Backpack is full");
                    }
                }, e.position);
            });
            return t;
        }
    }
}
