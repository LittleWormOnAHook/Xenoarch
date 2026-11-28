using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Full-backpack prompt (t303u): Send to ship (remote) or stash in a nearby shuttle / rover, in the same terminal style.
    /// While open, further overflow is queued so digging strokes do not stack windows.
    /// </summary>
    public sealed class OverflowPrompt : MonoBehaviour
    {
        public const string PanelId = "item.overflow";
        static OverflowPrompt _i;
        UIDocument _doc;
        VisualElement _root;
        bool _open;
        readonly List<Pending> _queue = new List<Pending>();

        struct Pending
        {
            public ItemStack Stack;
            public string Context;
            public Action<int> OnSent;
            public Action OnCancel;
            public Action<VehicleCargo.CargoDest> OnRoute; // if set, dest is handed to the caller instead of auto-sending
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _i = null;

        public static bool IsOpen => _i != null && _i._open;

        /// <summary>Offer to send a stack that did not fit in the backpack. <paramref name="onSent"/> gets how many moved
        /// (from the world / leftover, not taken from the backpack).</summary>
        public static void Offer(ItemStack leftover, string context, Action<int> onSent, Action onCancel = null)
        {
            if (leftover == null || leftover.IsEmpty) return;
            if (_i == null)
            {
                var go = new GameObject("Xenoarch Overflow Prompt");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<OverflowPrompt>();
            }
            _i.Enqueue(leftover.Clone(), context, onSent, onCancel, null);
        }

        /// <summary>Same prompt, but the caller performs the send (e.g. recover an artifact straight into the dest).</summary>
        public static void OfferRoute(ItemStack visual, string context, Action<VehicleCargo.CargoDest> onRoute, Action onCancel = null)
        {
            if (visual == null || visual.IsEmpty || onRoute == null) return;
            if (_i == null)
            {
                var go = new GameObject("Xenoarch Overflow Prompt");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<OverflowPrompt>();
            }
            _i.Enqueue(visual.Clone(), context, null, onCancel, onRoute);
        }

        public static void OfferWorld(string itemId, int count, string originBody, string originSite, string context, Action<int> onSent, Action onCancel = null)
        {
            var st = new ItemStack(itemId, count) { originBodyId = originBody, originSiteId = originSite };
            Offer(st, context, onSent, onCancel);
        }

        void Enqueue(ItemStack stack, string context, Action<int> onSent, Action onCancel, Action<VehicleCargo.CargoDest> onRoute)
        {
            // Merge into an already-queued or open stack of the same item + meta.
            for (int i = 0; i < _queue.Count; i++)
            {
                var p = _queue[i];
                if (p.Stack.itemId == stack.itemId && string.IsNullOrEmpty(stack.instanceId) && p.Stack.SameMeta(stack)
                    && string.IsNullOrEmpty(p.Stack.instanceId))
                {
                    int already = p.Stack.count;
                    p.Stack.count += stack.count;
                    var prevSent = p.OnSent; var prevCancel = p.OnCancel;
                    p.OnSent = n =>
                    {
                        // Split the moved count across the merged offers (first-come).
                        int first = Mathf.Min(n, already);
                        int second = Mathf.Max(0, n - already);
                        prevSent?.Invoke(first);
                        onSent?.Invoke(second);
                    };
                    p.OnCancel = () => { prevCancel?.Invoke(); onCancel?.Invoke(); };
                    _queue[i] = p;
                    if (_open) BuildUi();
                    return;
                }
            }
            _queue.Add(new Pending { Stack = stack, Context = context, OnSent = onSent, OnCancel = onCancel, OnRoute = onRoute });
            if (!_open) Show();
            else BuildUi();
        }

        void Show()
        {
            EnsureUi();
            _open = true;
            BuildUi();
            XenoarchUI.Show(_root, true);
            UIState.SetOpen(PanelId, true);
        }

        void Hide()
        {
            if (!_open) return;
            _open = false;
            _queue.Clear();
            XenoarchUI.Show(_root, false);
            UIState.SetOpen(PanelId, false);
        }

        void Update()
        {
            if (!_open) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame))
                CancelAll();
        }

        void EnsureUi()
        {
            if (_doc != null) return;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 70;
            var docRoot = _doc.rootVisualElement;
            XenoarchUI.FillRoot(docRoot);
            docRoot.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch");
            if (uss != null) docRoot.styleSheets.Add(uss);
            _root = new VisualElement();
            _root.AddToClassList("xa-fill");
            docRoot.Add(_root);
            XenoarchUI.Show(_root, false);
        }

        void BuildUi()
        {
            _root.Clear();
            if (_queue.Count == 0) { Hide(); return; }
            var p = _queue[0];
            var s = XenoarchRuntime.Session;
            ItemDefinition def = null;
            if (s != null) s.Items.TryGetItem(p.Stack.itemId, out def);
            string name = def != null ? def.DisplayNameOrId : p.Stack.itemId;
            int more = 0;
            for (int i = 1; i < _queue.Count; i++) more += _queue[i].Stack.count;

            var dim = new VisualElement(); dim.AddToClassList("xa-inspect-dim"); _root.Add(dim);

            var panel = new VisualElement(); panel.AddToClassList("xa-ctx"); panel.AddToClassList("xa-overflow");
            _root.Add(panel);
            var title = new Label("BACKPACK IS FULL"); title.AddToClassList("xa-term__h"); panel.Add(title);
            var ctx = new Label(string.IsNullOrEmpty(p.Context) ? "There is no room left in your pack." : p.Context);
            ctx.AddToClassList("xa-term__dim"); ctx.style.whiteSpace = WhiteSpace.Normal; panel.Add(ctx);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.marginTop = 8; row.style.marginBottom = 8;
            var icon = ItemSlotUI.Create(64, true);
            ItemSlotUI.Paint(icon, def, p.Stack, true);
            icon.pickingMode = PickingMode.Ignore;
            row.Add(icon);
            var info = new VisualElement(); info.style.marginLeft = 10;
            var nm = new Label(name + (p.Stack.count > 1 ? $"  x{p.Stack.count}" : "")); nm.AddToClassList("xa-term__h"); info.Add(nm);
            if (more > 0) { var extra = new Label($"+ {more} more waiting"); extra.AddToClassList("xa-term__dim"); info.Add(extra); }
            row.Add(info);
            panel.Add(row);

            var dests = VehicleCargo.Destinations(p.Stack);
            if (dests.Count == 0)
            {
                var w = new Label("Ship storage is full, and no shuttle or rover is nearby. Drop or destroy something first.");
                w.AddToClassList("xa-term__warn"); w.style.whiteSpace = WhiteSpace.Normal; panel.Add(w);
            }
            else
            {
                var hint = new Label("Send this find to the ship, or stash it in a nearby vehicle.");
                hint.AddToClassList("xa-term__dim"); hint.style.whiteSpace = WhiteSpace.Normal; panel.Add(hint);
                foreach (var d in dests)
                {
                    var dest = d;
                    var b = new Button(() => Accept(dest)) { text = dest.Label };
                    b.AddToClassList("xa-term__btn");
                    b.AddToClassList("xa-term__btn--primary");
                    panel.Add(b);
                }
            }
            var cancel = new Button(CancelAll) { text = "Leave it" };
            cancel.AddToClassList("xa-term__btn");
            cancel.AddToClassList("xa-term__btn--ghost");
            panel.Add(cancel);
        }

        void Accept(VehicleCargo.CargoDest dest)
        {
            if (_queue.Count == 0 || dest == null || dest.Inventory == null) return;
            var p = _queue[0];
            _queue.RemoveAt(0);
            if (p.OnRoute != null)
            {
                p.OnRoute(dest);
            }
            else
            {
                int moved = VehicleCargo.Send(null, dest.Inventory, p.Stack, fromWorld: true);
                if (moved <= 0) Toast.Show($"{dest.Label} is full.");
                else
                {
                    string where = dest.Id == "ship" ? "ship storage" : dest.Id;
                    Toast.Show($"Sent {moved} to the {where}.");
                }
                p.OnSent?.Invoke(moved);
            }
            if (_queue.Count > 0) BuildUi();
            else Hide();
        }

        void CancelAll()
        {
            var copy = _queue.ToArray();
            _queue.Clear();
            Hide();
            foreach (var p in copy) p.OnCancel?.Invoke();
        }
    }
}
