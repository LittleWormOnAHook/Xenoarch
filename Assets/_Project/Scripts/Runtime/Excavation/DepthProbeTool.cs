using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Depth Probe (ZEN-16): equip from its hotbar key (default 6), aim at the dug surface and pulse (LMB) or hold LMB
    /// for live readings. For the nearest 1-3 buried objects (by upgrade tier) below that point it shows the vertical
    /// distance (bands at Mk I, metres with error at Mk II/III) and the sideways offset, with a ring on the surface over
    /// each one. Never says what an object is. Warns "very close" under DepthProbeDefinition.veryClose.
    /// Added to the player next to <see cref="DigTool"/>.
    /// </summary>
    public sealed class DepthProbeTool : MonoBehaviour
    {
        public bool Equipped { get; private set; }
        DepthProbeDefinition _def;
        IPlayerRig _rig;
        DigTool _dig;
        SurfaceScannerTool _scanner;
        FirstPersonArms _arms;
        UIDocument _doc; VisualElement _panel; Label _head, _body;
        float _nextPulse, _showUntil;
        readonly List<Track> _tracks = new List<Track>();
        readonly List<LineRenderer> _rings = new List<LineRenderer>();
        LineRenderer _pulseRing; float _pulseAt = -10f; Vector3 _pulsePoint;
        static Material _lineMat;
        static readonly Color[] TrackColors = { new Color(0.41f, 0.87f, 0.84f), new Color(1f, 0.66f, 0.40f), new Color(0.85f, 0.55f, 1f) };
        static readonly Color WarnColor = new Color(1f, 0.35f, 0.3f);

        struct Track { public Vector3 surface, buried; public float depth, shownDepth, offset; public string dir; }

        static readonly Key[] SlotKeys = { Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8 };

        /// <summary>Add the probe to the player (next to DigTool); idempotent.</summary>
        public static void Ensure(Transform root)
        {
            if (root != null && root.GetComponent<DepthProbeTool>() == null) root.gameObject.AddComponent<DepthProbeTool>();
        }

        void Awake()
        {
            _def = DepthProbeDefinition.Load();
            _rig = GetComponentInParent<IPlayerRig>() ?? PlayerRigLocator.Find();
            _dig = GetComponent<DigTool>();
            _scanner = GetComponentInChildren<SurfaceScannerTool>(true);
            _arms = GetComponentInChildren<FirstPersonArms>(true);
        }

        void OnDisable() => SetEquipped(false);

        bool HasItem => XenoarchRuntime.Session != null && XenoarchRuntime.Session.Inventory.Count(_def.itemId) > 0;
        int Level => XenoarchRuntime.Session != null ? XenoarchRuntime.Session.Exploration.Ship.depthProbeLevel : 0;

        public void SetEquipped(bool on)
        {
            on = on && HasItem;
            if (on == Equipped) return;
            Equipped = on;
            if (on)
            {
                if (_dig != null) _dig.Holster();
                if (_scanner != null && _scanner.Equipped) _scanner.SetEquipped(false);
                EnsureUi();
                _tracks.Clear(); _showUntil = 0f;
                SetText($"{_def.displayName} {_def.Tier(Level).label}", "Aim at the dug surface. LMB: probe   hold LMB: live");
            }
            else ClearMarkers();
            if (_arms != null) _arms.SetHeld(Hand.Right, Equipped);
            if (_panel != null) XenoarchUI.Show(_panel, on);
        }

        void Update()
        {
            var s = XenoarchRuntime.Session;
            if (s == null) return;
            var kb = Keyboard.current; var mouse = Mouse.current; var gp = Gamepad.current;
            if (!UIState.BlocksGameplay && kb != null)
            {
                for (int k = 0; k < SlotKeys.Length && k + 1 < DMI.Xenoarch.Inventory.Hotbar.Size; k++)
                {
                    if (!kb[SlotKeys[k]].wasPressedThisFrame) continue;
                    if (s.Hotbar.At(k + 1) == _def.itemId) { if (!Equipped && !HasItem) Toast.Show(DigTool.NotOwnedText(_def.displayName), 3f); else SetEquipped(!Equipped); }
                    else if (Equipped) SetEquipped(false);
                }
            }
            if (!Equipped) return;
            if (_dig == null) _dig = GetComponent<DigTool>();
            if (!HasItem || (_dig != null && _dig.Equipped >= 0) || (_scanner != null && _scanner.Equipped)) { SetEquipped(false); return; }
            if (UIState.BlocksGameplay) return;

            bool pressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (gp != null && gp.rightTrigger.wasPressedThisFrame);
            bool held = (mouse != null && mouse.leftButton.isPressed) || (gp != null && gp.rightTrigger.isPressed);
            if (pressed || (held && Time.time >= _nextPulse))
            {
                _nextPulse = Time.time + Mathf.Max(0.05f, _def.holdInterval);
                Probe(pressed);
            }
            if (!held && _showUntil > 0f && Time.time > _showUntil) { _showUntil = 0f; _tracks.Clear(); ClearMarkers(); SetText($"{_def.displayName} {_def.Tier(Level).label}", "Aim at the dug surface. LMB: probe   hold LMB: live"); }
            AnimatePulse();
        }

        void Probe(bool fresh)
        {
            var cam = _rig?.ViewCamera != null ? _rig.ViewCamera : Camera.main;
            if (cam == null) return;
            var tier = _def.Tier(Level);
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var hits = Physics.RaycastAll(ray, _def.reach, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
            RaycastHit hit = default; bool ok = false;
            foreach (var h in hits) { if (_rig != null && h.transform.IsChildOf(_rig.Root)) continue; hit = h; ok = true; break; }
            if (!ok) { SetText($"{_def.displayName} {tier.label}", "No surface in reach. Aim at the ground you have dug."); return; }
            var p = hit.point;
            if (fresh) { _pulsePoint = p; _pulseAt = Time.time; }

            // Buried objects: unrecovered artifacts (top of their placeholder) and uncollected cache loot.
            var found = new List<(Vector3 top, int key)>();
            foreach (var a in FindObjectsByType<BuriedArtifact>())
            {
                if (a == null || a.Definition == null) continue;
                float half = BuriedArtifact.Extents(a.Definition).y * a.transform.lossyScale.y * 0.5f;
                found.Add((a.transform.position + Vector3.up * half, a.GetEntityId().GetHashCode()));
            }
            var vd = DMI.Xenoarch.Valuables.ValuableSiteDirector.Instance;
            var session = XenoarchRuntime.Session;
            if (vd != null)
                foreach (var c in vd.Sites)
                {
                    if (session.Valuables.IsCollected(c.id) || (c.position - p).sqrMagnitude > 400f) continue;
                    float ground = ExcavationSite.TerrainHeight(c.position) ?? c.position.y;
                    found.Add((new Vector3(c.position.x, ground - c.depth, c.position.z), c.id.GetHashCode()));
                }

            _tracks.Clear();
            var cands = new List<(float dist, Track t)>();
            foreach (var (top, key) in found)
            {
                var off = top - p; float depth = -off.y; off.y = 0f;
                if (depth < -0.05f || depth > tier.maxDepth || off.magnitude > tier.range) continue;
                depth = Mathf.Max(0f, depth);
                float err = ((Mathf.Abs((key * 2654435761L) % 1000) / 1000f) * 2f - 1f) * tier.noise; // stable per object
                var surf = new Vector3(top.x, p.y, top.z);
                if (Physics.Raycast(surf + Vector3.up * 2f, Vector3.down, out var gh, 4f, ~0, QueryTriggerInteraction.Ignore)) surf = gh.point;
                cands.Add((Mathf.Sqrt(depth * depth + off.sqrMagnitude), new Track { surface = surf, buried = top, depth = depth, shownDepth = Mathf.Max(0f, depth + err), offset = off.magnitude, dir = Direction(cam.transform, off) }));
            }
            foreach (var c in cands.OrderBy(c => c.dist).Take(Mathf.Clamp(tier.tracks, 1, 3))) _tracks.Add(c.t);
            _showUntil = Time.time + _def.readingSeconds;

            var lines = new List<string>();
            bool warn = false;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var t = _tracks[i];
                string depthText = tier.bandsOnly ? _def.Band(t.shownDepth) : $"~{t.shownDepth:0.00} m (+/-{tier.noise:0.00})";
                string where = t.offset < _def.straightDown ? "straight down" : $"{t.offset:0.0} m {t.dir}";
                bool close = t.depth < _def.veryClose;
                warn |= close;
                lines.Add($"{(_tracks.Count > 1 ? $"[{i + 1}] " : "")}Object {depthText} below, {where}{(close ? "  VERY CLOSE" : "")}");
            }
            if (_tracks.Count == 0) lines.Add($"Nothing within {tier.range:0.0} m across and {tier.maxDepth:0.0} m down.");
            if (warn) lines.Add("Very close: switch to the brush or the excavator's Fine focus (T).");
            SetText($"{_def.displayName} {tier.label}   tracks {tier.tracks}   reach {tier.range:0.0} m / {tier.maxDepth:0.0} m deep", string.Join("\n", lines), warn);
            DrawMarkers();
            DrawHologram(tier, p);
        }

        static string Direction(Transform cam, Vector3 off)
        {
            var fwd = cam.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = cam.up; fwd.y = 0f; fwd.Normalize();
            float a = Vector3.SignedAngle(fwd, off.normalized, Vector3.up);
            if (Mathf.Abs(a) <= 30f) return "ahead";
            if (Mathf.Abs(a) >= 150f) return "behind";
            if (a > 0f) return Mathf.Abs(a) < 70f ? "ahead-right" : Mathf.Abs(a) > 110f ? "behind-right" : "right";
            return Mathf.Abs(a) < 70f ? "ahead-left" : Mathf.Abs(a) > 110f ? "behind-left" : "left";
        }

        // ---------------------------------------------------------------- markers
        static Material LineMat()
        {
            if (_lineMat != null) return _lineMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (sh == null) return null;
            _lineMat = new Material(sh) { name = "DepthProbe Ring" };
            Scenes.ShaderPrewarm.Warm(_lineMat);
            return _lineMat;
        }

        LineRenderer Ring(string name)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = true; lr.positionCount = 32; lr.widthMultiplier = 0.02f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.sharedMaterial = LineMat();
            return lr;
        }

        static void Circle(LineRenderer lr, Vector3 c, float r, Color col)
        {
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i / (float)lr.positionCount * Mathf.PI * 2f;
                lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0.03f, Mathf.Sin(a) * r));
            }
            lr.startColor = col; lr.endColor = col;
            if (lr.sharedMaterial != null) lr.sharedMaterial.SetColor("_BaseColor", Color.white);
        }

        void DrawMarkers()
        {
            while (_rings.Count < _tracks.Count) _rings.Add(Ring($"DepthProbe Mark {_rings.Count + 1}"));
            for (int i = 0; i < _rings.Count; i++)
            {
                bool on = i < _tracks.Count;
                _rings[i].gameObject.SetActive(on);
                if (!on) continue;
                var t = _tracks[i];
                // Ring size grows with depth (a deeper object reads as a vaguer spot).
                Circle(_rings[i], t.surface, Mathf.Lerp(0.12f, 0.45f, Mathf.Clamp01(t.depth / 1.5f)), t.depth < _def.veryClose ? WarnColor : TrackColors[i % TrackColors.Length]);
            }
        }

        DepthProbeHologram _holo;
        readonly List<DepthProbeHologram.Item> _holoItems = new List<DepthProbeHologram.Item>();

        /// <summary>Top tier only: hologram scan volume and wire blobs for the tracked objects.</summary>
        void DrawHologram(DepthProbeTier tier, Vector3 p)
        {
            if (!tier.hologram) { if (_holo != null) _holo.Hide(); return; }
            if (_holo == null) _holo = gameObject.AddComponent<DepthProbeHologram>();
            _holoItems.Clear();
            for (int i = 0; i < _tracks.Count; i++)
            {
                var t = _tracks[i];
                _holoItems.Add(new DepthProbeHologram.Item { surface = t.surface, buried = t.buried, color = t.depth < _def.veryClose ? WarnColor : TrackColors[i % TrackColors.Length] });
            }
            _holo.Show(p, tier.range, tier.maxDepth, _holoItems);
        }

        void AnimatePulse()
        {
            float age = Time.time - _pulseAt;
            if (age > 0.6f) { if (_pulseRing != null) _pulseRing.gameObject.SetActive(false); return; }
            if (_pulseRing == null) _pulseRing = Ring("DepthProbe Pulse");
            _pulseRing.gameObject.SetActive(true);
            var col = TrackColors[0]; col.a = 1f - age / 0.6f;
            Circle(_pulseRing, _pulsePoint, Mathf.Lerp(0.05f, 0.6f, age / 0.6f), col);
        }

        void ClearMarkers()
        {
            if (_holo != null) _holo.Hide();
            foreach (var r in _rings) if (r != null) r.gameObject.SetActive(false);
            if (_pulseRing != null) _pulseRing.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var r in _rings) if (r != null) Destroy(r.gameObject);
            if (_pulseRing != null) Destroy(_pulseRing.gameObject);
            if (_doc != null) Destroy(_doc.gameObject);
        }

        // ---------------------------------------------------------------- HUD
        void EnsureUi()
        {
            if (_doc != null) return;
            var host = new GameObject("Depth Probe UI");
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 12;
            var root = _doc.rootVisualElement; XenoarchUI.FillRoot(root); root.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null) root.styleSheets.Add(uss);
            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            _panel.style.position = Position.Absolute; _panel.style.left = HudLayout.Margin; _panel.style.bottom = 200;
            _panel.style.width = 520; _panel.style.paddingLeft = 14; _panel.style.paddingRight = 14; _panel.style.paddingTop = 10; _panel.style.paddingBottom = 10;
            _panel.style.backgroundColor = new Color(0.05f, 0.06f, 0.07f, 0.78f);
            _panel.style.borderLeftWidth = 3; _panel.style.borderLeftColor = XenoarchPalette.Highlight;
            _head = new Label { pickingMode = PickingMode.Ignore }; _head.style.fontSize = 14; _head.style.color = XenoarchPalette.Highlight; _head.style.unityFontStyleAndWeight = FontStyle.Bold;
            _body = new Label { pickingMode = PickingMode.Ignore }; _body.style.fontSize = 15; _body.style.color = XenoarchPalette.Text; _body.style.whiteSpace = WhiteSpace.Normal; _body.style.marginTop = 4;
            _panel.Add(_head); _panel.Add(_body);
            root.Add(_panel);
            XenoarchUI.Show(_panel, false);
        }

        void SetText(string head, string body, bool warn = false)
        {
            if (_head == null) return;
            _head.text = head.ToUpperInvariant();
            _body.text = body;
            _body.style.color = warn ? XenoarchPalette.WarnText : XenoarchPalette.Text;
        }
    }
}
