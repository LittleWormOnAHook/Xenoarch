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
    /// </summary>
    public sealed class InteractionPromptHud : MonoBehaviour
    {
        public const string Uxml = "UI/Hud";
        [SerializeField] private Interactor interactor;
        [SerializeField] private int maxStripSlots = 8;

        /// <summary>Shown in the E popup when nothing is targeted (e.g. "Stand up" while seated). Null = hidden.</summary>
        public static string IdlePrompt;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => IdlePrompt = null;

        VisualElement _root, _crosshair, _prompt, _holdTrack, _holdFill, _strip, _bottom;
        Label _promptText, _hints;
        InventoryModel _boundInventory; Hotbar _boundHotbar;
        bool _stripDirty = true;

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
            if (_hints != null) _hints.text = "WASD move   Shift sprint   Space jump   E interact   F5 quick save   F9 quick load";
            _stripDirty = true;
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

            // Helmet visor up: lift the hotbar above the suit pod and in from the curved frame.
            if (_bottom != null)
            {
                _bottom.style.bottom = Length.Percent(HudLayout.BottomInset * 100f);
                _bottom.style.paddingLeft = HudLayout.BottomInset > 0f ? Length.Percent(7f) : new StyleLength(StyleKeyword.Null);
            }

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

        void RebuildStrip()
        {
            _stripDirty = false;
            if (_strip == null) return;
            _strip.Clear();
            var s = XenoarchRuntime.Session;
            if (s == null) return;
            // Hotbar (keys 1-8): assigned by dragging items onto it in the inventory window (I).
            int shown = 0;
            for (int k = 0; k < Hotbar.Size && k < maxStripSlots; k++)
            {
                string id = s.Hotbar.At(k);
                if (id == null) continue;
                int count = s.Inventory.Count(id);
                shown++;
                var cell = new VisualElement(); cell.AddToClassList("xa-slot");
                var icon = new VisualElement(); icon.AddToClassList("xa-slot__icon");
                string name = id;
                if (s.Items.TryGetItem(id, out var def))
                {
                    name = def.DisplayNameOrId;
                    if (def.icon != null) XenoarchIcons.ApplyItem(icon, def);
                }
                if (count <= 0) cell.style.opacity = 0.4f;
                cell.tooltip = $"{k + 1}: {name}";
                cell.Add(icon);
                var key = new Label((k + 1).ToString()); key.AddToClassList("xa-slot__count"); key.style.left = 3; key.style.right = StyleKeyword.Auto; key.style.top = 1; key.style.bottom = StyleKeyword.Auto; cell.Add(key);
                if (count > 1) { var c = new Label("x" + count); c.AddToClassList("xa-slot__count"); cell.Add(c); }
                var n = new Label(name); n.AddToClassList("xa-slot__name"); cell.Add(n);
                _strip.Add(cell);
            }
            if (shown == 0) { var e = new Label("Hotbar empty: drag items onto it in the inventory (I)"); e.AddToClassList("xa-hint"); _strip.Add(e); }
        }
    }
}
