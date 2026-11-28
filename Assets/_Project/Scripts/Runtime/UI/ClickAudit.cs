#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Ship;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Editor-only scripted click audit (tooling: runs only when Temp/xenoarch_clickaudit.run exists, which the
    /// XenoarchClickAuditRunner editor script writes before entering Play). It (1) hit-tests every clickable element at its
    /// centre and edges with panel.Pick and compares against the intended target, and (2) injects REAL Input System mouse
    /// events (the same path a player's mouse takes through UI Toolkit) at known positions and checks the clicks / hovers /
    /// right-clicks land. Results: Logs/xenoarch_clickaudit.txt. Stops Play via Temp/xenoarch_stopplay.trigger when done.
    /// </summary>
    public sealed class ClickAudit : MonoBehaviour
    {
        const string RunFlag = "Temp/xenoarch_clickaudit.run";
        const string OutFile = "Logs/xenoarch_clickaudit.txt";
        const string StopTrigger = "Temp/xenoarch_stopplay.trigger";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!File.Exists(RunFlag) || FindFirstObjectByType<ClickAudit>() != null) return;
            var go = new GameObject("[ClickAudit]");
            DontDestroyOnLoad(go);
            go.AddComponent<ClickAudit>();
        }

        readonly StringBuilder _sb = new StringBuilder();
        string _res = "?";
        int _pass, _fail, _skip;
        bool _flip = true;
        InputSettings.EditorInputBehaviorInPlayMode _prevBehavior;

        void Rec(string area, string test, string result, string detail = "")
        {
            if (result == "PASS") _pass++; else if (result == "FAIL") _fail++; else if (result == "SKIP") _skip++;
            string line = $"{_res} | {area} | {test} | {result} | {detail}";
            _sb.AppendLine(line);
            Debug.Log("[ClickAudit] " + line);
        }

        IEnumerator Start()
        {
            _res = File.ReadAllText(RunFlag).Trim();
            _prevBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _sb.AppendLine($"=== {DateTime.Now:HH:mm:ss} run {_res}: Screen {Screen.width}x{Screen.height} dpi {Screen.dpi:0} ===");
            yield return Wait(2f);
            yield return Safe(MainMenuPhase(), "MainMenu");
            yield return Safe(LoadGamePhase(), "LoadGame");
            yield return Safe(PausePhase(), "Pause");
            yield return Safe(InventoryPhase(), "Inventory");
            yield return Safe(TerminalPhase<RefinerTerminal>("Refiner", true), "Refiner");
            yield return Safe(TerminalPhase<ResearchTerminal>("Research", false), "Research");
            yield return Safe(TerminalPhase<StorageTerminal>("Crates/Storage", false), "Storage");
            yield return Safe(NavPhase(), "Nav");
            Rec("Cockpit", "cockpit screens", "SKIP", "not exercised by this harness (needs seated player + Alt cursor)");
            _sb.AppendLine($"--- {_res}: {_pass} pass, {_fail} fail, {_skip} skipped ---");
            try { Directory.CreateDirectory("Logs"); File.AppendAllText(OutFile, _sb.ToString()); } catch { }
            InputSystem.settings.editorInputBehaviorInPlayMode = _prevBehavior;
            try { File.Delete(RunFlag); File.WriteAllText(StopTrigger, ""); } catch { }
        }

        IEnumerator Safe(IEnumerator e, string name)
        {
            while (true)
            {
                bool more;
                try { more = e.MoveNext(); }
                catch (Exception ex) { Rec(name, "phase", "FAIL", "exception: " + ex.GetType().Name + " " + ex.Message); yield break; }
                if (!more) yield break;
                yield return e.Current;
            }
        }

        static readonly PropertyInfo PseudoProp = typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static bool Hovered(VisualElement e) => PseudoProp != null && PseudoProp.GetValue(e, null).ToString().Contains("Hover");

        static IEnumerator Wait(float s) { yield return new WaitForSecondsRealtime(s); }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static IEnumerator Until(Func<bool> c, float timeout)
        {
            float t = 0f;
            while (!c() && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
        }

        // ------------------------------------------------------------------ UI discovery + picking
        static List<UIDocument> Docs()
        {
            var l = new List<UIDocument>();
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var r = d.rootVisualElement;
                if (r == null || r.panel == null) continue;
                if (r.resolvedStyle.display == DisplayStyle.None) continue;
                l.Add(d);
            }
            return l;
        }

        static bool Visible(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden || p.resolvedStyle.opacity < 0.05f) return false;
            return true;
        }

        static Rect VisibleRect(VisualElement e)
        {
            var r = e.worldBound;
            for (var p = e.parent; p != null; p = p.parent)
            {
                var b = p.worldBound;
                float x0 = Mathf.Max(r.xMin, b.xMin), y0 = Mathf.Max(r.yMin, b.yMin), x1 = Mathf.Min(r.xMax, b.xMax), y1 = Mathf.Min(r.yMax, b.yMax);
                if (x1 <= x0 || y1 <= y0) return Rect.zero;
                r = Rect.MinMaxRect(x0, y0, x1, y1);
            }
            return r;
        }

        static bool IsCandidate(VisualElement e) =>
            e.pickingMode == PickingMode.Position &&
            (e is Button || e.ClassListContains("xa-item-slot") || e.ClassListContains("xa-term__row") || e.ClassListContains("xa-recipe"));

        static string Desc(VisualElement e)
        {
            if (e == null) return "(none)";
            var cls = new List<string>(e.GetClasses());
            string txt = e is TextElement te && !string.IsNullOrEmpty(te.text) ? " '" + te.text.Replace("\n", " ") + "'" : "";
            return e.GetType().Name + (string.IsNullOrEmpty(e.name) ? "" : "#" + e.name) + (cls.Count > 0 ? "." + string.Join(".", cls) : "") + txt;
        }

        static string Label(VisualElement e)
        {
            var tx = e.Q<TextElement>();
            string t = e is TextElement te && !string.IsNullOrEmpty(te.text) ? te.text : tx != null ? tx.text : "";
            if (string.IsNullOrEmpty(t)) t = e.tooltip;
            if (t != null && t.Length > 24) t = t.Substring(0, 24);
            return (t ?? "").Replace("\n", " ");
        }

        /// <summary>Hit-test centre + 4 edge-inset points of every visible clickable and compare with the picked element.</summary>
        void AuditPick(string area)
        {
            int total = 0, bad = 0, pts = 0;
            var fails = new List<string>();
            foreach (var d in Docs())
            {
                var root = d.rootVisualElement;
                var panel = root.panel;
                var all = root.Query<VisualElement>().ToList();
                foreach (var e in all)
                {
                    if (!IsCandidate(e) || !Visible(e)) continue;
                    var vr = VisibleRect(e);
                    if (vr.width < 6f || vr.height < 6f) continue;
                    var wb = e.worldBound;
                    if (vr.width < wb.width * 0.5f || vr.height < wb.height * 0.5f) continue; // mostly scrolled out of view
                    total++;
                    float ix = Mathf.Min(4f, vr.width * 0.25f), iy = Mathf.Min(4f, vr.height * 0.25f);
                    var points = new[]
                    {
                        vr.center,
                        new Vector2(vr.xMin + ix, vr.center.y), new Vector2(vr.xMax - ix, vr.center.y),
                        new Vector2(vr.center.x, vr.yMin + iy), new Vector2(vr.center.x, vr.yMax - iy),
                    };
                    bool elemBad = false;
                    foreach (var p in points)
                    {
                        pts++;
                        var hit = panel.Pick(p);
                        bool ok = hit != null && (hit == e || e.Contains(hit));
                        if (!ok)
                        {
                            elemBad = true;
                            if (fails.Count < 12) fails.Add($"{Desc(e)} @({p.x:0},{p.y:0}) picked {Desc(hit)}");
                        }
                    }
                    if (elemBad) bad++;
                }
            }
            if (total == 0) { Rec(area, "pick hit-test", "SKIP", "no visible clickable elements found"); return; }
            Rec(area, $"pick hit-test (centre+4 edges, {pts} points)", bad == 0 ? "PASS" : "FAIL", $"{total} elements, {bad} with a wrong pick" + (fails.Count > 0 ? "; " + string.Join(" || ", fails) : ""));
        }

        // ------------------------------------------------------------------ real input injection
        Vector2 PanelToScreen(IPanel panel, Vector2 pp)
        {
            Vector2 G(float sx, float sy) => RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sx, _flip ? Screen.height - sy : sy));
            var p0 = G(0f, 0f);
            float kx = (G(1000f, 0f).x - p0.x) / 1000f;
            float ky = (G(0f, 1000f).y - p0.y) / 1000f;
            if (Mathf.Abs(kx) < 1e-6f) kx = 1f;
            if (Mathf.Abs(ky) < 1e-6f) ky = 1f;
            return new Vector2((pp.x - p0.x) / kx, (pp.y - p0.y) / ky);
        }

        static IEnumerator MouseTo(Vector2 screen)
        {
            var m = Mouse.current;
            if (m == null) yield break;
            InputSystem.QueueStateEvent(m, new MouseState { position = screen + new Vector2(1f, 1f) });
            yield return Wait(0.06f);
            InputSystem.QueueStateEvent(m, new MouseState { position = screen });
            yield return Wait(0.06f);
        }

        IEnumerator ClickAt(IPanel panel, Vector2 panelPos, int button = 0)
        {
            var m = Mouse.current;
            if (m == null) yield break;
            var s = PanelToScreen(panel, panelPos);
            yield return MouseTo(s);
            var down = new MouseState { position = s }.WithButton(button == 0 ? UnityEngine.InputSystem.LowLevel.MouseButton.Left : UnityEngine.InputSystem.LowLevel.MouseButton.Right, true);
            InputSystem.QueueStateEvent(m, down);
            yield return Wait(0.1f);
            InputSystem.QueueStateEvent(m, new MouseState { position = s });
            yield return Wait(0.12f);
        }

        // ------------------------------------------------------------------ phases
        IEnumerator MainMenuPhase()
        {
            yield return Wait(1f);
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                yield return Until(() => Docs().Exists(d => d.rootVisualElement.Q<Button>() != null), 60f);
                AuditPick("MainMenu");
            }
            else Rec("MainMenu", "main menu audit", "SKIP", "Play started in scene " + SceneManager.GetActiveScene().name + ", not the menu");
            yield return GridTest();
        }

        /// <summary>A throwaway UI document with a 3x3 grid of buttons (corners / edges / centre of the panel); real clicks at the centre and near the edges of each.</summary>
        IEnumerator GridTest()
        {
            var go = new GameObject("[ClickAuditGrid]");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 9000;
            var root = doc.rootVisualElement;
            XenoarchUI.FillRoot(root);
            root.pickingMode = PickingMode.Ignore;
            yield return Frames(3);
            float W = root.worldBound.width, H = root.worldBound.height;
            if (W < 100f || H < 100f) { Rec("Grid", "panel size", "FAIL", $"{W}x{H}"); Destroy(go); yield break; }
            var btns = new List<(Button b, string name, int[] count)>();
            float bw = 120f, bh = 56f, m = 6f;
            float[] xs = { m, (W - bw) * 0.5f, W - bw - m }, ys = { m, (H - bh) * 0.5f, H - bh - m };
            string[] cn = { "left", "centre", "right" }, rn = { "top", "middle", "bottom" };
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    var b = new Button { text = rn[r] + "-" + cn[c] };
                    b.AddToClassList("xa-term__btn");
                    b.style.position = Position.Absolute; b.style.left = xs[c]; b.style.top = ys[r]; b.style.width = bw; b.style.height = bh;
                    int[] cnt = { 0 };
                    b.clicked += () => cnt[0]++;
                    root.Add(b);
                    btns.Add((b, rn[r] + "-" + cn[c], cnt));
                }
            yield return Frames(4);
            // calibrate the y convention with the centre button
            var cb = btns[4];
            yield return ClickAt(root.panel, cb.b.worldBound.center);
            if (cb.count[0] == 0)
            {
                _flip = !_flip;
                yield return ClickAt(root.panel, cb.b.worldBound.center);
                if (cb.count[0] == 0) _flip = !_flip; // neither worked: restore, tests below will report FAIL
                else Rec("Grid", "screen->panel convention", "INFO", "raw (unflipped) y mapped correctly");
            }
            foreach (var (b, name, count) in btns)
            {
                var wb = b.worldBound;
                var spots = new (string n, Vector2 p)[] { ("centre", wb.center), ("left-edge+3px", new Vector2(wb.xMin + 3f, wb.center.y)), ("bottom-right+3px", new Vector2(wb.xMax - 3f, wb.yMax - 3f)) };
                int before = count[0], ok = 0; var miss = new List<string>();
                foreach (var (n, p) in spots)
                {
                    int c0 = count[0];
                    yield return ClickAt(root.panel, p);
                    if (count[0] == c0 + 1) ok++; else miss.Add($"{n} -> {count[0] - c0} clicks");
                }
                Rec("Grid(real input)", $"button {name} (panel {wb.center.x:0},{wb.center.y:0})", ok == 3 ? "PASS" : "FAIL", ok == 3 ? "3/3 clicks (centre + 2 edge points)" : string.Join("; ", miss));
            }
            // hover: pointer over the centre button must set :hover on that button only
            yield return MouseTo(PanelToScreen(root.panel, cb.b.worldBound.center));
            yield return Frames(3);
            bool hov = Hovered(cb.b);
            Rec("Grid(real input)", "hover pseudo-state on centre button", hov ? "PASS" : "FAIL", hov ? "" : "no :hover after pointer move");
            Destroy(go);
            yield return Frames(2);
        }

        IEnumerator LoadGamePhase()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu") { Rec("LoadGame", "Continue", "SKIP", "not on the main menu"); yield break; }
            Button cont = null;
            foreach (var d in Docs()) foreach (var b in d.rootVisualElement.Query<Button>().ToList()) if (Label(b).StartsWith("Continue")) cont = b;
            if (cont == null) { Rec("LoadGame", "Continue", "SKIP", "no Continue button (no save)"); yield break; }
            // real left-click on Continue (also a click test on the real menu button)
            bool fired = false; cont.clicked += () => fired = true;
            yield return ClickAt(cont.panel, cont.worldBound.center);
            Rec("MainMenu", "real click on Continue", fired ? "PASS" : "FAIL", fired ? "clicked event fired" : "no click received");
            if (!fired) { var ev = NavigationSubmitEvent.GetPooled(); ev.target = cont; cont.SendEvent(ev); }
            yield return Wait(2f);
            yield return Until(() => SceneManager.GetActiveScene().name != "MainMenu", 60f);
            yield return Until(() => !DMI.Xenoarch.Scenes.SceneLoader.Busy, 240f);
            yield return Wait(6f);
            Rec("LoadGame", "scene after Continue", SceneManager.GetActiveScene().name != "MainMenu" ? "PASS" : "FAIL", SceneManager.GetActiveScene().name);
        }

        IEnumerator PausePhase()
        {
            var pm = FindFirstObjectByType<PauseMenu>();
            if (pm == null) { Rec("PauseMenu", "open", "SKIP", "no PauseMenu in scene"); yield break; }
            pm.Open();
            yield return Wait(1.2f);
            Rec("PauseMenu", "open", pm.IsOpen ? "PASS" : "FAIL");
            AuditPick("PauseMenu");
            pm.Close();
            yield return Wait(0.5f);
        }

        static object GetField(object o, string n)
        {
            var t = o is Type tt ? tt : o.GetType();
            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return f?.GetValue(o is Type ? null : o);
        }

        static string InspectorItem()
        {
            var inst = GetField(typeof(ItemInspector), "_i");
            if (inst == null) return null;
            var st = GetField(inst, "_stack");
            return st == null ? null : (string)GetField(st, "itemId");
        }

        List<VisualElement> Tiles(Func<VisualElement, bool> pred)
        {
            var l = new List<VisualElement>();
            foreach (var d in Docs()) foreach (var e in d.rootVisualElement.Query<VisualElement>(className: "xa-item-slot").ToList())
                if (e.pickingMode == PickingMode.Position && Visible(e) && pred(e)) l.Add(e);
            return l;
        }

        IEnumerator InventoryPhase()
        {
            var inv = FindFirstObjectByType<InventoryWindow>();
            if (inv == null || XenoarchRuntime.Session == null) { Rec("Inventory", "open", "SKIP", "no InventoryWindow / session"); yield break; }
            inv.Open();
            yield return Wait(1.2f);
            Rec("Inventory", "open", inv.IsOpen ? "PASS" : "FAIL");
            if (!inv.IsOpen) yield break;
            AuditPick("Inventory");
            var panel = Docs()[0].rootVisualElement.panel;

            // non-empty backpack tiles are 80px with at least one child and a tooltip
            Func<VisualElement, bool> filled = e => e.childCount > 0 && !string.IsNullOrEmpty(e.tooltip) && e.tooltip.Contains("Right-click: Inspect, Move") && e.resolvedStyle.width > 70f && VisibleRect(e).width > 40f;
            // hotbar slot 1 (scanner): right-click inspects via the item definition (no backpack item needed)
            {
                VisualElement hb = null;
                foreach (var t in Tiles(e => true))
                    foreach (var l in t.Query<Label>().ToList()) if (l.text == "1" && l.style.position == Position.Absolute) hb = t;
                if (hb == null) Rec("Inventory", "hotbar slot 1 right-click inspect", "SKIP", "hotbar slot 1 not found");
                else
                {
                    yield return ClickAt(panel, VisibleRect(hb).center, 1);
                    yield return Wait(0.6f);
                    Rec("Inventory", "hotbar slot 1 right-click opens inspector", ItemInspector.IsOpen ? "PASS" : "FAIL", "item=" + InspectorItem());
                    if (ItemInspector.IsOpen)
                    {
                        AuditPick("Inspector (docked)");
                        foreach (var d in Docs())
                        {
                            var side = d.rootVisualElement.Q(className: "xa-inspect__side");
                            if (side == null) continue;
                            float a = side.resolvedStyle.backgroundColor.a;
                            Rec("Inspector", "specs panel opaque", a > 0.99f ? "PASS" : "FAIL", $"bg alpha {a:0.00}");
                            Rec("Inspector", "no blocking dim", d.rootVisualElement.Q(className: "xa-inspect-dim") == null ? "PASS" : "FAIL");
                            var modal = d.rootVisualElement.Q(className: "xa-inspect");
                            if (modal != null) Rec("Inspector", "docked panel clear of the clicked slot", !modal.worldBound.Overlaps(hb.worldBound) ? "PASS" : "FAIL", $"panel x {modal.worldBound.xMin:0}-{modal.worldBound.xMax:0}, slot x {hb.worldBound.xMin:0}-{hb.worldBound.xMax:0}");
                        }
                        ItemInspector.Close();
                        yield return Wait(0.4f);
                    }
                }
            }
            var tiles = Tiles(filled);
            if (tiles.Count < 1) { Rec("Inventory", "backpack slot click / context menu / inspector retarget", "SKIP", "backpack is empty in this save: nothing to click (not modified to avoid touching the save)"); }
            else
            {
                var t0 = tiles[0];
                yield return ClickAt(panel, VisibleRect(t0).center, 0);
                yield return Frames(2);
                Rec("Inventory", "left-click slot (select + refresh)", t0.panel == null ? "PASS" : "FAIL", t0.panel == null ? "tile handler ran (window rebuilt)" : "tile unchanged: click not delivered");
                tiles = Tiles(filled);
                if (tiles.Count > 0)
                {
                    var t1 = tiles[0];
                    yield return ClickAt(panel, VisibleRect(t1).center, 1);
                    yield return Frames(2);
                    bool menu = ItemContextMenu.AnyOpen || ItemInspector.IsOpen;
                    Rec("Inventory", "right-click slot opens context menu", ItemContextMenu.AnyOpen ? "PASS" : "FAIL", "menuOpen=" + ItemContextMenu.AnyOpen);
                    if (ItemContextMenu.AnyOpen)
                    {
                        Button insp = null;
                        foreach (var d in Docs()) foreach (var b in d.rootVisualElement.Query<Button>().ToList()) if (Label(b) == "Inspect" && Visible(b) && b.parent != null && b.parent.ClassListContains("xa-ctx")) insp = b;
                        if (insp == null) Rec("Inventory", "context menu Inspect button", "FAIL", "not found");
                        else
                        {
                            AuditPick("Inventory context menu");
                            yield return ClickAt(panel, insp.worldBound.center, 0);
                            yield return Wait(0.6f);
                            Rec("Inventory", "Inspect button opens inspector", ItemInspector.IsOpen ? "PASS" : "FAIL");
                        }
                    }
                    if (ItemInspector.IsOpen)
                    {
                        AuditPick("Inspector (docked)");
                        string first = InspectorItem();
                        // opaque side panel + no dim
                        foreach (var d in Docs())
                        {
                            var side = d.rootVisualElement.Q(className: "xa-inspect__side");
                            if (side != null)
                            {
                                float a = side.resolvedStyle.backgroundColor.a;
                                Rec("Inspector", "specs panel opaque", a > 0.99f ? "PASS" : "FAIL", $"bg alpha {a:0.00}");
                                Rec("Inspector", "no blocking dim", d.rootVisualElement.Q(className: "xa-inspect-dim") == null ? "PASS" : "FAIL");
                            }
                        }
                        // retarget: right-click a different item while the inspector is open (no Close, no menu)
                        tiles = Tiles(filled);
                        VisualElement other = null;
                        foreach (var t in tiles) if (t.tooltip.Split('\n')[0] != t1.tooltip.Split('\n')[0] && Visible(t)) { other = t; break; }
                        if (other == null) Rec("Inspector", "right-click retarget", "SKIP", "only one distinct item in backpack");
                        else
                        {
                            var r = VisibleRect(other);
                            var hit = panel.Pick(r.center);
                            if (hit == null || !(hit == other || other.Contains(hit))) Rec("Inspector", "right-click retarget", "FAIL", "second slot is covered/blocked: picked " + Desc(hit));
                            else
                            {
                                yield return ClickAt(panel, r.center, 1);
                                yield return Wait(0.5f);
                                string now = InspectorItem();
                                Rec("Inspector", "right-click another item retargets (no menu, no close)", (now != null && now != first && !ItemContextMenu.AnyOpen) ? "PASS" : "FAIL", $"{first} -> {now}, menuOpen={ItemContextMenu.AnyOpen}");
                            }
                        }
                        // hotbar slot 1 (scanner) inspect via definition
                        VisualElement hb1 = null;
                        foreach (var t in Tiles(e => true))
                            foreach (var l in t.Query<Label>().ToList()) if (l.text == "1" && l.style.position == Position.Absolute) hb1 = t;
                        if (hb1 == null) Rec("Inventory", "hotbar slot 1 right-click inspect", "SKIP", "hotbar slot 1 not found");
                        else
                        {
                            string before = InspectorItem();
                            yield return ClickAt(panel, VisibleRect(hb1).center, 1);
                            yield return Wait(0.5f);
                            string now = InspectorItem();
                            Rec("Inventory", "hotbar slot 1 right-click inspects scanner", (now != null && now != before) ? "PASS" : "FAIL", $"{before} -> {now}");
                        }
                        ItemInspector.Close();
                        yield return Wait(0.4f);
                    }
                }
            }
            // crate lines
            VisualElement crate = null;
            foreach (var d in Docs()) foreach (var e in d.rootVisualElement.Query<VisualElement>(className: "xa-term__row").ToList())
                if (e is Label lb && lb.text.Contains("crate:") && Visible(e)) crate = e;
            if (crate == null) Rec("Inventory", "crate line click opens contents", "SKIP", "no crate line visible");
            else
            {
                var open = (HashSet<string>)GetField(inv, "_openCrates");
                int c0 = open != null ? open.Count : -1;
                yield return ClickAt(panel, VisibleRect(crate).center, 0);
                yield return Frames(3);
                int c1 = open != null ? open.Count : -1;
                Rec("Inventory", "crate line click toggles contents", (c0 >= 0 && c1 == c0 + 1) ? "PASS" : "FAIL", $"open crates {c0} -> {c1}");
            }
            inv.Close();
            yield return Wait(0.5f);
        }

        IEnumerator TerminalPhase<T>(string area, bool refiner) where T : TerminalWindow
        {
            var w = FindFirstObjectByType<T>();
            if (w == null) { Rec(area, "open", "SKIP", "no " + typeof(T).Name + " in the loaded scene"); yield break; }
            w.Open();
            yield return Wait(1.2f);
            Rec(area, "open", w.IsOpen ? "PASS" : "FAIL");
            if (!w.IsOpen) yield break;
            AuditPick(area);
            if (refiner)
            {
                var panel = Docs()[0].rootVisualElement.panel;
                var rows = new List<VisualElement>();
                foreach (var d in Docs()) rows.AddRange(d.rootVisualElement.Query<VisualElement>(className: "xa-recipe").ToList());
                rows.RemoveAll(r => !Visible(r) || VisibleRect(r).height < 20f);
                if (rows.Count == 0) Rec(area, "recipe rows", "SKIP", "no recipes visible");
                else
                {
                    var row = rows[rows.Count - 1];
                    var r = VisibleRect(row);
                    Rec(area, "recipe row height >= 90px (80px icon)", row.worldBound.height >= 90f * 0.95f ? "PASS" : "FAIL", $"{row.worldBound.height:0}px panel");
                    yield return ClickAt(panel, r.center, 1);
                    yield return Wait(0.5f);
                    Rec(area, "right-click recipe inspects its output", ItemInspector.IsOpen ? "PASS" : "FAIL");
                    ItemInspector.Close();
                    yield return Wait(0.3f);
                    rows.Clear();
                    foreach (var d in Docs()) rows.AddRange(d.rootVisualElement.Query<VisualElement>(className: "xa-recipe").ToList());
                    if (rows.Count > 0)
                    {
                        var row2 = rows[0]; bool gone = false;
                        yield return ClickAt(panel, VisibleRect(row2).center, 0);
                        yield return Frames(2);
                        gone = row2.panel == null;
                        Rec(area, "left-click recipe row selects (window rebuilt)", gone ? "PASS" : "FAIL");
                    }
                }
                // material tiles (80px, LMB select / RMB inspect)
                var mats = new List<VisualElement>();
                foreach (var d in Docs()) foreach (var e in d.rootVisualElement.Query<VisualElement>(className: "xa-item-slot").ToList())
                    if (e.pickingMode == PickingMode.Position && Visible(e) && e.resolvedStyle.width > 70f && e.tooltip != null && e.tooltip.Contains("Right-click: inspect")) mats.Add(e);
                if (mats.Count == 0) Rec(area, "material tile right-click inspect", "SKIP", "no materials aboard");
                else
                {
                    yield return ClickAt(panel, VisibleRect(mats[0]).center, 1);
                    yield return Wait(0.5f);
                    Rec(area, "right-click material tile inspects", ItemInspector.IsOpen ? "PASS" : "FAIL");
                    ItemInspector.Close();
                }
            }
            w.Close();
            yield return Wait(0.4f);
        }

        IEnumerator NavPhase()
        {
            var nav = FindFirstObjectByType<NavigationTerminal>();
            if (nav == null) { Rec("Nav", "open", "SKIP", "no NavigationTerminal in the loaded scene"); yield break; }
            nav.Open();
            yield return Wait(1.5f);
            Rec("Nav", "open", nav.IsOpen ? "PASS" : "FAIL");
            if (!nav.IsOpen) yield break;
            AuditPick("Nav");
            var panel = Docs()[0].rootVisualElement.panel;
            var rows = new List<Button>();
            foreach (var d in Docs()) foreach (var b in d.rootVisualElement.Query<Button>(className: "xa-term__row").ToList()) if (Visible(b) && VisibleRect(b).height > 10f) rows.Add(b);
            if (rows.Count == 0) Rec("Nav", "list rows", "SKIP", "no list rows");
            else
            {
                // hover at the centre and at the far corners of a row: :hover must be set (whole row hot)
                var row = rows[0];
                var vr = VisibleRect(row);
                var spots = new (string n, Vector2 p)[] { ("centre", vr.center), ("left+3px", new Vector2(vr.xMin + 3f, vr.center.y)), ("right-3px", new Vector2(vr.xMax - 3f, vr.center.y)), ("top+3px", new Vector2(vr.center.x, vr.yMin + 3f)), ("bottom-3px", new Vector2(vr.center.x, vr.yMax - 3f)) };
                int ok = 0; var miss = new List<string>();
                foreach (var (n, p) in spots)
                {
                    yield return MouseTo(new Vector2(2f, 2f));
                    yield return Frames(2);
                    yield return MouseTo(PanelToScreen(panel, p));
                    yield return Frames(3);
                    if (Hovered(row)) ok++; else miss.Add(n);
                }
                Rec("Nav", "row hover (centre + 4 edge points)", ok == spots.Length ? "PASS" : "FAIL", ok == spots.Length ? "5/5" : "no :hover at " + string.Join(", ", miss));
                // real click on a row that is not the selected one: the rebuilt list marks it selected
                int idx = -1;
                for (int i = 0; i < rows.Count; i++) if (!rows[i].ClassListContains("xa-term__row--sel") && !rows[i].ClassListContains("xa-term__dropdown")) { idx = i; break; }
                if (idx < 0) Rec("Nav", "row click selects", "SKIP", "only one row");
                else
                {
                    var target = rows[idx];
                    string txt = Label(target);
                    var edge = new Vector2(VisibleRect(target).xMax - 4f, VisibleRect(target).center.y);
                    yield return ClickAt(panel, edge, 0);
                    yield return Frames(3);
                    bool rebuilt = target.panel == null;
                    Rec("Nav", "click at far-right edge of a row (the 'only right side' report)", rebuilt ? "PASS" : "FAIL", rebuilt ? "row handler ran: " + txt : "row not activated: " + txt);
                    var rows2 = new List<Button>();
                    foreach (var d in Docs()) foreach (var b in d.rootVisualElement.Query<Button>(className: "xa-term__row").ToList()) if (Visible(b)) rows2.Add(b);
                    if (rows2.Count > idx)
                    {
                        var t2 = rows2[idx];
                        var lft = new Vector2(VisibleRect(t2).xMin + 4f, VisibleRect(t2).yMax - 3f);
                        yield return ClickAt(panel, lft, 0);
                        yield return Frames(3);
                        Rec("Nav", "click at bottom-left corner of a row", t2.panel == null ? "PASS" : "FAIL");
                    }
                }
            }
            // buttons on the right (Plot course, map header buttons): centre + edges via real clicks that are safe (map header 'Reset view')
            Button reset = null;
            foreach (var d in Docs()) foreach (var b in d.rootVisualElement.Query<Button>().ToList()) if (Label(b) == "Reset view" && Visible(b)) reset = b;
            if (reset != null)
            {
                int ok2 = 0;
                foreach (var f in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.06f, 0.5f), new Vector2(0.94f, 0.5f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.9f) })
                {
                    var rb = VisibleRect(reset);
                    var el = reset;
                    var p = new Vector2(rb.xMin + rb.width * f.x, rb.yMin + rb.height * f.y);
                    yield return ClickAt(panel, p, 0);
                    yield return Frames(3);
                    if (el.panel == null) ok2++;
                    // re-find after rebuild
                    reset = null;
                    foreach (var d in Docs()) foreach (var b in d.rootVisualElement.Query<Button>().ToList()) if (Label(b) == "Reset view" && Visible(b)) reset = b;
                    if (reset == null) break;
                }
                Rec("Nav", "'Reset view' button: centre + 4 edge points all activate", ok2 == 5 ? "PASS" : "FAIL", ok2 + "/5");
            }
            // map: hover + click a body
            MapView3D map = null;
            foreach (var d in Docs()) { var m = d.rootVisualElement.Q<MapView3D>(); if (m != null && Visible(m)) map = m; }
            if (map == null || map.Items.Count == 0) Rec("Nav", "map hover/click", "SKIP", "no map or no bodies");
            else
            {
                int bi = -1;
                for (int i = 0; i < map.Items.Count; i++) if (!string.IsNullOrEmpty(map.Items[i].id)) { bi = i; if (map.Items[i].size >= 8f) break; }
                if (bi < 0) Rec("Nav", "map hover/click", "SKIP", "no clickable bodies");
                else
                {
                    var local = map.ToScreen(map.Items[bi].pos, out _);
                    var wp = map.LocalToWorld(local);
                    string got = null; map.Clicked += id => got = id;
                    yield return MouseTo(new Vector2(2f, 2f));
                    yield return MouseTo(PanelToScreen(panel, wp + new Vector2(6f, 4f)));
                    yield return Frames(3);
                    int hov = (int)GetField(map, "_hover");
                    Rec("Nav", "map body hover highlight (6px off centre)", hov == bi ? "PASS" : "FAIL", $"hover index {hov}, expected {bi}");
                    yield return ClickAt(panel, wp + new Vector2(6f, 4f), 0);
                    yield return Frames(2);
                    Rec("Nav", "map body click selects (6px off centre)", got == map.Items[bi].id ? "PASS" : "FAIL", $"clicked '{got}', expected '{map.Items[bi].id}'");
                }
            }
            nav.Close();
            yield return Wait(0.4f);
        }
    }
}
#endif