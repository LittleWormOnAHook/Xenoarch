using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Save;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Saved tool wheel layout: 8 slots of item ids, or <see cref="ToolWheelHud.RoverId"/>.</summary>
    [Serializable]
    public sealed class ToolWheelData
    {
        public List<string> slots = new List<string>();
    }

    /// <summary>
    /// t246u tool wheel (Morpheus radial). Hold Tab to open, point with the mouse, release Tab to use the slot:
    /// tools equip (same toggles as the hotbar keys), the rover slot deploys the rover from the backpack or, when it is
    /// already out and far away, calls it to you (recall + deploy in front of you), consumables report what you carry.
    /// A short Tab tap still opens the inventory. Right-click a slot to choose what goes in it; drag one slot onto
    /// another to swap them (both pin the wheel open; Esc or Tab closes). The layout is saved with the game.
    /// Bootstraps itself onto the runtime object (no scene or prefab edits).
    /// </summary>
    public sealed class ToolWheelHud : MonoBehaviour
    {
        public const string Panel = "toolwheel";
        public const string SaveKey = "ui.toolwheel";
        public const string RoverId = "rover";
        public const int Size = 8;
        const float HoldSeconds = 0.22f;
        const float DeadZone = 48f;
        const float SummonDistance = 30f;

        public static ToolWheelHud Instance { get; private set; }

        ToolWheelData _data = new ToolWheelData();
        UIDocument _doc;
        VisualElement _root, _wheel, _picker, _info;
        WheelGraphic _graphic;
        readonly VisualElement[] _slotEls = new VisualElement[Size];
        readonly Label[] _slotLabels = new Label[Size];
        Label _centerAction, _centerName, _infoName, _infoText, _infoCount, _title;
        VisualElement _infoIcon;
        bool _open, _pinned, _wasBlocked;
        float _tabDownAt = -1f;
        int _hover = -1, _dragFrom = -1, _pickFor = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || rt.GetComponent<ToolWheelHud>() != null) return;
            rt.gameObject.AddComponent<ToolWheelHud>();
        }

        void Awake()
        {
            Instance = this;
            XenoarchRuntime.Session?.Saves.Register(new JsonStateSection<ToolWheelData>(SaveKey, 5,
                () => _data, v => { _data = v ?? Defaults(); Normalize(); if (_open) Refresh(); }, Defaults));
            _data = Defaults();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnDisable() { if (_open) Close(); }

        // ------------------------------------------------------------------ data
        static string ScannerId => XenoarchRuntime.Session?.Config.surfaceScanner != null ? XenoarchRuntime.Session.Config.surfaceScanner.itemId : null;
        static string ProbeId { get { var d = DepthProbeDefinition.Load(); return d != null ? d.itemId : null; } }
        const string SampleContainerId = "item.consumable.sample_container";

        static ToolWheelData Defaults()
        {
            var d = new ToolWheelData();
            void Add(string id) { if (!string.IsNullOrEmpty(id) && !d.slots.Contains(id) && d.slots.Count < Size) d.slots.Add(id); }
            Add(ScannerId);
            foreach (var t in DigTools.All) Add(t.itemId);
            Add(ProbeId);
            Add(RoverId);
            Add(SampleContainerId);
            while (d.slots.Count < Size) d.slots.Add("");
            return d;
        }

        void Normalize()
        {
            if (_data.slots == null) _data.slots = new List<string>();
            while (_data.slots.Count < Size) _data.slots.Add("");
            if (_data.slots.Count > Size) _data.slots.RemoveRange(Size, _data.slots.Count - Size);
        }

        string At(int i) { Normalize(); return i >= 0 && i < Size ? _data.slots[i] : null; }

        public void Assign(int i, string id)
        {
            Normalize();
            if (i < 0 || i >= Size) return;
            if (!string.IsNullOrEmpty(id)) { int was = _data.slots.IndexOf(id); if (was >= 0 && was != i) _data.slots[was] = _data.slots[i]; }
            _data.slots[i] = id ?? "";
            Refresh();
        }

        public void Swap(int a, int b)
        {
            Normalize();
            if (a < 0 || b < 0 || a >= Size || b >= Size || a == b) return;
            (_data.slots[a], _data.slots[b]) = (_data.slots[b], _data.slots[a]);
            Refresh();
        }

        static GameSession S => XenoarchRuntime.Session;
        static ItemDefinition Def(string id) => !string.IsNullOrEmpty(id) && S != null && S.Items.TryGetItem(id, out var d) ? d : null;
        static int Count(string id) => S != null && !string.IsNullOrEmpty(id) ? S.Inventory.Count(id) : 0;

        static VehicleItemDefinition RoverItem()
        {
            if (S == null) return null;
            foreach (var st in Carried())
                if (Def(st.itemId) is VehicleItemDefinition v) return v;
            return null;
        }

        /// <summary>Backpack stacks plus the hotbar's own stacks (t243u: hotbar items don't use backpack space).</summary>
        static IEnumerable<DMI.Xenoarch.Inventory.ItemStack> Carried()
        {
            if (S == null) yield break;
            foreach (var st in S.Inventory.Slots) if (st != null && !st.IsEmpty) yield return st;
            if (S.Inventory.ExtraStacks != null)
                foreach (var st in S.Inventory.ExtraStacks()) if (st != null && !st.IsEmpty) yield return st;
        }

        static string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Empty";
            if (id == RoverId) { var r = RoverItem(); return r != null ? r.DisplayNameOrId : "Rover"; }
            var d = Def(id); return d != null ? d.DisplayNameOrId : id;
        }

        static string ShortName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Empty";
            if (id == RoverId) return "Rover";
            if (id == ScannerId) return "Scanner";
            if (id == ProbeId) return "Depth probe";
            if (id == SampleContainerId) return "Sample jar";
            foreach (var t in DigTools.All) if (t.itemId == id && !string.IsNullOrEmpty(t.shortName)) return char.ToUpperInvariant(t.shortName[0]) + t.shortName.Substring(1);
            return Name(id);
        }

        static bool Owned(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id == RoverId) return RoverItem() != null || (RoverDirector.Instance != null && RoverDirector.Instance.Current != null);
            return Count(id) > 0;
        }

        /// <summary>What releasing Tab on this slot would do (centre of the wheel).</summary>
        static string ActionText(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Empty";
            if (id == RoverId)
            {
                var rd = RoverDirector.Instance;
                if (rd != null && rd.Current != null) return DistanceToRover(rd) > SummonDistance ? "Call" : "Nearby";
                return "Deploy";
            }
            if (!Owned(id)) return "Not carried";
            var d = Def(id);
            if (d != null && d.category == ItemCategory.Consumable) return "Carried";
            var dig = FindAnyObjectByType<DigTool>();
            int ti = ToolIndex(id);
            if (ti >= 0) return dig != null && dig.Equipped == ti ? "Holster" : "Equip";
            if (id == ScannerId) { var sc = FindAnyObjectByType<SurfaceScannerTool>(); return sc != null && sc.Equipped ? "Holster" : "Equip"; }
            if (id == ProbeId) { var p = FindAnyObjectByType<DepthProbeTool>(); return p != null && p.Equipped ? "Holster" : "Equip"; }
            return "Select";
        }

        static int ToolIndex(string id)
        {
            var all = DigTools.All;
            for (int i = 0; i < all.Length; i++) if (all[i].itemId == id) return i;
            return -1;
        }

        static float DistanceToRover(RoverDirector rd)
        {
            var p = FindAnyObjectByType<FirstPersonController>();
            if (p == null || rd.Current == null) return 0f;
            return Vector3.Distance(p.transform.position, rd.Current.transform.position);
        }

        /// <summary>Candidates for the slot picker: the default tools, the rover, and every tool, vehicle or consumable carried.</summary>
        List<string> Eligible()
        {
            var list = new List<string>();
            void Add(string id) { if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id); }
            foreach (var id in Defaults().slots) Add(id);
            if (S != null)
                foreach (var st in Carried())
                {
                    var d = Def(st.itemId);
                    if (d == null || d is VehicleItemDefinition) continue;
                    if (d.category == ItemCategory.Tool || d.category == ItemCategory.Consumable) Add(st.itemId);
                }
            return list;
        }

        // ------------------------------------------------------------------ use
        void Activate(int i)
        {
            string id = At(i);
            if (string.IsNullOrEmpty(id)) return;
            var dig = FindAnyObjectByType<DigTool>();
            var scanner = FindAnyObjectByType<SurfaceScannerTool>();
            var probe = FindAnyObjectByType<DepthProbeTool>();
            if (id == RoverId) { UseRover(); return; }
            var d = Def(id);
            if (!Owned(id)) { Toast.Show(DigTool.NotOwnedText(d != null ? d.DisplayNameOrId : ShortName(id))); return; }
            int ti = ToolIndex(id);
            if (ti >= 0)
            {
                if (dig == null) { Toast.Show("Tools can't be used here."); return; }
                if (probe != null && probe.Equipped) probe.SetEquipped(false);
                dig.Equip(dig.Equipped == ti ? -1 : ti);
                return;
            }
            if (id == ScannerId)
            {
                if (scanner == null) { Toast.Show("The scanner can't be used here."); return; }
                bool on = !scanner.Equipped;
                if (on) { dig?.Holster(); if (probe != null && probe.Equipped) probe.SetEquipped(false); }
                scanner.SetEquipped(on);
                return;
            }
            if (id == ProbeId)
            {
                if (probe == null) { Toast.Show("The depth probe can't be used here."); return; }
                bool on = !probe.Equipped;
                if (on) dig?.Holster();
                probe.SetEquipped(on);
                return;
            }
            if (d != null && d.category == ItemCategory.Consumable)
            {
                Toast.Show(id == SampleContainerId
                    ? $"{d.DisplayNameOrId}: {Count(id)} carried. One is used each time you take a sample."
                    : $"{d.DisplayNameOrId}: {Count(id)} carried.");
                return;
            }
            Toast.Show($"{Name(id)} has no quick use. Use it from the hotbar or inventory.");
        }

        static void UseRover()
        {
            var rd = RoverDirector.Instance;
            if (rd == null) { Toast.Show("The rover is not available here."); return; }
            if (rd.Current != null)
            {
                float dist = DistanceToRover(rd);
                if (dist <= SummonDistance) { Toast.Show($"Rover is {dist:0} m away. Rear door: E to open, E to enter."); return; }
                if (rd.Current.Occupied) { Toast.Show("The rover is occupied."); return; }
                var item = RoverItem();
                if (item == null) { Toast.Show($"Rover is {dist:0} m away."); return; }
                rd.Recall();
                if (rd.Deploy(item, out var why)) Toast.Show($"{item.DisplayNameOrId} called to your position.");
                else Toast.Show($"Could not call the rover: {why}");
                return;
            }
            var v = RoverItem();
            if (v == null) { Toast.Show("No rover in your backpack."); return; }
            if (rd.Deploy(v, out var r)) Toast.Show($"{v.DisplayNameOrId} deployed. Rear door: E to open, E to enter.");
            else Toast.Show($"Can't deploy: {r}");
        }

        // ------------------------------------------------------------------ input
        bool CanOpen()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || !rt.SessionActive || DMI.Xenoarch.Scenes.SceneLoader.Busy) return false;
            if (_wasBlocked || UIState.BlocksGameplay) return false;
            string sc = SceneManager.GetActiveScene().name;
            return sc != SceneNames.MainMenu && sc != SceneNames.Bootstrap;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) { _wasBlocked = UIState.BlocksGameplay; return; }

            if (_open)
            {
                if (kb.escapeKey.wasPressedThisFrame) { Close(); _tabDownAt = -1f; }
                else if (_pinned)
                {
                    if (kb.tabKey.wasPressedThisFrame) { Close(); _tabDownAt = -1f; }
                }
                else
                {
                    UpdateHover();
                    if (!kb.tabKey.isPressed)
                    {
                        int pick = _hover;
                        Close();
                        _tabDownAt = -1f;
                        if (pick >= 0) Activate(pick);
                    }
                }
            }
            else if (_tabDownAt >= 0f)
            {
                if (!kb.tabKey.isPressed)
                {
                    // Short tap: the inventory, as Tab always did.
                    _tabDownAt = -1f;
                    if (!UIState.BlocksGameplay) FindAnyObjectByType<InventoryWindow>()?.Open();
                }
                else if (Time.unscaledTime - _tabDownAt >= HoldSeconds)
                {
                    if (!UIState.BlocksGameplay) Open(); else _tabDownAt = -1f;
                }
            }
            else if (kb.tabKey.wasPressedThisFrame && CanOpen()) _tabDownAt = Time.unscaledTime;

            _wasBlocked = UIState.BlocksGameplay;
        }

        void UpdateHover()
        {
            var m = Mouse.current;
            if (m == null) return;
            Vector2 p = m.position.ReadValue();
            float dx = p.x - Screen.width * 0.5f, dy = (Screen.height - p.y) - Screen.height * 0.5f;
            SetHover(new Vector2(dx, dy).magnitude < DeadZone ? -1 : SlotAt(dx, dy));
        }

        static int SlotAt(float dx, float dy)
        {
            float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 90f; // 0 = up, clockwise
            a = Mathf.Repeat(a + 360f / Size * 0.5f, 360f);
            return Mathf.Clamp(Mathf.FloorToInt(a / (360f / Size)), 0, Size - 1);
        }

        void SetHover(int i)
        {
            if (i == _hover) return;
            _hover = i;
            RefreshHover();
        }

        // ------------------------------------------------------------------ view
        void Open()
        {
            Build();
            if (_root == null) return;
            _open = true; _pinned = false; _hover = -1; _dragFrom = -1; _pickFor = -1;
            UIState.SetOpen(Panel, true);
            XenoarchUI.Show(_root, true);
            XenoarchUI.Show(_picker, false);
            Refresh();
        }

        void Close()
        {
            _open = false; _pinned = false; _dragFrom = -1; _pickFor = -1;
            if (_root != null) XenoarchUI.Show(_root, false);
            UIState.SetOpen(Panel, false);
        }

        void Pin() { _pinned = true; _title.text = "TOOL WHEEL  ·  EDIT"; }

        void Build()
        {
            if (_root != null) return;
            if (_doc == null)
            {
                var go = new GameObject("[Tool Wheel]");
                go.transform.SetParent(transform, false);
                _doc = go.AddComponent<UIDocument>();
                _doc.panelSettings = XenoarchUI.LoadPanelSettings();
                _doc.sortingOrder = 40;
            }
            var r = _doc.rootVisualElement;
            if (r == null) return;
            XenoarchUI.FillRoot(r); r.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null && !r.styleSheets.Contains(uss)) r.styleSheets.Add(uss);
            XenoarchIcons.EnsureStyles(r);

            _root = new VisualElement(); _root.AddToClassList("xa-wheel-root");
            r.Add(_root);
            _title = MUI.L(_root, "TOOL WHEEL", "xa-wheel__title");

            _wheel = new VisualElement(); _wheel.AddToClassList("xa-wheel");
            _root.Add(_wheel);
            _graphic = new WheelGraphic { pickingMode = PickingMode.Position };
            _graphic.AddToClassList("xa-wheel__graphic");
            _wheel.Add(_graphic);

            const float W = 600f, C = W * 0.5f, R = 190f, LR = 272f;
            for (int i = 0; i < Size; i++)
            {
                float a = (-90f + i * 360f / Size) * Mathf.Deg2Rad;
                var el = new VisualElement { pickingMode = PickingMode.Ignore }; el.AddToClassList("xa-wheel__slot");
                el.style.left = C + Mathf.Cos(a) * R - 32f; el.style.top = C + Mathf.Sin(a) * R - 32f;
                _wheel.Add(el); _slotEls[i] = el;
                var lab = new Label { pickingMode = PickingMode.Ignore }; lab.AddToClassList("xa-wheel__label");
                lab.style.left = C + Mathf.Cos(a) * LR - 90f; lab.style.top = C + Mathf.Sin(a) * LR - 11f;
                _wheel.Add(lab); _slotLabels[i] = lab;
            }
            var center = new VisualElement { pickingMode = PickingMode.Ignore }; center.AddToClassList("xa-wheel__center");
            _centerAction = MUI.L(center, "", "xa-wheel__action");
            _centerName = MUI.L(center, "", "xa-wheel__name");
            var close = new VisualElement(); close.AddToClassList("xa-m-legend__item"); close.style.marginLeft = 0; close.style.marginTop = 6;
            MUI.Key(close, "Tab"); MUI.L(close, "RELEASE", "xa-m-legend__text"); center.Add(close);
            _wheel.Add(center);

            _info = new VisualElement(); _info.AddToClassList("xa-m-panel"); _info.AddToClassList("xa-wheel__info");
            _infoIcon = new VisualElement(); _infoIcon.AddToClassList("xa-wheel__info-icon"); _info.Add(_infoIcon);
            var col = new VisualElement(); col.style.flexGrow = 1; _info.Add(col);
            _infoName = MUI.L(col, "", "xa-m-card__title");
            _infoText = MUI.L(col, "", "xa-m-dim"); _infoText.style.whiteSpace = WhiteSpace.Normal;
            _infoCount = MUI.L(_info, "", "xa-m-badge");
            _root.Add(_info);

            _picker = new VisualElement(); _picker.AddToClassList("xa-m-panel"); _picker.AddToClassList("xa-wheel__picker");
            _root.Add(_picker);

            var lg = MUI.Legend(_root, ("Mouse", "Point"), ("Release Tab", "Use"), ("Right-click", "Change slot"), ("Drag", "Swap"), ("Esc", "Cancel"));
            lg.AddToClassList("xa-wheel__legend");

            _graphic.RegisterCallback<PointerMoveEvent>(e =>
            {
                var v = (Vector2)e.localPosition - new Vector2(C, C);
                if (_pinned || _dragFrom >= 0) SetHover(v.magnitude < 70f || v.magnitude > 300f ? -1 : SlotAt(v.x, v.y));
            });
            _graphic.RegisterCallback<PointerDownEvent>(e =>
            {
                var v = (Vector2)e.localPosition - new Vector2(C, C);
                if (v.magnitude < 70f || v.magnitude > 300f) return;
                int i = SlotAt(v.x, v.y);
                if (e.button == 1) { Pin(); ShowPicker(i); }
                else if (e.button == 0) { _dragFrom = i; _graphic.CapturePointer(e.pointerId); }
            });
            _graphic.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || _dragFrom < 0) return;
                if (_graphic.HasPointerCapture(e.pointerId)) _graphic.ReleasePointer(e.pointerId);
                var v = (Vector2)e.localPosition - new Vector2(C, C);
                int from = _dragFrom; _dragFrom = -1;
                int to = v.magnitude < 70f || v.magnitude > 300f ? -1 : SlotAt(v.x, v.y);
                if (to >= 0 && to != from) { Pin(); Swap(from, to); }
                else if (to == from && !_pinned) { Close(); _tabDownAt = -1f; Activate(from); } // click = use
                else if (to == from) ShowPicker(from);
            });
            XenoarchUI.Show(_root, false);
        }

        void ShowPicker(int slot)
        {
            _pickFor = slot;
            _picker.Clear();
            MUI.Head(_picker, $"Slot {slot + 1}", ShortName(At(slot)));
            MUI.L(_picker, "Choose what this slot holds. Tools, the rover and consumables.", "xa-m-dim").style.whiteSpace = WhiteSpace.Normal;
            var list = new ScrollView(); list.style.maxHeight = 520; list.style.marginTop = 8; _picker.Add(list);
            foreach (var id in Eligible())
            {
                string cid = id;
                var row = new Button(() => { Assign(_pickFor, cid); ShowPicker(_pickFor); }); row.AddToClassList("xa-m-tile"); row.AddToClassList("xa-wheel__pick");
                if (At(slot) == cid) row.AddToClassList("xa-m-sel");
                var ic = new VisualElement(); ic.AddToClassList("xa-wheel__pick-icon"); Icon(ic, cid); row.Add(ic);
                MUI.L(row, Name(cid), "xa-m-text").style.flexGrow = 1;
                int inWheel = _data.slots.IndexOf(cid);
                if (inWheel >= 0 && inWheel != slot) MUI.L(row, $"slot {inWheel + 1}", "xa-m-dim").style.marginRight = 8;
                if (!Owned(cid)) MUI.Badge(row, "Not carried", "warn");
                list.Add(row);
            }
            var clear = new Button(() => { Assign(_pickFor, ""); ShowPicker(_pickFor); }) { text = "EMPTY THIS SLOT" };
            clear.AddToClassList("xa-term__btn"); clear.AddToClassList("xa-term__btn--ghost"); _picker.Add(clear);
            var reset = new Button(() => { _data = Defaults(); Refresh(); ShowPicker(_pickFor); }) { text = "RESET WHEEL" };
            reset.AddToClassList("xa-term__btn"); reset.AddToClassList("xa-term__btn--ghost"); _picker.Add(reset);
            XenoarchUI.Show(_picker, true);
            SetHover(slot);
        }

        static void Icon(VisualElement e, string id)
        {
            e.Clear();
            if (string.IsNullOrEmpty(id)) return;
            var d = id == RoverId ? RoverItem() : Def(id);
            if (d != null && d.icon != null)
            {
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.style.width = Length.Percent(100); img.style.height = Length.Percent(100);
                e.Add(img);
                XenoarchIcons.ApplyItem(img, d);
                return;
            }
            var gl = XenoarchIcons.Glyph(id == RoverId ? "nav-rover" : "ui-inventory");
            gl.style.width = Length.Percent(100); gl.style.height = Length.Percent(100);
            e.Add(gl);
        }

        void Refresh()
        {
            if (_root == null) return;
            Normalize();
            for (int i = 0; i < Size; i++)
            {
                string id = At(i);
                Icon(_slotEls[i], id);
                _slotEls[i].EnableInClassList("xa-wheel__slot--missing", !string.IsNullOrEmpty(id) && !Owned(id));
                _slotLabels[i].text = $"{i + 1}  {ShortName(id).ToUpperInvariant()}";
            }
            RefreshHover();
        }

        void RefreshHover()
        {
            if (_root == null) return;
            _graphic.Selected = _hover;
            _graphic.MarkDirtyRepaint();
            for (int i = 0; i < Size; i++) _slotLabels[i].EnableInClassList("xa-wheel__label--sel", i == _hover);
            string id = _hover >= 0 ? At(_hover) : null;
            _centerAction.text = (_hover < 0 ? "Cancel" : ActionText(id)).ToUpperInvariant();
            _centerName.text = (_hover < 0 ? "" : ShortName(id)).ToUpperInvariant();
            bool show = _hover >= 0 && !string.IsNullOrEmpty(id);
            XenoarchUI.Show(_info, show);
            if (!show) return;
            Icon(_infoIcon, id);
            _infoName.text = Name(id).ToUpperInvariant();
            var d = id == RoverId ? (ItemDefinition)RoverItem() : Def(id);
            string text = d != null && !string.IsNullOrEmpty(d.description) ? d.description : "";
            if (id == RoverId && RoverDirector.Instance != null && RoverDirector.Instance.Current != null)
                text = $"Deployed, {DistanceToRover(RoverDirector.Instance):0} m away. Fuel {RoverDirector.Instance.Fuel01 * 100f:0}%." + (text.Length > 0 ? " " + text : "");
            _infoText.text = text;
            int n = id == RoverId ? (Owned(id) ? 1 : 0) : Count(id);
            _infoCount.text = n > 0 ? (d != null && d.category == ItemCategory.Consumable ? $"x{n}" : "CARRIED") : "NOT CARRIED";
            _infoCount.EnableInClassList("xa-m-badge--warn", n <= 0);
        }

        /// <summary>The pie: dark disk, slot dividers, teal highlight wedge with an outer arc, teal centre ring.</summary>
        sealed class WheelGraphic : VisualElement
        {
            public int Selected = -1;
            public WheelGraphic() { generateVisualContent += Draw; }

            void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                var c = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
                const float outer = 250f, inner = 64f;
                float step = 360f / Size;
                var teal = new Color(0.41f, 0.87f, 0.84f, 1f);
                var line = new Color(0.94f, 0.83f, 0.78f, 0.14f);

                p.fillColor = new Color(0.24f, 0.06f, 0.08f, 0.82f);
                p.BeginPath(); p.Arc(c, outer, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();

                p.strokeColor = line; p.lineWidth = 1f;
                p.BeginPath(); p.Arc(c, outer + 14f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();

                for (int i = 0; i < Size; i++)
                {
                    float a = (-90f + i * step - step * 0.5f) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    p.BeginPath(); p.MoveTo(c + dir * inner); p.LineTo(c + dir * (outer + 14f)); p.Stroke();
                }

                if (Selected >= 0)
                {
                    float a0 = -90f + Selected * step - step * 0.5f, a1 = a0 + step;
                    p.fillColor = new Color(teal.r, teal.g, teal.b, 0.22f);
                    p.BeginPath();
                    p.Arc(c, outer, Angle.Degrees(a0), Angle.Degrees(a1));
                    p.Arc(c, inner, Angle.Degrees(a1), Angle.Degrees(a0), ArcDirection.CounterClockwise);
                    p.ClosePath(); p.Fill();
                    p.strokeColor = teal; p.lineWidth = 4f;
                    p.BeginPath(); p.Arc(c, outer + 8f, Angle.Degrees(a0 + 1f), Angle.Degrees(a1 - 1f)); p.Stroke();
                }

                p.fillColor = new Color(0.12f, 0.03f, 0.05f, 0.95f);
                p.BeginPath(); p.Arc(c, inner, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
                p.strokeColor = teal; p.lineWidth = 2f;
                p.BeginPath(); p.Arc(c, inner, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Stroke();
            }
        }
    }
}
