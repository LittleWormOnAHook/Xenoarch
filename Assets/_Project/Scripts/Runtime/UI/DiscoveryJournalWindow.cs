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
            else if (IsOpen && kb != null && !TypingInField() && (kb.qKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame))
            { _tab = MUI.Cycle(Tabs, _tab, kb.qKey.wasPressedThisFrame ? -1 : 1); RenderTabs(); Render(); }
            else if (IsOpen && kb != null && _tab == "messages" && !TypingInField() && _msgThread != null)
            {
                // t246u: 1-4 answer the open conversation.
                int pick = kb.digit1Key.wasPressedThisFrame ? 0 : kb.digit2Key.wasPressedThisFrame ? 1 : kb.digit3Key.wasPressedThisFrame ? 2 : kb.digit4Key.wasPressedThisFrame ? 3 : -1;
                var ms = DMI.Xenoarch.Messages.MessageService.Instance;
                if (pick >= 0 && ms != null && ms.Reply(_msgThread, pick)) Render();
            }
        }

        /// <summary>t246u tab bar (Morpheus): Q / E cycle, click selects.</summary>
        static readonly (string id, string label, string glyph)[] Tabs =
        {
            ("objectives", "Objectives", "ui-missions"), ("discoveries", "Discoveries", "ui-new-discovery"), ("finds", "Finds", "research-archive"),
            ("notes", "Notes", "ui-journal"), ("log", "Captain's Log", "ui-journal"), ("messages", "Messages", "ship-comms"), ("standing", "Standing", "ui-level"),
        };

        VisualElement _tabHost;

        bool TypingInField() => _veil != null && _veil.panel?.focusController?.focusedElement is TextField;

        void RenderTabs()
        {
            if (_tabHost == null) return;
            _tabHost.Clear();
            MUI.Tabs(_tabHost, Tabs, _tab, id => { _tab = id; RenderTabs(); Render(); });
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
            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.Center; head.style.justifyContent = Justify.SpaceBetween;
            var title = new Label("JOURNAL"); title.AddToClassList("xa-term__title"); head.Add(title);
            MUI.Legend(head, ("Q / E", "Tabs"), ("J", "Close"), ("Esc", "Close"));
            win.Add(head);
            _tabHost = new VisualElement(); win.Add(_tabHost);
            RenderTabs();
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
            _veil?.RemoveFromHierarchy(); _veil = null; _body = null; _tabHost = null;
            UIState.SetOpen(Panel, false);
            if (was && Time.timeScale == 0f && !UIState.IsOpen(Panels.Pause)) Time.timeScale = _prevTimeScale;
        }

        void OnDisable() { if (IsOpen) Close(); }

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
                case "messages": RenderMessages(); break;
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

        // ------------------------------------------------------------ Objectives (M2; t246u Morpheus cards + detail)
        void RenderObjectives(GameSession s)
        {
            var o = s.Objectives;
            if (o == null) { T(Col(), "Objectives are not available.", "xa-term__dim"); return; }
            var col = Col(true);
            var top = new VisualElement(); top.style.flexDirection = FlexDirection.Row; top.style.alignItems = Align.Center; top.style.justifyContent = Justify.SpaceBetween;
            var hide = new Button(() => { o.HideOptional = !o.HideOptional; Render(); }) { text = o.HideOptional ? "SHOW OPTIONAL" : "HIDE OPTIONAL" };
            hide.AddToClassList("xa-term__btn"); hide.AddToClassList("xa-term__btn--ghost"); hide.style.minHeight = 32; hide.style.marginTop = 0;
            var main = o.Main();
            float mp = MUI.Progress(main, out int md, out int mt);
            MUI.Head(col, "Objectives", main != null ? $"{main.title}   {md} / {mt}   {Mathf.RoundToInt(mp * 100f)}%" : null);
            top.Add(hide); col.Add(top);

            ObjectiveView focusedView = null;
            foreach (var v in o.All())
            {
                if (v.optional && o.HideOptional && !v.complete && v.kind == ObjectiveKind.Side) continue;
                bool focused = o.IsFocused(v.id);
                if (focused) focusedView = v;
                bool isMain = v.kind == ObjectiveKind.Main;
                var card = new VisualElement(); card.AddToClassList("xa-m-card"); if (focused) card.AddToClassList("xa-m-card--sel");
                card.pickingMode = PickingMode.Position;
                var captured = v.id;
                card.RegisterCallback<ClickEvent>(e => { o.SetFocus(captured); Render(); e.StopPropagation(); });
                var row = new VisualElement(); row.AddToClassList("xa-m-card__row");
                var star = XenoarchIcons.Glyph("star", "xa-m-star"); if (!focused) star.AddToClassList("xa-m-star--off");
                row.Add(star);
                T(row, v.title, "xa-m-card__title");
                MUI.Badge(row, isMain ? "Main" : (v.optional ? "Optional" : "Side"), isMain ? "main" : (v.optional ? null : "side"));
                if (v.complete) MUI.Badge(row, "Complete", "side");
                card.Add(row);
                if (!string.IsNullOrEmpty(v.subtitle)) { var sub = T(card, v.subtitle, "xa-m-dim"); sub.style.marginLeft = 28; sub.style.marginTop = 4; }
                float p = MUI.Progress(v, out int d, out int t);
                var br = MUI.BarRow(card, p, $"{d} / {t}", !isMain); br.style.marginLeft = 28; br.style.marginTop = 8;
                col.Add(card);
            }

            // Detail: the focused objective (Morpheus quest detail pane).
            var side = Col();
            var f = focusedView ?? o.Focused() ?? main;
            if (f == null) { T(side, "No objective yet.", "xa-term__dim"); return; }
            var fh = new VisualElement(); fh.AddToClassList("xa-m-card__row");
            fh.Add(XenoarchIcons.Glyph("star", "xa-m-star"));
            var tl = T(fh, f.title.ToUpperInvariant(), "xa-term__title"); tl.style.whiteSpace = WhiteSpace.Normal; tl.style.flexShrink = 1;
            side.Add(fh);
            T(side, (o.IsFocused(f.id) ? "FOCUSED  ·  " : "") + (f.kind == ObjectiveKind.Main ? "MAIN OBJECTIVE" : f.optional ? "OPTIONAL OBJECTIVE" : "SIDE OBJECTIVE"), "xa-m-dim");
            if (!string.IsNullOrEmpty(f.subtitle)) T(side, f.subtitle, "xa-m-text");
            MUI.Head(side, "Progress");
            float fp = MUI.Progress(f, out int fd, out int ft);
            MUI.BarRow(side, fp, $"{fd} / {ft}", f.kind != ObjectiveKind.Main);
            MUI.Head(side, "Steps");
            bool nextShown = false;
            foreach (var l in f.lines)
            {
                string text = l.label;
                if (!string.IsNullOrEmpty(l.progress)) text += "  (" + l.progress + ")";
                if (l.complete) StateRow(side, "ui-complete", "done", text, "xa-term__dim");
                else { StateRow(side, "arrowRight", "open", text, "xa-term__text"); if (!nextShown) { nextShown = true; } }
            }
            if (!f.complete && !string.IsNullOrEmpty(f.Prompt)) { MUI.Head(side, "Hint"); T(side, f.Prompt, "xa-m-dim"); }
            var lg = MUI.Legend(side, ("Click", "Set focus"), ("J", "Close")); lg.style.marginTop = 16;
        }

        // ------------------------------------------------------------ Captain's Log (M2; t246u Morpheus entry cards)
        void RenderLog(GameSession s)
        {
            var log = s.Log;
            var notes = Col(true);
            if (log == null) { T(notes, "The log is not available.", "xa-term__dim"); return; }
            int total = 0; foreach (var e in log.Ordered()) if (e != null && !string.IsNullOrEmpty(e.text)) total++;
            MUI.Head(notes, "Captain's Log", total + (total == 1 ? " entry" : " entries"));

            var tools = new VisualElement(); tools.style.flexDirection = FlexDirection.Row; tools.style.marginBottom = 6; tools.style.alignItems = Align.Center;
            var entry = new TextField { multiline = false };
            entry.AddToClassList("xa-term__text");
            entry.style.flexGrow = 1; entry.style.marginRight = 8;
            tools.Add(entry);
            var add = new Button(() =>
            {
                if (s.Notifications != null && s.Notifications.AddNote(entry.value)) { entry.value = ""; Render(); }
                else Toast.Show("That note is already in the log, or it was empty.", 3f);
            }) { text = "ADD NOTE" };
            add.AddToClassList("xa-term__btn"); add.style.marginTop = 0; add.style.marginBottom = 0; tools.Add(add);
            var order = new Button(() => { log.OldestFirst = !log.OldestFirst; Render(); }) { text = log.OldestFirst ? "OLDEST FIRST" : "NEWEST FIRST" };
            order.AddToClassList("xa-term__btn"); order.AddToClassList("xa-term__btn--ghost"); order.style.marginLeft = 8; order.style.marginTop = 0; order.style.marginBottom = 0; tools.Add(order);
            notes.Add(tools);

            int n = 0;
            foreach (var e in log.Ordered())
            {
                if (e == null || string.IsNullOrEmpty(e.text)) continue;
                n++;
                var card = new VisualElement(); card.AddToClassList("xa-m-card");
                var row = new VisualElement(); row.AddToClassList("xa-m-card__row");
                var g = XenoarchIcons.Glyph(e.playerNote ? "ui-journal" : NotificationService.KindGlyph(e.kind), "xa-journal-icon"); g.style.width = 22; g.style.height = 22; g.style.marginRight = 10; g.style.marginBottom = 0;
                row.Add(g);
                T(row, Clock(e.time), "xa-m-dim").style.flexGrow = 1;
                string tag = e.playerNote ? "Note" : e.kind.ToString();
                MUI.Badge(row, tag, e.playerNote ? "main" : e.kind == NoticeKind.Discovery || e.kind == NoticeKind.Message ? "side" : e.kind == NoticeKind.Research ? "rare" : null);
                card.Add(row);
                var txt = T(card, e.text, "xa-m-text"); txt.style.marginTop = 4; txt.style.marginLeft = 32;
                notes.Add(card);
            }
            if (n == 0) T(notes, "Nothing yet. Discoveries, research, recoveries, messages and objective updates appear here. You can also add your own notes.", "xa-term__dim");
        }

        // ------------------------------------------------------------ Messages (t246u)
        string _msgThread;

        void RenderMessages()
        {
            var ms = DMI.Xenoarch.Messages.MessageService.Instance;
            var left = Col();
            if (ms == null) { T(left, "Messages are not available.", "xa-term__dim"); return; }
            var inbox = ms.Inbox();
            MUI.Head(left, "Messages", ms.Unread > 0 ? $"{ms.Unread} new" : $"{inbox.Count}");
            if (inbox.Count == 0) { T(left, "No messages yet. Mission Control and the ship AI will contact you here.", "xa-term__dim"); return; }
            if (_msgThread == null || ms.StateOf(_msgThread) == null) _msgThread = inbox[0].id;
            foreach (var st in inbox)
            {
                var th = ms.Thread(st.id);
                string id = st.id;
                var card = new VisualElement(); card.AddToClassList("xa-m-card");
                if (id == _msgThread) card.AddToClassList("xa-m-card--sel");
                card.RegisterCallback<ClickEvent>(_ => { _msgThread = id; Render(); });
                var row = new VisualElement(); row.AddToClassList("xa-m-card__row");
                var g = XenoarchIcons.Glyph(string.IsNullOrEmpty(th.glyph) ? "ship-comms" : th.glyph); g.style.width = 26; g.style.height = 26; g.style.marginRight = 10;
                row.Add(g);
                var col = new VisualElement(); col.style.flexGrow = 1; col.style.flexShrink = 1;
                MUI.L(col, (th.contact ?? "").ToUpperInvariant(), "xa-m-card__title");
                MUI.L(col, th.subject ?? "", "xa-m-dim");
                row.Add(col);
                if (st.unread) MUI.Badge(row, "New", "new");
                else if (ms.Choices(id).Count > 0) MUI.Badge(row, "Reply", "side");
                card.Add(row);
                var last = st.lines.Count > 0 ? st.lines[st.lines.Count - 1] : null;
                if (last != null) { var pv = MUI.L(card, (last.player ? "You: " : "") + last.text, "xa-m-dim"); pv.style.marginLeft = 36; pv.style.marginTop = 4; pv.style.whiteSpace = WhiteSpace.Normal; pv.style.maxHeight = 40; pv.style.overflow = Overflow.Hidden; }
                left.Add(card);
            }

            var right = Col(true);
            var cur = ms.StateOf(_msgThread); var thr = ms.Thread(_msgThread);
            if (cur == null || thr == null) return;
            ms.MarkRead(_msgThread);
            MUI.Head(right, thr.contact, thr.subject);
            foreach (var line in cur.lines)
            {
                var b = new VisualElement(); b.AddToClassList("xa-msg"); b.AddToClassList(line.player ? "xa-msg--me" : "xa-msg--them");
                MUI.L(b, $"{(line.from ?? "").ToUpperInvariant()}  ·  {Clock(line.time)}", "xa-msg__from");
                MUI.L(b, line.text, "xa-msg__text");
                right.Add(b);
            }
            var choices = ms.Choices(_msgThread);
            if (choices.Count > 0)
            {
                MUI.L(right, "REPLY", "xa-m-head__title").style.marginTop = 14;
                for (int i = 0; i < choices.Count; i++)
                {
                    int ci = i; string tid = _msgThread;
                    var btn = new Button(() => { ms.Reply(tid, ci); Render(); }); btn.AddToClassList("xa-m-tile"); btn.AddToClassList("xa-msg__reply");
                    MUI.Key(btn, (i + 1).ToString());
                    MUI.L(btn, choices[i].text, "xa-m-text").style.whiteSpace = WhiteSpace.Normal;
                    right.Add(btn);
                }
            }
            else T(right, "End of conversation.", "xa-term__dim").style.marginTop = 10;
        }
    }
}
