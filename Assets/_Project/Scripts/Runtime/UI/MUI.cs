using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// t246u Morpheus-style UI building blocks (approved mockups). Only adds elements and .xa-m-* classes; every size and
    /// colour lives in Resources/UI/Xenoarch.uss (theme section) and XenoarchPalette.uss. USS has no text-transform, so
    /// labels that should read in capitals are upper-cased here.
    /// </summary>
    public static class MUI
    {
        public static Label L(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        /// <summary>Keyboard / controller key chip (e.g. "Q", "Esc"). Teal = the primary action.</summary>
        public static Label Key(VisualElement parent, string key, bool teal = false)
        {
            var k = L(parent, key, "xa-m-key");
            if (teal) k.AddToClassList("xa-m-key--teal");
            k.pickingMode = PickingMode.Ignore;
            return k;
        }

        /// <summary>Bottom-right key legend: pairs of (key, action).</summary>
        public static VisualElement Legend(VisualElement parent, params (string key, string action)[] items)
        {
            var row = new VisualElement(); row.AddToClassList("xa-m-legend"); row.pickingMode = PickingMode.Ignore;
            foreach (var (key, action) in items)
            {
                var it = new VisualElement(); it.AddToClassList("xa-m-legend__item");
                Key(it, key);
                L(it, action.ToUpperInvariant(), "xa-m-legend__text");
                row.Add(it);
            }
            parent?.Add(row);
            return row;
        }

        /// <summary>Section header bar: title left, optional dim text right.</summary>
        public static VisualElement Head(VisualElement parent, string title, string right = null)
        {
            var h = new VisualElement(); h.AddToClassList("xa-m-head");
            L(h, title.ToUpperInvariant(), "xa-m-head__title");
            if (!string.IsNullOrEmpty(right)) L(h, right, "xa-m-head__right");
            parent?.Add(h);
            return h;
        }

        public static Label Badge(VisualElement parent, string text, string variant = null)
        {
            var b = L(parent, text.ToUpperInvariant(), "xa-m-badge");
            if (!string.IsNullOrEmpty(variant)) b.AddToClassList("xa-m-badge--" + variant);
            return b;
        }

        /// <summary>Thin progress bar (fill 0-1).</summary>
        public static VisualElement Bar(VisualElement parent, float fill01, bool teal = false, bool thin = false)
        {
            var bar = new VisualElement(); bar.AddToClassList("xa-m-bar");
            if (teal) bar.AddToClassList("xa-m-bar--teal");
            if (thin) bar.AddToClassList("xa-m-bar--thin");
            var f = new VisualElement(); f.AddToClassList("xa-m-bar__fill");
            f.style.width = Length.Percent(Mathf.Clamp01(fill01) * 100f);
            bar.Add(f);
            parent?.Add(bar);
            return bar;
        }

        /// <summary>"57%  [=====-----]  4 / 7" row: percent left, bar, count right (Morpheus progress style).</summary>
        public static VisualElement BarRow(VisualElement parent, float fill01, string count, bool teal = false)
        {
            var row = new VisualElement(); row.AddToClassList("xa-m-barrow");
            L(row, Mathf.RoundToInt(Mathf.Clamp01(fill01) * 100f) + "%", "xa-m-barrow__pct");
            Bar(row, fill01, teal);
            if (count != null) L(row, count, "xa-m-barrow__count");
            parent?.Add(row);
            return row;
        }

        /// <summary>Full-width confirm band over <paramref name="host"/>'s panel (Morpheus "Are you sure?").</summary>
        public static void ConfirmBand(VisualElement host, string title, string text, string yes, Action onYes, string no = "Back", Action onNo = null)
        {
            var root = host?.panel?.visualTree ?? host;
            if (root == null) return;
            var veil = new VisualElement(); veil.AddToClassList("xa-m-band-veil");
            XenoarchIcons.EnsureStyles(veil);
            var band = new VisualElement(); band.AddToClassList("xa-m-band");
            L(band, title.ToUpperInvariant(), "xa-m-band__title");
            if (!string.IsNullOrEmpty(text)) L(band, text, "xa-m-band__text");
            var btns = new VisualElement(); btns.AddToClassList("xa-m-band__buttons");
            var y = new Button(() => { veil.RemoveFromHierarchy(); onYes?.Invoke(); }) { text = yes.ToUpperInvariant() };
            y.AddToClassList("xa-term__btn"); y.AddToClassList("xa-m-band__btn"); btns.Add(y);
            var n = new Button(() => { veil.RemoveFromHierarchy(); onNo?.Invoke(); }) { text = no.ToUpperInvariant() };
            n.AddToClassList("xa-term__btn"); n.AddToClassList("xa-term__btn--ghost"); n.AddToClassList("xa-m-band__btn"); btns.Add(n);
            band.Add(btns);
            veil.Add(band);
            root.Add(veil);
            y.Focus();
        }

        /// <summary>Tab bar with Q / E chips at its ends. Returns the bar; <paramref name="select"/> gets the tab id.</summary>
        public static VisualElement Tabs(VisualElement parent, IList<(string id, string label, string glyph)> tabs, string active, Action<string> select, VisualElement right = null)
        {
            var bar = new VisualElement(); bar.AddToClassList("xa-m-tabs");
            Key(bar, "Q");
            foreach (var t in tabs)
            {
                var id = t.id;
                var b = new Button(() => select?.Invoke(id)) { text = "" };
                b.AddToClassList("xa-m-tab");
                if (id == active) b.AddToClassList("xa-m-tab--on");
                if (!string.IsNullOrEmpty(t.glyph)) b.Add(XenoarchIcons.Glyph(t.glyph, "xa-m-tab__icon"));
                var l = new Label(t.label.ToUpperInvariant()) { pickingMode = PickingMode.Ignore }; b.Add(l);
                bar.Add(b);
            }
            Key(bar, "E").style.marginLeft = 6;
            var sp = new VisualElement(); sp.AddToClassList("xa-m-tabs__spacer"); bar.Add(sp);
            if (right != null) bar.Add(right);
            parent?.Add(bar);
            return bar;
        }

        /// <summary>Next / previous tab id for Q / E cycling.</summary>
        public static string Cycle(IList<(string id, string label, string glyph)> tabs, string active, int dir)
        {
            int i = 0;
            for (int k = 0; k < tabs.Count; k++) if (tabs[k].id == active) { i = k; break; }
            i = (i + dir + tabs.Count) % tabs.Count;
            return tabs[i].id;
        }

        /// <summary>Percent of an objective's lines that are complete (1 when the objective is complete).</summary>
        public static float Progress(DMI.Xenoarch.Objectives.ObjectiveView v, out int done, out int total)
        {
            done = 0; total = 0;
            if (v == null) return 0f;
            foreach (var l in v.lines) { total++; if (l.complete) done++; }
            if (v.complete) { if (total == 0) total = done = 1; return 1f; }
            return total > 0 ? (float)done / total : 0f;
        }
    }
}
