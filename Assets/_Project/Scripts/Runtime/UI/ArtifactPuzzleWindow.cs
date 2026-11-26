using System;
using System.Collections.Generic;
using DMI.Xenoarch.Artifacts;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// "Combine with other artifact" panel: owned puzzle pieces on the left, a square board with the puzzle's slots on
    /// the right. Click a piece then a slot, or drag a piece onto a slot; right-click a slot to take the piece out.
    /// Filling every slot solves the puzzle (see <see cref="ArtifactPuzzleService"/>) and plays the reveal.
    /// Opened from the inventory's right-click menu; Back returns to the inventory.
    /// </summary>
    public sealed class ArtifactPuzzleWindow : TerminalWindow
    {
        protected override string PanelId => "artifact.puzzle";
        string _group, _sel, _drag, _message;
        VisualElement _ghost; Vector2 _dragStart; bool _dragMoved;
        readonly List<VisualElement> _slotEls = new List<VisualElement>();
        float _revealStart = -1f; Label _revealCoords; VisualElement _revealMap;
        AudioSource _audio;
        public Action Back;

        void Awake() { terminalName = "Artifact Reconstruction"; }

        public void Show(string groupId, string selectInstance = null)
        {
            _group = groupId; _sel = selectInstance; _message = null; _revealStart = -1f;
            if (IsOpen) Refresh(); else Open();
        }

        protected override void OnClosed() { EndDrag(); Back?.Invoke(); }

        protected override void Tick()
        {
            if (_revealStart < 0f || _revealMap == null) return;
            float t = Time.unscaledTime - _revealStart;
            _revealMap.style.opacity = Mathf.Clamp01(t / 1.5f);
            if (_revealCoords != null)
            {
                var full = ArtifactPuzzleService.Spec(_group)?.revealText ?? "";
                int n = Mathf.Clamp(Mathf.FloorToInt((t - 1.0f) * 28f), 0, full.Length);
                _revealCoords.text = full.Substring(0, n);
            }
            _revealMap.MarkDirtyRepaint();
        }

        protected override void Build(VisualElement body)
        {
            var s = Session; var p = s.Puzzles; var spec = ArtifactPuzzleService.Spec(_group);
            _slotEls.Clear(); _ghost = null; _drag = null;
            if (spec == null) { Text(body, "Unknown puzzle.", "xa-term__warn"); return; }
            var board = p.Board(_group);
            var pieces = p.Pieces(_group);
            Status.text = $"{spec.title}   |   {p.Filled(_group)}/{board.slots.Count} slots filled   |   {pieces.Count} pieces found";
            Hint.text = board.solved ? "Esc / Tab: back to inventory" : "Click a piece then a slot, or drag it onto a slot   |   Right-click a slot to remove   |   Esc / Tab: back";

            // ---------------- left: puzzle artifacts
            var left = ScrollCol(body, 0); ListCol(left);
            Text(left, "PUZZLE ARTIFACTS", "xa-term__h");
            if (pieces.Count == 0) Text(left, "No pieces in the backpack or ship crates.", "xa-term__dim");
            foreach (var a in pieces)
            {
                var id = a.instanceId;
                int at = p.SlotOf(_group, id);
                var row = new VisualElement(); row.AddToClassList("xa-term__row");
                if (id == _sel) row.AddToClassList("xa-term__row--sel");
                if (at >= 0) row.AddToClassList("xa-term__row--locked");
                row.Add(new Label(s.Artifacts.DisplayName(a)) { pickingMode = PickingMode.Ignore });
                row.Add(new Label(at >= 0 ? $"slot {at + 1}" : $"{a.condition:P0}") { pickingMode = PickingMode.Ignore, style = { color = XenoarchPalette.SecondaryText } });
                if (!board.solved)
                {
                    row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) BeginDrag(row, id, e.position, e.pointerId); });
                    row.RegisterCallback<PointerMoveEvent>(e => MoveDrag(row, e.position));
                    row.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0) DropDrag(row, e.position, e.pointerId); });
                }
                left.Add(row);
            }
            if (!string.IsNullOrEmpty(_message)) Text(left, _message, "xa-term__warn");

            // ---------------- right: square board or the reveal
            var right = Col(body, true); right.style.minHeight = 0; right.style.alignItems = Align.Center; right.style.justifyContent = Justify.Center;
            if (board.solved) { BuildReveal(right, spec, _revealStart >= 0f); return; }
            Text(right, "RECONSTRUCTION BOARD", "xa-term__h");
            var sq = new VisualElement(); sq.style.flexDirection = FlexDirection.Row; sq.style.flexWrap = Wrap.Wrap;
            sq.style.backgroundColor = XenoarchPalette.PanelDeep;
            Border(sq, XenoarchPalette.A(XenoarchPalette.Secondary, 0.6f), 2);
            right.Add(sq);
            right.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float side = Mathf.Max(240f, Mathf.Min(right.contentRect.width, right.contentRect.height - 40f));
                sq.style.width = side; sq.style.height = side;
            });
            // 3x3 grid: eight slots around a centre readout (the map core).
            int slot = 0;
            for (int cell = 0; cell < 9; cell++)
            {
                var c = new VisualElement(); c.style.width = Length.Percent(33.333f); c.style.height = Length.Percent(33.333f);
                c.style.paddingLeft = c.style.paddingRight = c.style.paddingTop = c.style.paddingBottom = 6;
                sq.Add(c);
                if (cell == 4)
                {
                    c.style.alignItems = Align.Center; c.style.justifyContent = Justify.Center;
                    c.Add(new Label($"{p.Filled(_group)}/{board.slots.Count}") { style = { fontSize = 34, color = XenoarchPalette.Primary, unityFontStyleAndWeight = FontStyle.Bold } });
                    c.Add(new Label("MAP CORE") { style = { fontSize = 13, color = XenoarchPalette.Primary } });
                    continue;
                }
                c.Add(SlotEl(slot++, board));
            }
        }

        VisualElement SlotEl(int i, PuzzleBoardState board)
        {
            var s = Session; var id = board.slots[i]; bool filled = !string.IsNullOrEmpty(id);
            var el = new VisualElement(); el.style.flexGrow = 1; el.style.alignItems = Align.Center; el.style.justifyContent = Justify.Center;
            el.style.backgroundColor = filled ? XenoarchPalette.A(XenoarchPalette.Highlight, 0.18f) : XenoarchPalette.PanelDark;
            Border(el, filled ? XenoarchPalette.Highlight : XenoarchPalette.A(XenoarchPalette.Secondary, 0.5f), filled ? 2 : 1);
            if (filled)
            {
                var a = s.Artifacts.Get(id); var d = s.Artifacts.Definition(a);
                if (d != null && d.icon != null)
                {
                    var img = new VisualElement { pickingMode = PickingMode.Ignore }; img.style.width = Length.Percent(60); img.style.height = Length.Percent(60);
                    XenoarchIcons.ApplyItem(img, d); el.Add(img);
                }
                el.Add(new Label(a != null ? s.Artifacts.DisplayName(a) : "?") { pickingMode = PickingMode.Ignore, style = { fontSize = 12, whiteSpace = WhiteSpace.Normal, unityTextAlign = TextAnchor.MiddleCenter } });
            }
            else el.Add(new Label($"{i + 1}") { pickingMode = PickingMode.Ignore, style = { fontSize = 22, color = XenoarchPalette.SecondaryText } });
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1) { Session.Puzzles.Remove(_group, i); _message = null; Refresh(); }
                else if (e.button == 0 && !string.IsNullOrEmpty(_sel)) TryPlace(i, _sel);
            });
            _slotEls.Add(el);
            return el;
        }

        void TryPlace(int slot, string instanceId)
        {
            var p = Session.Puzzles;
            bool wasSolved = p.IsSolved(_group);
            if (!p.Place(_group, slot, instanceId, out var why)) { _message = why; Refresh(); return; }
            _message = null; _sel = null;
            if (!wasSolved && p.IsSolved(_group)) { _revealStart = Time.unscaledTime; PlayReveal(); }
            else Click(1f);
            Refresh();
        }

        // ---------------------------------------------------------------- drag and drop
        void BeginDrag(VisualElement row, string id, Vector2 pos, int pointerId)
        {
            _drag = id; _dragStart = pos; _dragMoved = false;
            row.CapturePointer(pointerId);
        }

        void MoveDrag(VisualElement row, Vector2 pos)
        {
            if (_drag == null) return;
            if (!_dragMoved && (pos - _dragStart).sqrMagnitude < 64f) return;
            _dragMoved = true;
            if (_ghost == null)
            {
                var a = Session.Artifacts.Get(_drag);
                _ghost = new Label(a != null ? Session.Artifacts.DisplayName(a) : "piece") { pickingMode = PickingMode.Ignore };
                _ghost.style.position = Position.Absolute; _ghost.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.Highlight, 0.85f);
                _ghost.style.color = XenoarchPalette.OnButton; _ghost.style.paddingLeft = _ghost.style.paddingRight = 8; _ghost.style.paddingTop = _ghost.style.paddingBottom = 4;
                Body.Add(_ghost);
            }
            var local = Body.WorldToLocal(pos);
            _ghost.style.left = local.x + 12; _ghost.style.top = local.y + 8;
            foreach (var el in _slotEls) el.style.opacity = el.worldBound.Contains(pos) ? 0.7f : 1f;
        }

        void DropDrag(VisualElement row, Vector2 pos, int pointerId)
        {
            if (_drag == null) return;
            var id = _drag; bool moved = _dragMoved;
            if (row.HasPointerCapture(pointerId)) row.ReleasePointer(pointerId);
            int hit = -1;
            for (int i = 0; i < _slotEls.Count; i++) if (_slotEls[i].worldBound.Contains(pos)) { hit = i; break; }
            EndDrag();
            if (moved && hit >= 0) TryPlace(hit, id);
            else { _sel = _sel == id ? null : id; Refresh(); }
        }

        void EndDrag() { _drag = null; _dragMoved = false; if (_ghost != null) { _ghost.RemoveFromHierarchy(); _ghost = null; } }

        // ---------------------------------------------------------------- reveal
        void BuildReveal(VisualElement right, PuzzleSpec spec, bool animate)
        {
            Text(right, "STAR MAP RECONSTRUCTED", "xa-term__h");
            var map = new VisualElement(); map.style.backgroundColor = XenoarchPalette.PanelDeep;
            Border(map, XenoarchPalette.Highlight, 2);
            right.Add(map);
            right.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float side = Mathf.Max(240f, Mathf.Min(right.contentRect.width, right.contentRect.height - 120f));
                map.style.width = side; map.style.height = side;
            });
            map.generateVisualContent += DrawMap;
            map.style.opacity = animate ? 0f : 1f;
            var name = new Label(spec.revealTitle) { style = { fontSize = 26, color = XenoarchPalette.Primary, unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } };
            right.Add(name);
            _revealCoords = new Label(animate ? "" : spec.revealText) { style = { fontSize = 18, color = XenoarchPalette.Highlight } };
            right.Add(_revealCoords);
            Text(right, "New destination unlocked in the Nav terminal's system list.", "xa-term__dim");
            _revealMap = animate ? map : null;
        }

        /// <summary>Simple stylised map: faint star field, Sol at the centre, a bearing line out to the target.</summary>
        void DrawMap(MeshGenerationContext ctx)
        {
            var r = ctx.visualElement.contentRect; var p2 = ctx.painter2D;
            var c = r.center; float R = Mathf.Min(r.width, r.height) * 0.45f;
            var rng = new System.Random(4253);
            for (int i = 0; i < 90; i++)
            {
                var pt = new Vector2(r.xMin + (float)rng.NextDouble() * r.width, r.yMin + (float)rng.NextDouble() * r.height);
                p2.fillColor = XenoarchPalette.A(XenoarchPalette.Text, 0.15f + 0.35f * (float)rng.NextDouble());
                p2.BeginPath(); p2.Arc(pt, 1f + (float)rng.NextDouble() * 1.4f, 0f, 360f); p2.Fill();
            }
            p2.strokeColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.35f); p2.lineWidth = 1f;
            for (int k = 1; k <= 3; k++) { p2.BeginPath(); p2.Arc(c, R * k / 3f, 0f, 360f); p2.Stroke(); }
            // Bearing from RA 14h29.7m (~217 deg); southern declination drawn below the plane.
            float ang = 217.4f * Mathf.Deg2Rad;
            var target = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R * 0.85f;
            p2.strokeColor = XenoarchPalette.A(XenoarchPalette.Primary, 0.8f); p2.lineWidth = 2f;
            p2.BeginPath(); p2.MoveTo(c); p2.LineTo(target); p2.Stroke();
            p2.fillColor = XenoarchPalette.Primary; p2.BeginPath(); p2.Arc(c, 5f, 0f, 360f); p2.Fill();
            p2.fillColor = XenoarchPalette.Highlight; p2.BeginPath(); p2.Arc(target, 8f, 0f, 360f); p2.Fill();
            p2.strokeColor = XenoarchPalette.Highlight; p2.lineWidth = 1.5f; p2.BeginPath(); p2.Arc(target, 14f, 0f, 360f); p2.Stroke();
        }

        // ---------------------------------------------------------------- audio
        static AudioClip _click;
        void Click(float pitch)
        {
            EnsureAudio();
            if (_click == null) _click = DMI.Xenoarch.Ship.ShipSfx.ProceduralClunk("xa_puzzle_click", 2.2f);
            _audio.pitch = pitch; _audio.PlayOneShot(_click, 0.35f);
        }

        void PlayReveal()
        {
            EnsureAudio(); _audio.pitch = 1f;
            const int rate = 44100; float len = 2.4f; int n = (int)(rate * len); var d = new float[n];
            float[] notes = { 392f, 523.25f, 659.25f, 783.99f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float st = k * 0.28f; if (t < st) continue; float u = t - st;
                    v += Mathf.Sin(2f * Mathf.PI * notes[k] * u) * Mathf.Exp(-u * 1.6f) * Mathf.Min(1f, u * 60f) * 0.22f;
                }
                d[i] = v;
            }
            var clip = AudioClip.Create("xa_puzzle_reveal", n, 1, rate, false); clip.SetData(d, 0);
            _audio.PlayOneShot(clip, 0.8f);
            Toast.Show("Star map reconstructed: Proxima Centauri coordinates recovered.", 6f);
        }

        void EnsureAudio()
        {
            if (_audio != null) return;
            _audio = gameObject.AddComponent<AudioSource>(); _audio.playOnAwake = false; _audio.spatialBlend = 0f;
        }

        static void Border(VisualElement e, Color c, float w)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }
    }
}
