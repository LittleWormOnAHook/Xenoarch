using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Objectives;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Phase 1D/M2 journal (J): Objectives, Discoveries (chain progress), Finds (every artifact recovered,
    /// incl. sold / installed), Notes (collected datapad journals), Captain's Log (newest-first, player notes) and Standing (credits, notoriety
    /// rank ladder, recent deals). Read-only; pauses like the maps window; J / Esc closes.
    /// </summary>
    public sealed class DiscoveryJournalWindow : MonoBehaviour
    {
        public const string Panel = "discovery_journal";
        public Key toggleKey = Key.J;
        UIDocument _doc; VisualElement _veil, _body;
        string _tab = "discoveries";
        float _prevTimeScale = 1f;
        public bool IsOpen => _veil != null;

        void Update()
        {
            var kb = Keyboard.current;
            if (JournalReader.Blocking) return;
            if (kb != null && kb[toggleKey].wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else if (!UIState.BlocksGameplay && XenoarchRuntime.Instance != null && XenoarchRuntime.Instance.SessionActive && !(XenoarchRuntime.Scenes?.IsLoading ?? true)) Open();
            }
            else if (IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        void Open()
        {
            if (_doc == null)
            {
                var host = new GameObject("DiscoveryJournal"); host.transform.SetParent(transform, false);
                _doc = host.AddComponent<UIDocument>(); _doc.panelSettings = XenoarchUI.LoadPanelSettings(); _doc.sortingOrder = 56;
            }
            var root = _doc.rootVisualElement; XenoarchUI.FillRoot(root); root.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
            _veil = new VisualElement(); XenoarchUI.FillRoot(_veil); _veil.AddToClassList("xa-term-root");
            var win = new VisualElement(); win.AddToClassList("xa-term");
            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.Center;
            var title = new Label("JOURNAL"); title.AddToClassList("xa-title"); title.style.fontSize = 30; title.style.marginRight = 24; head.Add(title);
            head.style.flexWrap = Wrap.Wrap;
            Tab(head, "Objectives", "objectives"); Tab(head, "Discoveries", "discoveries"); Tab(head, "Finds", "finds"); Tab(head, "Notes", "notes"); Tab(head, "Log", "log"); Tab(head, "Standing", "standing");
            var hint = new Label("[J] / Esc close"); hint.AddToClassList("xa-hint"); hint.style.marginLeft = 20; head.Add(hint);
            win.Add(head);
            _body = new VisualElement(); _body.style.flexGrow = 1; _body.style.flexDirection = FlexDirection.Row; _body.style.marginTop = 10; _body.style.minHeight = 0;
            win.Add(_body);
            _veil.Add(win); root.Add(_veil);
            UIState.SetOpen(Panel, true);
            _prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            Render();
        }

        void Close()
        {
            bool was = _veil != null;
            _veil?.RemoveFromHierarchy(); _veil = null; _body = null;
            UIState.SetOpen(Panel, false);
            if (was && Time.timeScale == 0f && !UIState.IsOpen(Panels.Pause)) Time.timeScale = _prevTimeScale;
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Tab(VisualElement head, string label, string id)
        {
            var b = new Button(() => { _tab = id; Render(); }) { text = label };
            b.AddToClassList("xa-button"); b.style.width = 118; b.style.marginRight = 6;
            head.Add(b);
        }

        VisualElement Col(bool wide = false)
        {
            var c = new VisualElement(); c.AddToClassList("xa-term__col"); if (wide) c.AddToClassList("xa-term__col--wide");
            c.style.minHeight = 0; c.style.overflow = Overflow.Hidden; _body.Add(c);
            var sv = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            sv.style.flexGrow = 1; sv.style.minHeight = 0; c.Add(sv);
            return sv.contentContainer;
        }

        static Label T(VisualElement p, string text, string cls = "xa-term__text") { var l = new Label(text); l.AddToClassList(cls); p.Add(l); return l; }

        /// <summary>Journal line with a state glyph (done / open / locked), t155u icon style.</summary>
        static void StateRow(VisualElement p, string glyph, string state, string text, string cls)
        {
            var row = new VisualElement(); row.AddToClassList("xa-journal-row");
            row.Add(XenoarchIcons.Glyph(glyph, "xa-journal-icon", "xa-journal-icon--" + state));
            T(row, text, cls).style.flexShrink = 1;
            p.Add(row);
        }

        void Render()
        {
            if (_body == null) return;
            _body.Clear();
            var s = XenoarchRuntime.Session; if (s == null) return;
            switch (_tab)
            {
                case "objectives": RenderObjectives(s); break;
                case "finds": RenderFinds(s); break;
                case "notes": RenderNotes(s); break;
                case "log": RenderLog(s); break;
                case "standing": RenderStanding(s); break;
                default: RenderDiscoveries(s); break;
            }
        }

        // ------------------------------------------------------------ Discoveries
        void RenderDiscoveries(GameSession s)
        {
            var d = s.Discovery;
            var chains = Col(true);
            foreach (var c in d.Chains)
            {
                var done = d.Progress(c.Id).completed;
                T(chains, (string.IsNullOrEmpty(c.title) ? c.Id : c.title).ToUpperInvariant() + $"   {done.Count(id => c.nodes.Any(n => n.id == id))}/{c.nodes.Count}", "xa-term__h");
                foreach (var n in c.nodes)
                {
                    bool isDone = done.Contains(n.id);
                    bool open = isDone || PrereqsMet(n, done);
                    string tag = n.optional ? "  (optional)" : "";
                    if (isDone) StateRow(chains, "ui-complete", "done", $"{n.title}{tag}", "xa-term__text");
                    else if (open) StateRow(chains, "arrowRight", "open", $"{n.title}{tag}", "xa-term__text");
                    else StateRow(chains, "locked", "locked", $"???{tag}", "xa-term__dim");
                }
            }
            if (d.Chains.Count == 0) T(chains, "No discovery chains.", "xa-term__dim");

            var log = Col();
            T(log, "DISCOVERY LOG", "xa-term__h");
            var j = d.State.journal;
            if (j.Count == 0) T(log, "Nothing yet. Recover and research artifacts to follow their trail.", "xa-term__dim");
            for (int i = j.Count - 1; i >= 0; i--) T(log, $"{Clock(j[i].time)}  {j[i].text}");
        }

        static bool PrereqsMet(DiscoveryNode n, List<string> done)
        {
            if (n.prerequisites == null || n.prerequisites.Count == 0) return true;
            int have = n.prerequisites.Count(done.Contains);
            return n.mode switch { PrereqMode.Any => have > 0, PrereqMode.Count => have >= n.count, _ => have == n.prerequisites.Count };
        }

        static string Clock(float t) { int m = Mathf.FloorToInt(Mathf.Max(0f, t) / 60f); return $"[{m / 60:00}:{m % 60:00}]"; }

        // ------------------------------------------------------------ Finds
        void RenderFinds(GameSession s)
        {
            var a = s.Artifacts;
            var list = Col(true);
            var all = a.State.instances.OrderByDescending(i => i.discoveredAt).ToList();
            T(list, $"FINDS   {all.Count} recovered, {all.Select(i => i.definitionId).Distinct().Count()} kinds", "xa-term__h");
            if (all.Count == 0) T(list, "No artifacts recovered yet.", "xa-term__dim");
            foreach (var i in all)
            {
                var d = a.Definition(i);
                string where = i.location switch { ArtifactLocation.Sold => "sold", ArtifactLocation.Installed => "installed", ArtifactLocation.Stored => "in ship storage", _ => "carried" };
                T(list, $"{a.DisplayName(i)}  -  {(i.Identified && d != null ? d.rarity.ToString() : "unidentified")}, {i.condition:P0}, {where}");
                T(list, $"   from {i.siteId}   |   est. {a.Value(i):0} cr", "xa-term__dim");
            }
            var side = Col();
            T(side, "CIVILIZATIONS", "xa-term__h");
            var civs = all.Where(i => i.Done(ResearchOp.CivilizationIdentification)).Select(i => a.Definition(i)?.civilizationId).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            if (civs.Count == 0) T(side, "None identified yet.", "xa-term__dim");
            foreach (var c in civs) { var civ = a.Civilization(c); T(side, $"{(civ != null ? civ.displayName : c)}: language {a.Knowledge(c).language:P0}"); }
            T(side, "INSTALLED TECH", "xa-term__h");
            var inst = all.Where(i => i.location == ArtifactLocation.Installed).ToList();
            if (inst.Count == 0) T(side, "None. Functional devices can be installed at the research lab after Technology analysis.", "xa-term__dim");
            foreach (var i in inst) { var d = a.Definition(i); T(side, $"{a.DisplayName(i)}: {d?.installTarget} +{d?.installBonus}"); }
        }

        // ------------------------------------------------------------ Notes
        void RenderNotes(GameSession s)
        {
            var col = Col(true);
            var notes = s.Journals.Collected.ToList();
            T(col, $"DATAPAD NOTES   {notes.Count} / {DMI.Xenoarch.Journals.JournalService.All.Length}", "xa-term__h");
            if (notes.Count == 0) T(col, "No notes collected. Look for datapads near landing zones and dig sites.", "xa-term__dim");
            var ids = notes.Select(n => n.Id).ToList();
            foreach (var n in notes)
            {
                var id = n.Id;
                var b = new Button(() => DMI.Xenoarch.UI.JournalReader.Show(id, ids)) { text = (s.Journals.IsRead(id) ? "" : "NEW  ") + $"{n.title}  ({n.category})" };
                b.AddToClassList("xa-term__btn"); b.style.whiteSpace = WhiteSpace.Normal; col.Add(b);
            }
        }

        // ------------------------------------------------------------ Standing
        void RenderStanding(GameSession s)
        {
            var e = s.Economy;
            var col = Col();
            T(col, "STANDING", "xa-term__h");
            T(col, $"Credits: {e.Credits:N0} cr");
            T(col, $"Notoriety: {e.Score}   ({e.RankName})");
            if (e.NextRankAt > 0) T(col, $"Next rank at {e.NextRankAt}", "xa-term__dim");
            T(col, "RANKS", "xa-term__h");
            var th = e.Settings.rankThresholds ?? new int[0];
            for (int r = 0; r < th.Length; r++) T(col, $"{(r == e.Rank ? "> " : "   ")}{e.RankNameOf(r)}  ({th[r]})", r <= e.Rank ? "xa-term__text" : "xa-term__dim");
            T(col, "Notoriety comes from recovering artifacts, pinpointing sites, research, identifying civilizations, translations, installing alien tech and collecting caches. Vendors pay more and offer more to higher ranks.", "xa-term__dim");

            var deals = Col();
            T(deals, "RECENT DEALS", "xa-term__h");
            var tx = e.Wallet.transactions;
            if (tx.Count == 0) T(deals, "None yet. Trade at the Trade Hub (Sol).", "xa-term__dim");
            for (int i = tx.Count - 1; i >= 0 && i >= tx.Count - 25; i--)
            {
                var t = tx[i];
                string item = t.itemId != null && s.Items.TryGetItem(t.itemId, out var d) ? d.DisplayNameOrId : t.itemId;
                T(deals, $"{t.kind} {t.count}x {item}: {(t.credits >= 0 ? "+" : "")}{t.credits} cr", "xa-term__dim");
            }
        }

        // ------------------------------------------------------------ Objectives (M2)
        void RenderObjectives(GameSession s)
        {
            var o = s.Objectives;
            if (o == null) { T(Col(), "Objectives are not available.", "xa-term__dim"); return; }
            var col = Col(true);
            T(col, "OBJECTIVES", "xa-term__h");
            var hide = new Button(() => { o.HideOptional = !o.HideOptional; Render(); }) { text = o.HideOptional ? "Show optional objectives" : "Hide optional objectives" };
            hide.AddToClassList("xa-term__btn"); hide.AddToClassList("xa-term__btn--ghost"); col.Add(hide);

            foreach (var v in o.All())
            {
                if (v.optional && o.HideOptional && !v.complete && v.kind == ObjectiveKind.Side) continue;
                bool focused = o.IsFocused(v.id);
                string kind = v.kind == ObjectiveKind.Main ? "MAIN" : (v.optional ? "OPTIONAL" : "SIDE");
                string status = v.complete ? "  -  complete" : (focused ? "  -  focused" : "");
                var head = new VisualElement(); head.AddToClassList("xa-obj__head"); head.pickingMode = PickingMode.Position;
                var star = XenoarchIcons.Glyph("star", "xa-obj__star");
                if (!focused) star.AddToClassList("xa-obj__star--hidden");
                head.Add(star);
                var title = T(head, $"{kind}   {v.title}{status}", "xa-term__h");
                title.AddToClassList(focused ? "xa-obj__focus" : "xa-obj__idle");
                title.style.marginTop = 0; title.style.marginBottom = 0;
                var captured = v.id;
                head.RegisterCallback<ClickEvent>(e => { o.SetFocus(captured); Render(); e.StopPropagation(); });
                col.Add(head);
                if (!string.IsNullOrEmpty(v.subtitle)) T(col, v.subtitle, "xa-term__dim");
                foreach (var l in v.lines)
                {
                    string text = l.label;
                    if (!string.IsNullOrEmpty(l.progress)) text += "  (" + l.progress + ")";
                    if (l.complete) StateRow(col, "ui-complete", "done", text, "xa-term__text");
                    else StateRow(col, "arrowRight", "open", text, focused ? "xa-term__text" : "xa-term__dim");
                }
                if (!v.complete && !string.IsNullOrEmpty(v.Prompt)) T(col, "Hint: " + v.Prompt, "xa-term__dim");
            }

            var side = Col();
            T(side, "TRACKER", "xa-term__h");
            T(side, "The main expedition stays at the top. Click an objective here or on the HUD to focus it (gold star). Navigation markers follow the focused objective. Hide optional keeps that toggle, and completing or hiding a focused side returns focus to the main expedition.", "xa-term__dim");
        }

        // ------------------------------------------------------------ Captain's Log (M2)
        void RenderLog(GameSession s)
        {
            var log = s.Log;
            var notes = Col(true);
            T(notes, "CAPTAIN'S LOG", "xa-term__h");
            if (log == null) { T(notes, "The log is not available.", "xa-term__dim"); return; }

            var tools = new VisualElement(); tools.style.flexDirection = FlexDirection.Row; tools.style.marginBottom = 8;
            var order = new Button(() => { log.OldestFirst = !log.OldestFirst; Render(); }) { text = log.OldestFirst ? "Oldest First" : "Newest First" };
            order.AddToClassList("xa-term__btn"); order.AddToClassList("xa-term__btn--ghost"); order.style.marginRight = 8; tools.Add(order);
            notes.Add(tools);

            var entry = new TextField { multiline = true };
            entry.AddToClassList("xa-term__text");
            entry.style.minHeight = 64; entry.style.marginBottom = 6; entry.style.whiteSpace = WhiteSpace.Normal;
            notes.Add(entry);
            var add = new Button(() =>
            {
                if (s.Notifications != null && s.Notifications.AddNote(entry.value)) { entry.value = ""; Render(); }
                else Toast.Show("That note is already in the log, or it was empty.", 3f);
            }) { text = "Add note" };
            add.AddToClassList("xa-term__btn"); notes.Add(add);

            int n = 0;
            foreach (var e in log.Ordered())
            {
                if (e == null || string.IsNullOrEmpty(e.text)) continue;
                n++;
                string tag = e.playerNote ? "NOTE" : e.kind.ToString().ToUpperInvariant();
                T(notes, $"{Clock(e.time)}  [{tag}]  {e.text}", e.playerNote ? "xa-term__good" : "xa-term__text");
            }
            if (n == 0) T(notes, "Nothing yet. Discoveries, research, recoveries and objective updates appear here. You can also add your own notes.", "xa-term__dim");
        }
    }
}
