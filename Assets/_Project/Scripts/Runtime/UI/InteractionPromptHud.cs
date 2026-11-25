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

        VisualElement _root, _crosshair, _prompt, _holdTrack, _holdFill, _strip;
        Label _promptText, _hints;
        InventoryModel _boundInventory;
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
            _hints = r.Q<Label>("hints");
            if (_hints != null) _hints.text = "WASD move   Shift sprint   Space jump   E interact   F5 quick save   F9 quick load";
            _stripDirty = true;
        }

        void OnDisable()
        {
            if (_boundInventory != null) _boundInventory.Changed -= MarkDirty;
            _boundInventory = null;
        }

        void MarkDirty() => _stripDirty = true;

        void Update()
        {
            if (_root == null) return;
            XenoarchUI.Show(_root, !UIState.IsOpen(Panels.Loading));

            string prompt = interactor != null ? interactor.Prompt : null;
            bool hasPrompt = !string.IsNullOrEmpty(prompt);
            XenoarchUI.Show(_prompt, hasPrompt);
            _crosshair?.EnableInClassList("xa-crosshair--target", hasPrompt);
            if (hasPrompt && _promptText != null) _promptText.text = prompt;
            float hold = hasPrompt ? interactor.HoldProgress01 : 0f;
            XenoarchUI.Show(_holdTrack, hold > 0f);
            if (_holdFill != null) _holdFill.style.width = Length.Percent(hold * 100f);

            var inv = XenoarchRuntime.Session?.Inventory;
            if (inv != _boundInventory)
            {
                if (_boundInventory != null) _boundInventory.Changed -= MarkDirty;
                _boundInventory = inv;
                if (inv != null) inv.Changed += MarkDirty;
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
            int shown = 0;
            foreach (var slot in s.Inventory.Slots)
            {
                if (slot.IsEmpty) continue;
                if (shown++ >= maxStripSlots) break;
                var cell = new VisualElement(); cell.AddToClassList("xa-slot");
                var icon = new VisualElement(); icon.AddToClassList("xa-slot__icon");
                string name = slot.itemId;
                if (s.Items.TryGetItem(slot.itemId, out var def))
                {
                    name = def.DisplayNameOrId;
                    if (def.icon != null) icon.style.backgroundImage = new StyleBackground(def.icon);
                }
                cell.tooltip = name;
                cell.Add(icon);
                if (slot.count > 1) { var c = new Label("x" + slot.count); c.AddToClassList("xa-slot__count"); cell.Add(c); }
                var n = new Label(name); n.AddToClassList("xa-slot__name"); cell.Add(n);
                _strip.Add(cell);
            }
            if (shown == 0) { var e = new Label("Inventory empty"); e.AddToClassList("xa-hint"); _strip.Add(e); }
        }
    }
}
