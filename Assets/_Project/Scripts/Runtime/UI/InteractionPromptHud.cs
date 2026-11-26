using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// PLACEHOLDER HUD (UI Toolkit, Resources/UI/Hud.uxml): crosshair, interaction prompt + hold bar, inventory strip,
    /// control hints. Reads state only; gameplay does not depend on it.
    /// t170u: with the helmet visor up, all 8 hotbar slots are shown, split 4 lower-left and 4 lower-right inside the
    /// dark mask corners (names kept when they fit). The default single bottom row returns when the visor is off.
    /// </summary>
    public sealed class InteractionPromptHud : MonoBehaviour
    {
        public const string Uxml = "UI/Hud";
        [SerializeField] private Interactor interactor;
        [SerializeField] private int maxStripSlots = 8;
        const string HintText = "WASD move   Shift sprint   Space jump   E interact   F5 quick save   F9 quick load";

        /// <summary>Shown in the E popup when nothing is targeted (e.g. "Stand up" while seated). Null = hidden.</summary>
        public static string IdlePrompt;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => IdlePrompt = null;

        VisualElement _root, _crosshair, _prompt, _holdTrack, _holdFill, _strip, _bottom;
        VisualElement _helmetLeft, _helmetRight, _helmetLeftStrip, _helmetRightStrip;
        Label _promptText, _hints, _helmetHints;
        InventoryModel _boundInventory; Hotbar _boundHotbar;
        bool _stripDirty = true;
        bool _helmetLayout;

        void Awake() { if (interactor == null) interactor = FindAnyObjectByType<Interactor>(); }

        void OnEnable()
        {
            var doc = XenoarchUI.CreateDocument(gameObject, Uxml, 10);
            if (doc == null) { enabled = false; return; }
            var r = doc.rootVisualElement;
            _root = r.Q("hud-root");
            _crosshair = r.Q("crosshair");
            _prompt = r.Q("prompt");
            _promptText = r.Q<Label>("prompt-text");
            _holdTrack = r.Q("hold-track");
            _holdFill = r.Q("hold-fill");
            _strip = r.Q("inventory-strip");
            _bottom = r.Q("bottom");
            _hints = r.Q<Label>("hints");
            if (_hints != null) _hints.text = HintText;
            EnsureHelmetBars();
            _stripDirty = true;
        }

        void EnsureHelmetBars()
        {
            if (_root == null || _helmetLeft != null) return;
            _helmetLeft = MakeCluster("helmet-hotbar-left");
            _helmetLeftStrip = MakeStrip();
            _helmetHints = new Label(HintText) { pickingMode = PickingMode.Ignore, name = "helmet-hints" };
            _helmetHints.AddToClassList("xa-hints");
            _helmetHints.style.whiteSpace = WhiteSpace.Normal;
            _helmetLeft.Add(_helmetLeftStrip);
            _helmetLeft.Add(_helmetHints);
            _helmetRight = MakeCluster("helmet-hotbar-right");
            _helmetRightStrip = MakeStrip();
            _helmetRight.Add(_helmetRightStrip);
            _root.Add(_helmetLeft);
            _root.Add(_helmetRight);
            XenoarchUI.Show(_helmetLeft, false);
            XenoarchUI.Show(_helmetRight, false);
        }

        static VisualElement MakeCluster(string name)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        static VisualElement MakeStrip()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("xa-strip");
            e.style.marginBottom = 4;
            return e;
        }

        void OnDisable()
        {
            if (_boundInventory != null) _boundInventory.Changed -= MarkDirty;
            _boundInventory = null;
            if (_boundHotbar != null) _boundHotbar.Changed -= MarkDirty;
            _boundHotbar = null;
        }

        void MarkDirty() => _stripDirty = true;

        void Update()
        {
            if (_root == null) return;
            XenoarchUI.Show(_root, !UIState.IsOpen(Panels.Loading));

            string prompt = interactor != null ? interactor.Prompt : null;
            bool targeted = !string.IsNullOrEmpty(prompt);
            if (!targeted && !UIState.BlocksGameplay) prompt = IdlePrompt;
            bool hasPrompt = !string.IsNullOrEmpty(prompt);
            XenoarchUI.Show(_prompt, hasPrompt);
            _crosshair?.EnableInClassList("xa-crosshair--target", targeted);
            if (hasPrompt && _promptText != null) _promptText.text = prompt;
            float hold = targeted ? interactor.HoldProgress01 : 0f;
            XenoarchUI.Show(_holdTrack, hold > 0f);
            if (_holdFill != null) _holdFill.style.width = Length.Percent(hold * 100f);

            bool helmet = HelmetHud.Visible;
            if (helmet != _helmetLayout) { _helmetLayout = helmet; _stripDirty = true; }
            ApplyLayout(helmet);

            var inv = XenoarchRuntime.Session?.Inventory;
            if (inv != _boundInventory)
            {
                if (_boundInventory != null) _boundInventory.Changed -= MarkDirty;
                _boundInventory = inv;
                if (inv != null) inv.Changed += MarkDirty;
                _stripDirty = true;
            }
            var bar = XenoarchRuntime.Session?.Hotbar;
            if (bar != _boundHotbar)
            {
                if (_boundHotbar != null) _boundHotbar.Changed -= MarkDirty;
                _boundHotbar = bar;
                if (bar != null) bar.Changed += MarkDirty;
                _stripDirty = true;
            }
            if (_stripDirty) RebuildStrip();
        }

        void ApplyLayout(bool helmet)
        {
            EnsureHelmetBars();
            if (_bottom != null)
            {
                // Helmet off: the original full-width bottom bar. Helmet on: hide it so nothing is left
                // sitting on the visor glass (t169u stray fragments).
                XenoarchUI.Show(_bottom, !helmet);
                _bottom.style.bottom = 0;
                _bottom.style.paddingLeft = new StyleLength(StyleKeyword.Null);
            }
            if (_helmetLeft == null) return;
            XenoarchUI.Show(_helmetLeft, helmet);
            XenoarchUI.Show(_helmetRight, helmet);
            if (!helmet) return;
            var g = HudLayout.HelmetHotbar();
            // Anchor both clusters to the bottom corners so they stay inside the dark crescent.
            _helmetLeft.style.left = g.xLeft;
            _helmetLeft.style.right = StyleKeyword.Auto;
            _helmetLeft.style.bottom = g.padEdge;
            _helmetLeft.style.top = StyleKeyword.Auto;
            _helmetLeft.style.width = g.width + 12f + g.hintMaxWidth;
            _helmetLeft.style.height = g.height;
            _helmetRight.style.left = StyleKeyword.Auto;
            _helmetRight.style.right = g.padEdge;
            _helmetRight.style.bottom = g.padEdge;
            _helmetRight.style.top = StyleKeyword.Auto;
            _helmetRight.style.width = g.width;
            _helmetRight.style.height = g.height;
            if (_helmetHints != null)
            {
                _helmetHints.style.position = Position.Absolute;
                _helmetHints.style.left = g.width + 10f;
                _helmetHints.style.bottom = 0;
                _helmetHints.style.top = StyleKeyword.Auto;
                _helmetHints.style.width = g.hintMaxWidth;
                _helmetHints.style.marginLeft = 0;
            }
        }

        void RebuildStrip()
        {
            _stripDirty = false;
            var s = XenoarchRuntime.Session;
            if (_helmetLayout)
            {
                var g = HudLayout.HelmetHotbar();
                FillStrip(_helmetLeftStrip, s, 0, 4, true, g);
                FillStrip(_helmetRightStrip, s, 4, 4, true, g);
                return;
            }
            if (_strip == null) return;
            _strip.Clear();
            if (s == null) return;
            int shown = 0;
            for (int k = 0; k < Hotbar.Size && k < maxStripSlots; k++)
            {
                string id = s.Hotbar.At(k);
                if (id == null) continue;
                shown++;
                _strip.Add(MakeSlot(s, k, id, false, default));
            }
            if (shown == 0) { var e = new Label("Hotbar empty: drag items onto it in the inventory (I)"); e.AddToClassList("xa-hint"); _strip.Add(e); }
        }

        void FillStrip(VisualElement strip, GameSession s, int start, int count, bool showEmpty, HudLayout.CornerHotbar g)
        {
            if (strip == null) return;
            strip.Clear();
            if (s == null) return;
            for (int i = 0; i < count; i++)
            {
                int k = start + i;
                if (k >= Hotbar.Size || k >= maxStripSlots) break;
                string id = s.Hotbar.At(k);
                if (id == null && !showEmpty) continue;
                strip.Add(MakeSlot(s, k, id, true, g));
            }
        }

        VisualElement MakeSlot(GameSession s, int k, string id, bool helmet, HudLayout.CornerHotbar g)
        {
            var cell = new VisualElement(); cell.AddToClassList("xa-slot");
            if (helmet)
            {
                cell.style.width = g.slot;
                cell.style.height = g.showNames ? g.slot + Mathf.Round(g.slot * 0.40f) : g.slot;
                cell.style.marginRight = k == 3 || k == 7 ? 0 : g.gap;
                cell.style.paddingTop = cell.style.paddingBottom = 0;
                cell.style.paddingLeft = cell.style.paddingRight = 0;
            }
            var icon = new VisualElement(); icon.AddToClassList("xa-slot__icon");
            if (helmet)
            {
                // Fill almost the whole fitted slot (t180u). A 6 px inset leaves a hair for the key and stack count.
                float iconPx = Mathf.Max(24f, g.slot - 6f);
                icon.style.width = iconPx; icon.style.height = iconPx;
            }
            string name = id;
            int count = 0;
            if (!string.IsNullOrEmpty(id) && s != null)
            {
                count = s.Inventory.Count(id);
                if (s.Items.TryGetItem(id, out var def))
                {
                    name = def.DisplayNameOrId;
                    if (def.icon != null) XenoarchIcons.ApplyItem(icon, def);
                }
                if (count <= 0) cell.style.opacity = 0.4f;
                cell.tooltip = count > 0 ? $"{k + 1}: {name}" : $"{k + 1}: {name} (not owned yet)";
            }
            else
            {
                cell.style.opacity = 0.35f;
                cell.tooltip = $"{k + 1}: empty";
                name = "";
            }
            cell.Add(icon);
            var key = new Label((k + 1).ToString()); key.AddToClassList("xa-slot__count"); key.style.left = 3; key.style.right = StyleKeyword.Auto; key.style.top = 1; key.style.bottom = StyleKeyword.Auto; cell.Add(key);
            if (count > 1) { var c = new Label("x" + count); c.AddToClassList("xa-slot__count"); cell.Add(c); }
            if (!string.IsNullOrEmpty(name) && (!helmet || g.showNames))
            {
                var n = new Label(name); n.AddToClassList("xa-slot__name");
                if (helmet)
                {
                    n.style.width = g.slot;
                    n.style.fontSize = Mathf.Clamp(Mathf.Round(g.slot * 0.16f), 8f, 11f);
                    n.style.height = Mathf.Round(g.slot * 0.40f);
                }
                cell.Add(n);
            }
            return cell;
        }
    }
}
