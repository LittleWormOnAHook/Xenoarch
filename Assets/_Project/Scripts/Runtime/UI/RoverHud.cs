using DMI.Xenoarch.Core;
using DMI.Xenoarch.Economy;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// t206u: seated rover instruments (speed, fuel, boost) and a small refuel menu (R). Transfer from ship fuel
    /// or buy at the same 4 cr/unit price as the ship refuelling vendor.
    /// </summary>
    public sealed class RoverHud : MonoBehaviour
    {
        public const string Panel = "rover_refuel";

        UIDocument _doc;
        VisualElement _root, _cluster, _menu;
        Label _speed, _fuelTxt, _boostTxt, _warn, _menuBody, _menuMsg;
        VisualElement _fuelFill, _boostFill;
        bool _built;

        void Start()
        {
            var go = new GameObject("[Rover HUD]");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 12;
        }

        void OnDisable()
        {
            if (UIState.IsOpen(Panel)) UIState.SetOpen(Panel, false);
        }

        void Build()
        {
            var r = _doc != null ? _doc.rootVisualElement : null;
            if (r == null) return;
            XenoarchUI.FillRoot(r); r.pickingMode = PickingMode.Ignore;
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;
            r.Add(_root);

            _cluster = new VisualElement { pickingMode = PickingMode.Ignore };
            _cluster.style.position = Position.Absolute;
            _cluster.style.left = Length.Percent(32f);
            _cluster.style.width = Length.Percent(36f);
            _cluster.style.bottom = 170;
            _cluster.style.alignItems = Align.Center;
            _root.Add(_cluster);

            _speed = Lab(_cluster, 22, XenoarchPalette.Heading, "0 km/h");
            _speed.style.unityFontStyleAndWeight = FontStyle.Bold;
            _speed.style.marginBottom = 2;

            _fuelFill = Bar(_cluster, out _fuelTxt, "FUEL");
            _boostFill = Bar(_cluster, out _boostTxt, "BOOST");
            _warn = Lab(_cluster, 13, XenoarchPalette.WarnText, "");
            _warn.style.marginTop = 2;
            Lab(_cluster, 12, XenoarchPalette.SecondaryText, "Shift boost   R refuel   V camera   E exit");

            _menu = new VisualElement { pickingMode = PickingMode.Position };
            _menu.style.position = Position.Absolute;
            _menu.style.left = Length.Percent(32f);
            _menu.style.width = Length.Percent(36f);
            _menu.style.top = Length.Percent(22f);
            _menu.style.paddingTop = _menu.style.paddingBottom = 16;
            _menu.style.paddingLeft = _menu.style.paddingRight = 18;
            _menu.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDeep, 0.94f);
            _menu.style.borderTopWidth = _menu.style.borderBottomWidth = _menu.style.borderLeftWidth = _menu.style.borderRightWidth = 1;
            var frame = XenoarchPalette.Frame;
            _menu.style.borderTopColor = _menu.style.borderBottomColor = _menu.style.borderLeftColor = _menu.style.borderRightColor = frame;
            _root.Add(_menu);

            Lab(_menu, 18, XenoarchPalette.Heading, "ROVER FUEL").style.unityFontStyleAndWeight = FontStyle.Bold;
            _menuBody = Lab(_menu, 14, XenoarchPalette.Text, "");
            _menuBody.style.whiteSpace = WhiteSpace.Normal;
            _menuBody.style.marginTop = 8; _menuBody.style.marginBottom = 10;
            Btn(_menu, "Transfer 10 from ship", () => Do(d => d.TransferFromShip(10f)));
            Btn(_menu, "Fill from ship", () => Do(d => d.TransferFromShip(d.FuelCapacity)));
            Btn(_menu, "Buy 10 units (credits)", () => Buy(10f));
            Btn(_menu, "Fill with credits", () => Buy(9999f));
            _menuMsg = Lab(_menu, 13, XenoarchPalette.SecondaryText, "Esc / R: close");
            _menuMsg.style.marginTop = 8;
            _built = true;
        }

        static Label Lab(VisualElement parent, int size, Color color, string text)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = color;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.paddingLeft = l.style.paddingRight = 0;
            parent.Add(l);
            return l;
        }

        static VisualElement Bar(VisualElement parent, out Label caption, string title)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            row.style.width = Length.Percent(100f);
            var name = new Label(title) { pickingMode = PickingMode.Ignore };
            name.style.fontSize = 11; name.style.color = XenoarchPalette.SecondaryText;
            name.style.width = 52; name.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(name);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.flexGrow = 1; track.style.height = 10;
            track.style.backgroundColor = new Color(0.04f, 0.07f, 0.09f, 0.85f);
            track.style.borderTopWidth = track.style.borderBottomWidth = track.style.borderLeftWidth = track.style.borderRightWidth = 1;
            var bc = XenoarchPalette.A(XenoarchPalette.Highlight, 0.45f);
            track.style.borderTopColor = track.style.borderBottomColor = track.style.borderLeftColor = track.style.borderRightColor = bc;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute; fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.width = Length.Percent(0);
            fill.style.backgroundColor = new Color(0.3f, 0.85f, 1f);
            track.Add(fill);
            row.Add(track);
            caption = new Label("") { pickingMode = PickingMode.Ignore };
            caption.style.fontSize = 11; caption.style.color = XenoarchPalette.Text;
            caption.style.width = 70; caption.style.unityTextAlign = TextAnchor.MiddleRight; caption.style.marginLeft = 6;
            row.Add(caption);
            parent.Add(row);
            return fill;
        }

        static void Btn(VisualElement parent, string text, System.Action click)
        {
            var b = new Button(click) { text = text };
            b.style.marginTop = 4;
            b.style.height = 28;
            b.style.backgroundColor = XenoarchPalette.Button;
            b.style.color = XenoarchPalette.OnButton;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            parent.Add(b);
        }

        void Do(System.Func<RoverDirector, float> op)
        {
            var d = RoverDirector.Instance;
            if (d == null) return;
            float n = op(d);
            _menuMsg.text = n > 0.05f ? $"Transferred {n:0.#} units." : "Nothing to transfer (or tank full).";
            RefreshMenu();
        }

        void Buy(float units)
        {
            var d = RoverDirector.Instance;
            if (d == null) return;
            _menuMsg.text = d.BuyFuel(units, out var msg) ? msg : msg;
            RefreshMenu();
        }

        void RefreshMenu()
        {
            var d = RoverDirector.Instance;
            var s = XenoarchRuntime.Session;
            if (d == null || _menuBody == null) return;
            float ship = s != null && s.Exploration != null ? s.Exploration.Ship.fuel : 0f;
            float shipCap = s != null && s.Exploration != null ? s.Exploration.Ship.fuelCapacity : 0f;
            int cr = s != null && s.Economy != null ? s.Economy.Credits : 0;
            _menuBody.text = $"Rover  {d.Fuel:0.#} / {d.FuelCapacity:0} units\nShip   {ship:0.#} / {shipCap:0} units\n{cr:N0} cr   ({RoverDirector.DefaultFuelPricePerUnit:0.#} cr per unit)";
        }

        void SetBar(VisualElement fill, Label txt, float v01, string label, bool warn)
        {
            v01 = Mathf.Clamp01(v01);
            fill.style.width = Length.Percent(v01 * 100f);
            fill.style.backgroundColor = warn ? XenoarchPalette.WarnText : new Color(0.3f, 0.85f, 1f);
            txt.text = label;
        }

        void Update()
        {
            if (_doc == null) return;
            if (!_built) Build();
            if (!_built) return;

            var d = RoverDirector.Instance;
            var rover = d != null ? d.Current : null;
            bool occupied = rover != null && rover.Occupied;
            bool loading = UIState.IsOpen(Panels.Loading);
            bool menu = UIState.IsOpen(Panel);

            if ((!occupied || loading) && menu) UIState.SetOpen(Panel, false);

            var kb = Keyboard.current;
            if (occupied && !loading && kb != null && kb.rKey.wasPressedThisFrame)
            {
                if (menu) UIState.SetOpen(Panel, false);
                else if (!UIState.BlocksGameplay) { UIState.SetOpen(Panel, true); RefreshMenu(); }
            }
            if (menu && kb != null && kb.escapeKey.wasPressedThisFrame) UIState.SetOpen(Panel, false);

            menu = UIState.IsOpen(Panel);
            XenoarchUI.Show(_root, occupied && !loading);
            XenoarchUI.Show(_cluster, occupied && !loading && !menu);
            XenoarchUI.Show(_menu, occupied && !loading && menu);
            if (!occupied || loading) return;

            if (menu) RefreshMenu();

            float fuel01 = d.Fuel01;
            _speed.text = $"{rover.SpeedKph:0} km/h";
            SetBar(_fuelFill, _fuelTxt, fuel01, $"{d.Fuel:0}/{d.FuelCapacity:0}", fuel01 <= 0.2f);
            SetBar(_boostFill, _boostTxt, rover.Boost01, $"{rover.Boost01 * 100f:0}%", false);
            if (d.Fuel <= 0.01f) _warn.text = "OUT OF FUEL — R to refuel";
            else if (fuel01 <= 0.2f) _warn.text = "LOW FUEL";
            else _warn.text = "";
        }
    }
}
