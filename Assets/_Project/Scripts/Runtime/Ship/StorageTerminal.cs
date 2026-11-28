using System.Collections;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship Storage crate: E plays a hand reach, the hinged lid swings open, then the shared ship hold opens as icon slot grids (click or drag a
    /// stack to move it between backpack and hold; right-click to inspect). The lid closes when the panel closes. Every Storage crate shows the
    /// role's inventory (<see cref="Core.GameSession.Holds"/>). The lid is only rotated at runtime; its saved transform is untouched.
    /// </summary>
    public sealed class StorageTerminal : TerminalWindow
    {
        [Tooltip("Auto: from the object name (contains Artifact / Equipment / General; fallback General).")] public bool roleFromName = true;
        public StorageRole role = StorageRole.General;
        [Tooltip("Hinged lid (auto: child named Crate_Top_02, else a child with Top/Lid in its name).")] public Transform lid;
        [Tooltip("How far the lid opens (deg).")] [Range(5f, 110f)] public float lidAngle = 30f;
        public float lidSeconds = 0.35f;

        protected override string PanelId => "terminal.storage";
        void Reset() => terminalName = "Ship Storage";

        Vector3 _closedPos; Quaternion _closedRot; Vector3 _hingeLocal, _axisLocal; bool _lidReady;
        float _lidT; Coroutine _anim; bool _opening;

        public string HingeInfo { get; private set; } = "no lid";

        public StorageRole Role => roleFromName ? ShipHold.RoleFromName(name) : role;
        Inventory.InventoryModel HoldInv => Session.Hold(Role);

        void Awake()
        {
            if (lid == null) lid = FindLid(transform);
            if (lid == null) return;
            _closedPos = lid.localPosition; _closedRot = lid.localRotation;
            // Hinge: the lid's pivot sits on one edge (Crate_Top_02). If the pivot is central, use the edge away from
            // the crate's front instead. Axis: horizontal, along that edge; sign chosen so the free edge rises.
            var b = Bounds(lid);
            var parent = lid.parent;
            Vector3 pivot = lid.position, c = b.center - pivot; c.y = 0f;
            if (c.magnitude < Mathf.Max(b.extents.x, b.extents.z) * 0.3f)
            {
                var back = -transform.forward; back.y = 0f; back.Normalize();
                pivot = b.center + Vector3.Scale(back, b.extents); pivot.y = lid.position.y;
                c = b.center - pivot; c.y = 0f;
            }
            if (c.sqrMagnitude < 1e-4f) return;
            var axis = Vector3.Cross(Vector3.up, c.normalized);
            if ((Quaternion.AngleAxis(lidAngle, axis) * c).y < 0f) axis = -axis;
            _hingeLocal = parent != null ? parent.InverseTransformPoint(pivot) : pivot;
            _axisLocal = parent != null ? parent.InverseTransformDirection(axis) : axis;
            _lidReady = true;
            HingeInfo = $"lid '{lid.name}', hinge at {(pivot == lid.position ? "its pivot" : "the back edge")}, axis (world) {axis:F2}";
        }

        public static Transform FindLid(Transform root)
        {
            var exact = Find(root, t => t.name == "Crate_Top_02");
            return exact != null ? exact : Find(root, t => t != root && (t.name.Contains("Top") || t.name.ToLowerInvariant().Contains("lid")));
        }

        static Transform Find(Transform t, System.Func<Transform, bool> pred)
        {
            if (pred(t)) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), pred); if (r != null) return r; }
            return null;
        }

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.5f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        void ApplyLid(float t01)
        {
            if (!_lidReady || lid == null) return;
            lid.SetLocalPositionAndRotation(_closedPos, _closedRot);
            if (t01 <= 0f) return;
            var parent = lid.parent;
            var h = parent != null ? parent.TransformPoint(_hingeLocal) : _hingeLocal;
            var a = parent != null ? parent.TransformDirection(_axisLocal) : _axisLocal;
            lid.RotateAround(h, a, lidAngle * Mathf.SmoothStep(0f, 1f, t01));
        }

        IEnumerator AnimateLid(float to, System.Action done)
        {
            while (!Mathf.Approximately(_lidT, to))
            {
                _lidT = Mathf.MoveTowards(_lidT, to, Time.unscaledDeltaTime / Mathf.Max(0.05f, lidSeconds));
                ApplyLid(_lidT);
                yield return null;
            }
            _anim = null;
            done?.Invoke();
        }

        public override float GetPriority(in InteractionContext c) => IsOpen || _opening ? -1f : 1f;
        public override string GetPrompt(in InteractionContext c) => $"Open {Title}";
        string Title => Role == StorageRole.General ? "General Storage" : $"{Role} Storage";

        public override bool TryInteract(in InteractionContext c)
        {
            if (IsOpen || _opening || Session == null) return false;
            _opening = true;
            var arms = c.Rig != null ? c.Rig.Root.GetComponentInChildren<FirstPersonArms>() : null;
            var target = lid != null ? Bounds(lid).center : transform.position + Vector3.up * 0.6f;
            void Lift()
            {
                if (_anim != null) StopCoroutine(_anim);
                _anim = StartCoroutine(AnimateLid(1f, () => { _opening = false; Open(); }));
            }
            if (arms != null) arms.Reach(target, Lift); else Lift();
            return true;
        }

        protected override void OnClosed()
        {
            if (Session != null) { HoldInv.Changed -= Refresh; Session.Inventory.Changed -= Refresh; }
            if (!isActiveAndEnabled) { _lidT = 0f; ApplyLid(0f); return; }
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimateLid(0f, null));
        }

        protected override void OnOpened() { terminalName = Title; HoldInv.Changed += Refresh; Session.Inventory.Changed += Refresh; }

        protected override void Build(VisualElement body)
        {
            var inv = Session.Inventory; var hold = HoldInv;
            int used = hold.Capacity - hold.FreeSlots;
            Status.text = $"HOLD {used}/{hold.Capacity} SLOTS  -  LEVEL {ShipHold.Level + 1}/{ShipHold.MaxLevel + 1}";
            Hint.text = "LMB: move / drag   |   RMB: Inspect, Move, Split, Drop, Destroy   |   Esc / Tab / B: close";

            var left = ScrollCol(body, 0, true);
            Text(left, $"BACKPACK  ({inv.Capacity - inv.FreeSlots}/{inv.Capacity})", "xa-term__h");
            Text(left, $"Click or drag a stack to store it here. {Accepts()}", "xa-term__dim");
            Grid(left, inv, hold, "backpack", "crate", 7);
            Btn(left, "Store everything this crate takes (not tools in hand)", () =>
            {
                foreach (var s in Snapshot(inv))
                    if (Session.Items.TryGetItem(s.itemId, out var d) && d.category != Items.ItemCategory.Tool && ShipHold.Accepts(Role, d.category)) ShipHold.Transfer(inv, hold, s);
            });

            var right = ScrollCol(body, 1, true);
            Text(right, $"{Title.ToUpperInvariant()}  ({used}/{hold.Capacity})", "xa-term__h");
            Text(right, "Click or drag a stack to take it. Right-click any item to inspect.", "xa-term__dim");
            Grid(right, hold, inv, "crate", "backpack", 10);
            if (ShipHold.Level < ShipHold.MaxLevel) Text(right, $"Hold upgrades: {ShipHold.SlotsFor(Session.Config.starterShip, ShipHold.Level + 1)} slots at the next level (ship upgrades, coming with crafting).", "xa-term__dim");
        }

        string Accepts() => Role == StorageRole.General ? "Takes anything." : Role == StorageRole.Equipment ? "Takes tools, equipment, consumables and materials." : "Takes artifacts, data records, valuables and materials.";

        static System.Collections.Generic.List<ItemStack> Snapshot(InventoryModel m)
        {
            var l = new System.Collections.Generic.List<ItemStack>();
            foreach (var s in m.Slots) if (!s.IsEmpty) l.Add(s.Clone());
            return l;
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
            if (dest == HoldInv && Session.Items.TryGetItem(st.itemId, out var dd) && dd != null && !ShipHold.Accepts(Role, dd.category))
            { Toast.Show($"{Title} does not take that"); return; }
            if (ShipHold.Transfer(src, dest, st) == 0) Toast.Show(dest == HoldInv ? $"{Title} is full" : "Backpack is full");
            else Refresh();
        }

        VisualElement Slot(InventoryModel from, int index, InventoryModel to, string kind, string otherKind)
        {
            var st = from.Slots[index];
            Session.Items.TryGetItem(st.itemId, out var d);
            var t = ItemSlotUI.Create();
            ItemSlotUI.Paint(t, d, st, false, "Right-click: Inspect, Move, Split, Drop, Destroy");
            bool intoHold = to == HoldInv;
            bool ok = st.IsEmpty || !intoHold || d == null || ShipHold.Accepts(Role, d.category);
            if (!st.IsEmpty && ok)
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
                if (intoHold && Session.Items.TryGetItem(cur.itemId, out var dd) && dd != null && !ShipHold.Accepts(Role, dd.category))
                { Toast.Show($"{Title} does not take that"); return; }
                if (ShipHold.Transfer(from, to, cur) == 0) Toast.Show(intoHold ? $"{Title} is full" : "Backpack is full");
                else Refresh();
            });
            t.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || from.Slots[slot].IsEmpty) return;
                e.StopPropagation();
                var cur = from.Slots[slot];
                Session.Items.TryGetItem(cur.itemId, out var def);
                string moveLabel = kind == "backpack" ? "Move to Hold" : "Move to Backpack";
                ItemContextMenu.Show(new ItemContextMenu.Request
                {
                    Session = Session,
                    Host = Body,
                    Stack = cur.Clone(),
                    Inventory = from,
                    Index = slot,
                    Remove = () => from.TakeAt(slot),
                    Refresh = Refresh,
                    MoveLabel = moveLabel,
                    OnMove = () =>
                    {
                        var stack = from.Slots[slot].Clone();
                        if (stack.IsEmpty) return;
                        if (to == HoldInv && Session.Items.TryGetItem(stack.itemId, out var dd) && dd != null && !ShipHold.Accepts(Role, dd.category))
                        { Toast.Show($"{Title} does not take that"); return; }
                        if (ShipHold.Transfer(from, to, stack) == 0) Toast.Show(to == HoldInv ? $"{Title} is full" : "Backpack is full");
                    }
                }, e.position);
            });
            return t;
        }
    }
}
