using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Handheld surface scanner (on the player). Equip with 1 / D-pad up when the scanner item is in the inventory,
    /// pulse with LMB / RT. A pulse reads the strongest archaeological signal within range and depth (stats from
    /// <see cref="SurfaceScannerDefinition"/>): strength, approx. distance and bearing, depth; accuracy adds error.
    /// Detected sites reach "surface signal"; within the pinpoint radius they are pinpointed and marked (persisted).
    /// </summary>
    public sealed class SurfaceScannerTool : MonoBehaviour
    {
        [SerializeField] private FirstPersonArms arms;
        [SerializeField] private Hand hand = Hand.Right;

        public bool Equipped { get; private set; }
        public string LastReading { get; private set; }

        float _cooldownUntil, _pulseT = -1f;
        UIDocument _doc;
        VisualElement _panel, _pulse;
        Label _text;
        AudioSource _audio;
        AudioClip _beep, _lock;
        int _pulseCount;

        [Header("Direction indicator")]
        [Tooltip("Gold chevron around the crosshair pointing toward the nearest dig site while the scanner is out.")] public bool directionIndicator = true;
        [Tooltip("Radius of the chevron's orbit around the screen centre (px).")] public float indicatorRadius = 96f;
        [Tooltip("Sites further than scanner range x this are not indicated.")] public float indicatorRangeMult = 6f;
        VisualElement _dirPivot, _dirArrow;
        Label _dirLabel;
        float _dirAngle, _dirBlink;
        bool _dirHasAngle;

        static SurfaceScannerDefinition Def => XenoarchRuntime.Session?.Config.surfaceScanner;

        bool HasItem
        {
            get
            {
                var s = XenoarchRuntime.Session; var d = Def;
                return s != null && d != null && s.Inventory.Count(d.itemId) > 0;
            }
        }

        void Awake()
        {
            if (arms == null) arms = GetComponentInChildren<FirstPersonArms>(true);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.35f;
            _beep = MakeBeep();
        }

        void Start() => BuildUi();

        void OnDisable() { if (Equipped) SetEquipped(false); }

        public void SetEquipped(bool on)
        {
            Equipped = on && HasItem;
            if (!Equipped) Refined = false;
            if (arms != null) arms.SetHeld(hand, Equipped);
            XenoarchUI.Show(_panel, Equipped);
            if (Equipped && string.IsNullOrEmpty(LastReading)) SetText(ReadyText, false);
        }

        void LateUpdate()
        {
            // Stack under the minimap (top-right column) instead of overlapping it.
            if (_panel != null && _panel.resolvedStyle.display == DisplayStyle.Flex)
            { _panel.style.top = UI.HudLayout.NextTopRight; _panel.style.right = UI.HudLayout.CornerMargin; }
        }

        void Update()
        {
            if (Equipped && !HasItem) SetEquipped(false);
            UpdateIndicator();
            if (_pulseT >= 0f) AnimatePulse();
            if (_clearAt > 0f && Time.time >= _clearAt) SetText(ReadyText, false);
            if (UIState.BlocksGameplay) { _keyDownAt = -1f; return; }
            var kb = Keyboard.current; var gp = Gamepad.current; var mouse = Mouse.current;
            // Tap 1 = equip / holster (on release). Hold 1 = toggle the refined scanner at a fenced dig site (ZEN-9B).
            if ((kb != null && kb.digit1Key.wasPressedThisFrame) || (gp != null && gp.dpad.up.wasPressedThisFrame)) { _keyDownAt = Time.time; _holdHandled = false; }
            bool keyHeld = (kb != null && kb.digit1Key.isPressed) || (gp != null && gp.dpad.up.isPressed);
            if (_keyDownAt >= 0f && keyHeld && !_holdHandled && Time.time - _keyDownAt >= RefineHoldSeconds) { _holdHandled = true; ToggleRefined(); }
            if (_keyDownAt >= 0f && !keyHeld)
            {
                if (!_holdHandled)
                {
                    if (!HasItem && !Equipped) Debug.Log("[Xenoarch] No handheld scanner in inventory");
                    SetEquipped(!Equipped);
                    if (!Equipped) Refined = false;
                }
                _keyDownAt = -1f;
            }
            if (!Equipped || !Refined) StopSweep();
            if (!Equipped) return;
            if (Refined) { UpdateDetector(mouse, gp); return; }
            bool fire = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (gp != null && gp.rightTrigger.wasPressedThisFrame);
            if (fire && Time.time >= _cooldownUntil) Pulse();
        }

        // ------------------------------------------------------------------ detector sweep (ZEN-26)
        // Refined mode only. Tap LMB / RT = the refined pulse (pinpoint + red flag) on release; hold it past
        // SweepHoldSeconds = metal-detector sweep at the aim point: buried finds of the fenced site within detectRadius of
        // the aim glow faintly through the ground (Xenoarch/XRayGlow, ZTest Always) and a tick repeats faster and higher
        // the closer the aim is. Release stops it. No new UI panel (the grid display waits on mockup approval).
        public const float SweepHoldSeconds = 0.25f;
        [Header("Detector sweep (refined mode, hold LMB)")]
        [Tooltip("Aim reach of the sweep (m).")] public float sweepReach = 10f;
        [Tooltip("Finds within this horizontal distance of the aim point are detected (m).")] public float detectRadius = 2.5f;
        float _lmbDownAt = -1f, _nextTick, _sweepText;
        bool _sweeping;
        readonly System.Collections.Generic.List<DMI.Xenoarch.Excavation.BuriedArtifact> _sweepTargets = new System.Collections.Generic.List<DMI.Xenoarch.Excavation.BuriedArtifact>();
        static Material _glowMat; static AudioClip _tick, _close;
        MaterialPropertyBlock _glowProps;
        Camera _cam;
        static readonly RaycastHit[] _sweepHits = new RaycastHit[12];

        void UpdateDetector(Mouse mouse, Gamepad gp)
        {
            bool down = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (gp != null && gp.rightTrigger.wasPressedThisFrame);
            bool held = (mouse != null && mouse.leftButton.isPressed) || (gp != null && gp.rightTrigger.isPressed);
            if (down) { _lmbDownAt = Time.time; }
            if (_lmbDownAt >= 0f && held && !_sweeping && Time.time - _lmbDownAt >= SweepHoldSeconds) StartSweep();
            if (_sweeping && held) Sweep();
            if (_lmbDownAt >= 0f && !held)
            {
                if (!_sweeping && Time.time >= _cooldownUntil) Pulse();   // tap: refined pulse, flags the find
                StopSweep();
                _lmbDownAt = -1f;
            }
        }

        void StartSweep()
        {
            var site = RefinedSite();
            if (site == null) { Refined = false; SetText("Left the fenced site: back to normal scanning."); Beep(0.5f, 1); _lmbDownAt = -1f; return; }
            _sweeping = true; _nextTick = 0f; _sweepText = 0f;
            _sweepTargets.Clear();
            foreach (var a in FindObjectsByType<DMI.Xenoarch.Excavation.BuriedArtifact>(FindObjectsSortMode.None))
                if (a != null && a.Site == site && a.Grid != null && !a.Grid.recovered) _sweepTargets.Add(a);
            if (_glowMat == null) _glowMat = Resources.Load<Material>("Excavation/XRayGlow");
            if (_tick == null) _tick = Resources.Load<AudioClip>("Audio/Scanner/detector_tick");
            if (_close == null) _close = Resources.Load<AudioClip>("Audio/Scanner/detector_close");
            _glowProps ??= new MaterialPropertyBlock();
            if (arms != null) arms.RaiseTemporarily(hand, 0.5f);
            SetText("DETECTOR SWEEP: move the aim over the ground. The tick speeds up near a find.", false);
        }

        void StopSweep()
        {
            if (!_sweeping) return;
            _sweeping = false;
            _sweepTargets.Clear();
            SetText(ReadyText, false);
        }

        void Sweep()
        {
            if (_cam == null) _cam = PlayerRigLocator.Find()?.ViewCamera ?? Camera.main;
            if (_cam == null) return;
            if (arms != null && Time.frameCount % 20 == 0) arms.RaiseTemporarily(hand, 0.5f);
            // Aim point: nearest hit that is not the player, a trigger or a buried find.
            int n = Physics.RaycastNonAlloc(_cam.transform.position, _cam.transform.forward, _sweepHits, sweepReach, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; Vector3 aim = default;
            for (int i = 0; i < n; i++)
            {
                var h = _sweepHits[i];
                if (h.collider == null || h.distance >= bd || h.collider.transform.IsChildOf(transform.root)) continue;
                if (h.collider.GetComponentInParent<DMI.Xenoarch.Excavation.BuriedArtifact>() != null) continue;
                bd = h.distance; aim = h.point;
            }
            bool aimed = bd < float.MaxValue;
            float best = 0f; DMI.Xenoarch.Excavation.BuriedArtifact nearest = null; float nearestD = 0f;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
            for (int i = _sweepTargets.Count - 1; i >= 0; i--)
            {
                var a = _sweepTargets[i];
                if (a == null || a.Grid == null || a.Grid.recovered) { _sweepTargets.RemoveAt(i); continue; }
                if (!aimed) continue;
                var d = a.transform.position - aim; d.y = 0f;
                float dist = d.magnitude;
                if (dist > detectRadius) continue;
                float c = 1f - dist / detectRadius;
                if (c > best) { best = c; nearest = a; nearestD = dist; }
                DrawGlow(a, Mathf.Lerp(0.12f, 0.5f, c) * pulse);
            }
            // Tick: interval 0.8 s at the edge of the radius down to 0.07 s right over it; pitch rises with it.
            if (nearest != null && Time.time >= _nextTick && _audio != null)
            {
                _nextTick = Time.time + Mathf.Lerp(0.8f, 0.07f, best);
                _audio.pitch = Mathf.Lerp(0.9f, 1.8f, best);
                var clip = best > 0.85f && _close != null ? _close : (_tick != null ? _tick : _beep);
                _audio.PlayOneShot(clip, Mathf.Lerp(0.5f, 1f, best));
            }
            if (Time.time >= _sweepText)
            {
                _sweepText = Time.time + 0.2f;
                SetText(nearest != null
                    ? $"DETECTOR: signal {Mathf.RoundToInt(best * 100f)}%   ~{nearestD:0.0} m from the aim point\nTap LMB / RT to pinpoint and flag it."
                    : aimed ? "DETECTOR SWEEP: no signal here. Sweep the aim over the ground." : "DETECTOR SWEEP: aim at the ground (within " + sweepReach.ToString("0") + " m).", false);
            }
        }

        void DrawGlow(DMI.Xenoarch.Excavation.BuriedArtifact a, float alpha)
        {
            if (_glowMat == null) return;
            _glowProps.SetFloat("_Alpha", Mathf.Clamp01(alpha));
            var rp = new RenderParams(_glowMat) { matProps = _glowProps, layer = 0, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            foreach (var mf in a.GetComponentsInChildren<MeshFilter>(true))
                if (mf != null && mf.sharedMesh != null) Graphics.RenderMesh(rp, mf.sharedMesh, 0, mf.transform.localToWorldMatrix);
        }

        // ------------------------------------------------------------------ refined scanner (ZEN-9B)
        public const float RefineHoldSeconds = 0.6f;
        [Tooltip("Refined scan reach (m, horizontal) for pinpointing buried finds inside a fenced dig site.")] public float refinedRange = 8f;
        /// <summary>Refined mode: pulses pinpoint individual finds of the fenced site you stand in and flag them.</summary>
        public bool Refined { get; private set; }
        float _keyDownAt = -1f; bool _holdHandled;
        string ReadyText => Refined ? "REFINED SCAN ready. Tap LMB / RT: pinpoint + flag the nearest find. Hold LMB / RT: sweep like a metal detector. Hold 1 for normal scan." : "Ready. Pulse to scan (LMB / RT). Hold 1 at a fenced site: refined scan.";

        /// <summary>The fenced (marked) major dig site the player stands in or next to (6 m), if any.</summary>
        DMI.Xenoarch.Excavation.ExcavationSite RefinedSite()
        {
            Vector3 me = transform.position;
            DMI.Xenoarch.Excavation.ExcavationSite best = null; float bd = 6f;
            foreach (var site in DMI.Xenoarch.Excavation.ExcavationSite.Active)
            {
                if (site == null || site.IsCache || !site.BorderVisible) continue;
                var l = site.transform.InverseTransformPoint(me);
                float dx = Mathf.Max(0f, -l.x, l.x - site.Grid.SizeX), dz = Mathf.Max(0f, -l.z, l.z - site.Grid.SizeZ);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d <= bd) { bd = d; best = site; }
            }
            return best;
        }

        void ToggleRefined()
        {
            if (Refined) { Refined = false; SetText(ReadyText, false); Beep(0.8f, 1); return; }
            if (!HasItem) { Debug.Log("[Xenoarch] No handheld scanner in inventory"); return; }
            var site = RefinedSite();
            if (!Equipped) SetEquipped(true);
            if (site == null) { SetText("Refined scan needs a marked, fenced dig site. Scan to pinpoint a site, then stand inside its fence."); Beep(0.5f, 1); return; }
            Refined = true;
            var map = XenoarchRuntime.Session.DigMaps.Find(site.Definition.Id);
            SetText($"REFINED SCAN: {(map != null ? map.Code + " " : "")}{site.Definition.displayName}\nPulse to pinpoint buried finds within {refinedRange:0} m; each gets a red flag and a spot code.", false);
            Beep(1.6f, 2);
        }

        void RefinedPulse()
        {
            var site = RefinedSite();
            if (site == null) { Refined = false; SetText("Left the fenced site: back to normal scanning."); Beep(0.5f, 1); return; }
            var flags = DMI.Xenoarch.Excavation.DigSiteFlags.Instance;
            if (flags != null && flags.TryPinpoint(site, transform.position, refinedRange, out var map, out var spot, out var find))
            {
                var c = site.CellCentre(find.x, find.y, find.z);
                float ground = DMI.Xenoarch.Excavation.ExcavationSite.GroundAt(c, site.transform.position.y);
                var to = c - transform.position; to.y = 0f;
                float rel = Mathf.DeltaAngle(ViewYaw(), Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                SetText($"FIND PINPOINTED: {map.SpotCode(spot)}\n~{to.magnitude:0.0} m {Direction(rel)}, about {Mathf.Max(0f, ground - c.y):0.0} m down. Red flag placed; marked on the dig site map (N).");
                if (_audio != null) { _audio.pitch = 1.2f; _audio.PlayOneShot(_lock ??= MakeLock(), 0.9f); }
                return;
            }
            int left = 0;
            var m = XenoarchRuntime.Session.DigMaps.Find(site.Definition.Id);
            foreach (var a in site.Grid.artifacts) if (!a.recovered && (m == null || m.SpotFor(a.index) == null)) left++;
            SetText(left == 0 ? $"No unflagged finds left in {(m != null ? m.Code : "this site")}." : $"No unflagged find within {refinedRange:0} m. {left} more buried in this site: move around and pulse again.");
            Beep(0.7f, 1);
        }

        public void Pulse()
        {
            var s = XenoarchRuntime.Session; var d = Def;
            if (s == null || d == null) return;
            _cooldownUntil = Time.time + d.cooldown;
            _pulseT = 0f; _pulseCount++;
            if (arms != null) arms.RaiseTemporarily(hand, 0.5f);
            if (Refined) { RefinedPulse(); return; }

            var x = s.Exploration;
            string scene = SceneManager.GetActiveScene().name;
            ScanTriangulation.ResetIfSceneChanged(scene);
            Vector3 me = transform.position;
            float range = ScannerStats.HandheldRange(), maxDepth = ScannerStats.HandheldMaxDepth();
            ArchaeologicalSiteDefinition best = null; float bestScore = 0f, bestDist = 0f;
            string cacheLine = CacheReading(me, range);
            foreach (var site in x.SitesInScene(scene))
            {
                Vector3 flat = site.position - me; flat.y = 0f;
                float dist = flat.magnitude;
                // Standing on a site always gives a (weak) reading, even past the scanner's depth rating: deep sites
                // used to read "No signal" right on top of them, so they never got marked or fenced (ZEN-8).
                bool onSite = dist <= site.radius;
                if (dist > range + site.radius || (site.depth > maxDepth && !onSite)) continue;
                if (!string.IsNullOrEmpty(site.gateFact) && x.Gate != null && !x.Gate(site.gateFact)) continue; // chain-gated sites stay hidden
                if (DMI.Xenoarch.Excavation.ExcavationSite.IsCleared(site)) continue;                            // ZEN-26: all finds recovered
                float score = site.signalStrength * (1f - Mathf.Clamp01(dist / (range + site.radius))) * Mathf.Max(0.15f, 1f - 0.5f * site.depth / Mathf.Max(0.1f, maxDepth));
                if (score > bestScore) { best = site; bestScore = score; bestDist = dist; }
            }

            if (best == null)
            {
                SetText(cacheLine ?? $"No signal within {range:0} m (max depth {maxDepth:0.#} m).");
                Beep(cacheLine != null ? 1.8f : 0.6f, cacheLine != null ? 2 : 1);
                return;
            }

            // Triangulation: distinct scan positions (and the angle between them) tighten every estimate.
            bool wasPinned = x.Site(best.Id).revealLevel >= (int)SiteRevealLevel.Pinpointed;
            var fix = ScanTriangulation.Observe(best.Id, me, best.position);
            var rng = new System.Random(best.seed ^ (_pulseCount * 7919));
            float err = (1f - ScannerStats.HandheldAccuracy()) * ScanTriangulation.ErrorScale(fix.quality);
            float distErr = bestDist * err * 0.5f;
            float distShown = Mathf.Max(0f, bestDist + distErr * ((float)rng.NextDouble() * 2f - 1f));
            Vector3 to = best.position - me; to.y = 0f;
            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + err * ((float)rng.NextDouble() * 2f - 1f) * 40f;
            float rel = Mathf.DeltaAngle(ViewYaw(), bearing);
            _lastTargetId = best.Id; _lastTargetUntil = Time.time + 90f;
            float depthErr = best.depth * err * 0.4f;
            float depthShown = best.depth + depthErr * ((float)rng.NextDouble() * 2f - 1f);

            bool close = bestDist <= d.pinpointRadius + best.radius * 0.25f;
            bool triangulated = fix.quality >= ScanTriangulation.PinpointFix;
            bool pin = close || triangulated;
            x.Reveal(best.Id, pin ? (int)SiteRevealLevel.Pinpointed : (int)SiteRevealLevel.SurfaceSignal);
            if (pin) { SiteMarkerDirector.Instance?.Refresh(); SiteMarkerDirector.Instance?.Ping(best.Id); }

            string fixLine = $"Fix {Mathf.RoundToInt(fix.quality * 100f)}%  {ScanTriangulation.Advice(fix)}";
            string body = pin
                ? $"PINPOINTED{(triangulated && !close ? " (triangulated)" : "")}: {best.displayName}\n~{distShown:0} m {Direction(rel)}   depth {depthShown:0.0} m ±{Mathf.Max(0.1f, depthErr):0.0}\n{(wasPinned ? "Site already marked." : "Site marked.")}"
                : $"Signal {Mathf.RoundToInt(bestScore * 100f)}%   ~{distShown:0} m ±{Mathf.Max(1f, distErr):0} {Direction(rel)}\nDepth ~{depthShown:0.0} m ±{Mathf.Max(0.1f, depthErr):0.0}\n{fixLine}";
            SetText(body + (cacheLine != null ? "\n" + cacheLine : ""));
            if (_pulse != null) _pulse.style.borderTopColor = _pulse.style.borderBottomColor = _pulse.style.borderLeftColor = _pulse.style.borderRightColor = pin ? XenoarchPalette.Bar : XenoarchPalette.Highlight;
            // Audio: pitch rises with signal and fix; a pinpoint lock plays a quick rising triple.
            if (pin && !wasPinned && _audio != null) { _audio.pitch = 1f; _audio.PlayOneShot(_lock ??= MakeLock(), 0.9f); }
            else Beep(1f + bestScore * 1.2f + fix.quality * 0.6f, pin ? 2 : 1 + Mathf.RoundToInt(bestScore * 3f));
        }

        /// <summary>Random valuable caches: a pulse reveals the nearest uncollected cache within the scan radius (marked
        /// with a gold border, dig it out with the shovel).</summary>
        string CacheReading(Vector3 me, float range)
        {
            var dir = DMI.Xenoarch.Valuables.ValuableSiteDirector.Instance;
            if (dir == null || !dir.Nearest(me, range, false, out var v, out float dist)) return null;
            bool isNew = !XenoarchRuntime.Session.Valuables.IsRevealed(v.id);
            dir.Reveal(v);
            var to = v.position - me; to.y = 0f;
            float rel = Mathf.DeltaAngle(ViewYaw(), Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            return $"VALUABLE SIGNATURE ~{dist:0} m {Direction(rel)}{(isNew ? ". Marked." : "")}";
        }

        static string Direction(float rel)
        {
            float a = Mathf.Abs(rel);
            if (a < 20f) return "ahead";
            if (a > 150f) return "behind";
            return (rel > 0 ? "right" : "left") + (a < 70f ? ", ahead" : a > 110f ? ", behind" : "");
        }

        /// <summary>Scan readings clear after this many seconds (the pinpointed site keeps its square border).</summary>
        public const float ReadingSeconds = 5f;
        float _clearAt = -1f;

        void SetText(string t, bool timed = true)
        {
            LastReading = t; if (_text != null) _text.text = t;
            _clearAt = timed ? Time.time + ReadingSeconds : -1f;
        }

        void Beep(float pitch, int count)
        {
            if (_audio == null || _beep == null) return;
            _audio.pitch = pitch;
            for (int i = 0; i < count; i++) _audio.PlayOneShot(_beep, 1f / (1 + i * 0.3f));
        }

        void AnimatePulse()
        {
            _pulseT += Time.deltaTime;
            float t = _pulseT / 0.9f;
            if (_pulse == null) return;
            if (t >= 1f) { _pulseT = -1f; XenoarchUI.Show(_pulse, false); return; }
            XenoarchUI.Show(_pulse, true);
            float size = Mathf.Lerp(40f, 900f, t);
            _pulse.style.width = size; _pulse.style.height = size;
            _pulse.style.marginLeft = -size / 2; _pulse.style.marginTop = -size / 2;
            _pulse.style.opacity = 1f - t;
        }

        void BuildUi()
        {
            var host = new GameObject("Surface Scanner UI");
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 5;
            var root = _doc.rootVisualElement; DMI.Xenoarch.UI.XenoarchUI.FillRoot(root);
            if (root == null) return;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null) root.styleSheets.Add(uss);
            root.pickingMode = PickingMode.Ignore;
            var fill = new VisualElement(); fill.AddToClassList("xa-fill"); fill.pickingMode = PickingMode.Ignore; root.Add(fill);
            _pulse = new VisualElement(); _pulse.AddToClassList("xa-scan__pulse"); _pulse.pickingMode = PickingMode.Ignore; fill.Add(_pulse);
            XenoarchUI.Show(_pulse, false);
            _panel = new VisualElement(); _panel.AddToClassList("xa-scan"); _panel.pickingMode = PickingMode.Ignore; fill.Add(_panel);
            BuildIndicator(fill);
            var title = new Label("SURFACE SCANNER"); title.AddToClassList("xa-scan__title"); _panel.Add(title);
            _text = new Label(); _text.AddToClassList("xa-scan__text"); _panel.Add(_text);
            var hint = new Label("LMB/RT pulse   scan from different spots to triangulate   1/D-pad up holster"); hint.AddToClassList("xa-hint"); _panel.Add(hint);
            XenoarchUI.Show(_panel, false);
        }

        // ---------------------------------------------------------------- direction indicator
        void BuildIndicator(VisualElement fill)
        {
            _dirPivot = new VisualElement { pickingMode = PickingMode.Ignore };
            _dirPivot.style.position = Position.Absolute; _dirPivot.style.left = Length.Percent(50); _dirPivot.style.top = Length.Percent(50);
            _dirPivot.style.width = 0; _dirPivot.style.height = 0;
            fill.Add(_dirPivot);
            _dirArrow = new VisualElement { pickingMode = PickingMode.Ignore };
            _dirArrow.style.position = Position.Absolute; _dirArrow.style.width = 30; _dirArrow.style.height = 22;
            _dirArrow.style.left = -15; _dirArrow.style.top = -indicatorRadius - 11;
            _dirArrow.generateVisualContent += DrawChevron;
            _dirPivot.Add(_dirArrow);
            _dirLabel = new Label { pickingMode = PickingMode.Ignore };
            _dirLabel.style.position = Position.Absolute; _dirLabel.style.left = Length.Percent(50); _dirLabel.style.top = Length.Percent(50);
            _dirLabel.style.marginLeft = -80; _dirLabel.style.width = 160; _dirLabel.style.marginTop = indicatorRadius + 18;
            _dirLabel.style.unityTextAlign = TextAnchor.MiddleCenter; _dirLabel.style.fontSize = 14; _dirLabel.style.color = XenoarchPalette.Highlight;
            fill.Add(_dirLabel);
            XenoarchUI.Show(_dirPivot, false); XenoarchUI.Show(_dirLabel, false);
        }

        static void DrawChevron(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var r = ctx.visualElement.contentRect;
            p.strokeColor = XenoarchPalette.Highlight; p.lineWidth = 4f; p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(3f, r.height - 3f)); p.LineTo(new Vector2(r.width * 0.5f, 3f)); p.LineTo(new Vector2(r.width - 3f, r.height - 3f));
            p.Stroke();
        }

        string _lastTargetId; float _lastTargetUntil = -1f; Camera _viewCam;

        /// <summary>Heading (deg) the player is looking, from the view camera's flattened forward. Uses the camera's up
        /// vector when looking almost straight down (scanning the ground), where Euler yaw is unreliable.</summary>
        float ViewYaw()
        {
            if (_viewCam == null || !_viewCam.isActiveAndEnabled)
            {
                var rig = DMI.Xenoarch.Player.PlayerRigLocator.Find();
                _viewCam = rig != null && rig.ViewCamera != null ? rig.ViewCamera : Camera.main;
            }
            var cam = _viewCam;
            var t = cam != null ? cam.transform : transform;
            var f = t.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.04f) { f = t.up; f.y = 0f; }
            if (f.sqrMagnitude < 1e-6f) return transform.eulerAngles.y;
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        static ArchaeologicalSiteDefinition SiteInScene(string id, Vector3 from, float maxDist, out float dist)
        {
            dist = 0f;
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null || string.IsNullOrEmpty(id)) return null;
            foreach (var site in x.SitesInScene(SceneManager.GetActiveScene().name))
            {
                if (site.Id != id) continue;
                var f = site.position - from; f.y = 0f;
                dist = f.magnitude;
                return dist <= maxDist ? site : null;
            }
            return null;
        }

        /// <summary>Nearest revealable dig site in this scene (gated chain sites stay hidden), with its flat distance.</summary>
        public static ArchaeologicalSiteDefinition NearestSite(Vector3 from, float maxDist, out float dist)
        {
            dist = 0f;
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null) return null;
            ArchaeologicalSiteDefinition best = null; float bd = maxDist;
            foreach (var site in x.SitesInScene(SceneManager.GetActiveScene().name))
            {
                if (!string.IsNullOrEmpty(site.gateFact) && x.Gate != null && !x.Gate(site.gateFact)) continue;
                if (DMI.Xenoarch.Excavation.ExcavationSite.IsCleared(site)) continue;   // ZEN-26
                var f = site.position - from; f.y = 0f;
                float d = f.magnitude;
                if (d < bd) { bd = d; best = site; }
            }
            dist = bd;
            return best;
        }

        static ArchaeologicalSiteDefinition _placeholder;
        static ArchaeologicalSiteDefinition Placeholder => _placeholder != null ? _placeholder : (_placeholder = ScriptableObject.CreateInstance<ArchaeologicalSiteDefinition>());

        void UpdateIndicator()
        {
            if (_dirPivot == null) return;
            var d = Def;
            bool show = directionIndicator && Equipped && d != null && !UIState.BlocksGameplay;
            ArchaeologicalSiteDefinition site = null; float dist = 0f;
            if (show)
            {
                // Point at the site the last pulse read out (so the chevron and the "ahead/left/behind" text agree);
                // otherwise the nearest one.
                float lim = ScannerStats.HandheldRange() * Mathf.Max(1f, indicatorRangeMult);
                site = Time.time < _lastTargetUntil ? SiteInScene(_lastTargetId, transform.position, lim, out dist) : null;
                if (site == null) site = NearestSite(transform.position, lim, out dist);
            }
            bool cache = false; Vector3 targetPos = site != null ? site.position : Vector3.zero; float targetRadius = site != null ? site.radius : 0f; int targetSeed = site != null ? site.seed : 0;
            var vdir = DMI.Xenoarch.Valuables.ValuableSiteDirector.Instance;
            if (show && vdir != null && vdir.Nearest(transform.position, site != null ? dist : ScannerStats.HandheldRange() * Mathf.Max(1f, indicatorRangeMult), true, out var cv, out float cd))
            { cache = true; dist = cd; targetPos = cv.position; targetRadius = 0.9f; targetSeed = cv.seed; site = site ?? Placeholder; }
            if (site == null) { XenoarchUI.Show(_dirPivot, false); XenoarchUI.Show(_dirLabel, show); if (show) _dirLabel.text = "NO SITE IN RANGE"; _dirHasAngle = false; return; }
            bool onSite = dist <= targetRadius;
            XenoarchUI.Show(_dirPivot, !onSite); XenoarchUI.Show(_dirLabel, true);

            float yaw = ViewYaw();
            var to = targetPos - transform.position;
            float err = (1f - ScannerStats.HandheldAccuracy()) * (cache ? 1f : ScanTriangulation.ErrorScale(ScanTriangulation.Quality(site.Id)));
            float wobble = (Mathf.PerlinNoise(Time.time * 0.6f, targetSeed * 0.013f) * 2f - 1f) * err * 35f; // "general direction"
            float rel = Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + wobble);
            _dirAngle = _dirHasAngle ? Mathf.LerpAngle(_dirAngle, rel, 1f - Mathf.Exp(-Time.deltaTime * 8f)) : rel; _dirHasAngle = true;
            _dirPivot.style.rotate = new Rotate(new Angle(_dirAngle, AngleUnit.Degree));

            // Blink like the pulse ring: faster when closer.
            float near = 1f - Mathf.Clamp01(dist / (ScannerStats.HandheldRange() * Mathf.Max(1f, indicatorRangeMult)));
            _dirBlink += Time.deltaTime * Mathf.Lerp(1.2f, 5f, near);
            float a = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(_dirBlink * Mathf.PI * 2f));
            _dirArrow.style.opacity = a;
            float shown = Mathf.Max(5f, Mathf.Round(dist * (1f + err * 0.3f) / 5f) * 5f);
            _dirLabel.text = onSite ? (cache ? "DIG HERE" : "ON SITE") : $"{(cache ? "CACHE" : "SITE")} ~{shown:0} m";
            _dirLabel.style.opacity = onSite ? 1f : Mathf.Lerp(0.6f, 1f, a);
        }

        /// <summary>Pinpoint lock: three quick rising tones.</summary>
        static AudioClip MakeLock()
        {
            const int rate = 22050; int seg = rate / 14, n = seg * 3;
            var data = new float[n]; float[] f = { 990f, 1320f, 1760f };
            for (int i = 0; i < n; i++) { int k = i / seg; float t = (i % seg) / (float)rate; data[i] = Mathf.Sin(2f * Mathf.PI * f[k] * t) * Mathf.Exp(-t * 30f) * 0.55f; }
            var clip = AudioClip.Create("ScannerLock", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeBeep()
        {
            const int rate = 22050; int n = rate / 12;
            var data = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)rate; data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 40f) * 0.6f; }
            var clip = AudioClip.Create("ScannerBeep", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
