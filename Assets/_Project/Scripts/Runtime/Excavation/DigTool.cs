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
    /// Excavation tools on the player (added at runtime by <see cref="ExcavationDirector"/>). 2 shovel, 3 precision tool,
    /// 4 brush, 5 powered drill (each needs its item in the inventory; same key again holsters). Hold LMB / RT on a dig
    /// site to dig: strokes go to the site's cell grid, which decides removal, refusals and artifact damage.
    /// </summary>
    public sealed class DigTool : MonoBehaviour
    {
        public const float Reach = 3f;
        public int Equipped { get; private set; } = -1;

        [Header("Dig strength (tool tiers are DigToolDefinition assets; this scales them all)")]
        [Tooltip("Global chunk-size multiplier for every tool (1 = as authored).")] [Range(0.25f, 4f)] public float strengthScale = 1f;
        [Tooltip("Show the projected aim ring on the ground while a dig tool is out.")] public bool aimRing = true;

        /// <summary>The equipped tool with the tunable strength applied.</summary>
        public DigToolSpec Current
        {
            get
            {
                if (Equipped < 0) return default;
                var t = DigTools.All[Equipped];
                t.removal *= strengthScale;
                return t;
            }
        }

        DigAimRing _ring;

        FirstPersonArms _arms;
        SurfaceScannerTool _scanner;
        IPlayerRig _rig;
        float _nextStroke, _flashUntil;
        Label _line1, _line2;
        VisualElement _panel;
        AudioSource _audio;
        AudioClip _scrape, _clunk;
        string _flash;
        float _digUntil; // "Digging:" while strokes keep landing

        void Awake()
        {
            _arms = GetComponentInChildren<FirstPersonArms>(true);
            _scanner = GetComponentInChildren<SurfaceScannerTool>(true);
            _rig = GetComponent<IPlayerRig>() ?? GetComponentInParent<IPlayerRig>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.4f;
            _scrape = Noise("scrape", 0.16f, 0.5f);
            _clunk = Noise("clunk", 0.08f, 0.15f);
        }

        void Start() { BuildUi(); if (aimRing) _ring = DigAimRing.Create(transform); }
        void OnDisable() => Holster();

        static int ToolIndex(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            var all = DigTools.All;
            for (int i = 0; i < all.Length; i++) if (all[i].itemId == itemId) return i;
            return -1;
        }

        /// <summary>Hotbar key (2-8) a tool is on, or 0.</summary>
        static int KeyFor(int toolIndex)
        {
            var bar = XenoarchRuntime.Session?.Hotbar;
            if (bar == null || toolIndex < 0) return 0;
            for (int k = 1; k < DMI.Xenoarch.Inventory.Hotbar.Size; k++) if (bar.At(k) == DigTools.All[toolIndex].itemId) return k + 1;
            return 0;
        }

        /// <summary>Feedback for a hotbar key whose tool is not in the backpack (ZEN-21).</summary>
        public static string NotOwnedText(string name) => $"You don't have a {name} yet. Craft it aboard the ship, then press the key again.";

        static bool Has(string itemId) => XenoarchRuntime.Session != null && XenoarchRuntime.Session.Inventory.Count(itemId) > 0;

        public void Equip(int index)
        {
            if (index < 0 || index >= DigTools.All.Length || index == Equipped) { Holster(); return; }
            if (!Has(DigTools.All[index].itemId)) { Toast.Show(NotOwnedText(DigTools.All[index].name), 3f); return; }
            if (_scanner != null && _scanner.Equipped) _scanner.SetEquipped(false);
            Equipped = index;
            if (_arms != null) _arms.SetHeld(Hand.Right, true);
            XenoarchUI.Show(_panel, true);
        }

        public void Holster()
        {
            if (Equipped < 0) return;
            Equipped = -1;
            if (_arms != null) _arms.SetHeld(Hand.Right, false);
            XenoarchUI.Show(_panel, false);
            if (_ring != null) _ring.Hide();
        }

        static readonly Key[] SlotKeys = { Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };

        void Update()
        {
            if (XenoarchRuntime.Session == null) return;
            var kb = Keyboard.current; var gp = Gamepad.current; var mouse = Mouse.current;
            if (!UIState.BlocksGameplay && kb != null)
            {
                // Keys 2-8 follow the hotbar (drag tools onto it in the inventory); default layout = 2 shovel, 3 precision...
                var bar = XenoarchRuntime.Session.Hotbar;
                for (int k = 0; k < SlotKeys.Length && k + 1 < DMI.Xenoarch.Inventory.Hotbar.Size; k++)
                {
                    if (!kb[SlotKeys[k]].wasPressedThisFrame) continue;
                    int i = ToolIndex(bar.At(k + 1));
                    if (i >= 0) Equip(Equipped == i ? -1 : i);
                    else if (Equipped >= 0) Holster();
                }
            }
            if (Equipped < 0) return;
            if (!Has(Current.itemId) || (_scanner != null && _scanner.Equipped)) { Holster(); return; }

            var cam = _rig?.ViewCamera != null ? _rig.ViewCamera : Camera.main;
            ExcavationSite site = null; RaycastHit hit = default;
            bool aimed = cam != null && Aim(cam, out hit);
            bool inReach = aimed && hit.distance <= Reach;
            if (aimed) site = SiteFor(hit);
            var tool = Current;

            bool held = !UIState.BlocksGameplay && ((mouse != null && mouse.leftButton.isPressed) || (gp != null && gp.rightTrigger.isPressed));
            bool refusedHere = false;
            if (held && site != null && inReach && Time.time >= _nextStroke)
            {
                _nextStroke = Time.time + 1f / Mathf.Max(0.5f, tool.rate);
                var r = site.Stroke(hit.point, tool);
                if (r.accepted)
                {
                    _digUntil = Time.time + 0.6f;
                    _audio.pitch = Random.Range(0.85f, 1.15f);
                    _audio.PlayOneShot(_scrape);
                    if (_arms != null && !_arms.Reaching) _arms.Reach(hit.point, null);
                    StrataFinds(site, r);
                    DigSiteFlags.Instance?.OnStroke(site, hit.point);
                    if (r.emerged != null)
                    {
                        Toast.Show("Something is emerging from the soil! Switch to the precision tool (3) or brush (4).", 4f);
                        _audio.pitch = 1.6f; _audio.PlayOneShot(_clunk, 0.7f);
                    }
                    if (r.jarred) Flash($"The {tool.name.ToLowerInvariant()} jarred something buried just below! Condition {r.damaged.condition:P0}. Go gently: precision tool (3) or brush (4).");
                    else if (r.damageDealt > 0.001f) Flash($"Damaging the find! Condition {r.damaged.condition:P0}. Switch to the precision tool (3) or brush (4).");
                    else if (r.hiddenBelow != null && tool.crude) Flash("The soil feels different here: something solid below. Slow down with the precision tool (3).");
                }
                else if (r.refused)
                {
                    refusedHere = true;
                    _audio.PlayOneShot(_clunk);
                    Flash(r.needsBetterTool
                        ? $"This {r.layer.label} needs a tool rated {r.layer.minToolRating:0.#}+. {ToolsFor(r.layer.minToolRating)}"
                        : "Bedrock: the site floor. Nothing below.");
                }
            }
            if (_ring != null)
            {
                if (!aimed || UIState.BlocksGameplay) _ring.Hide();
                else
                {
                    float rad = tool.radius;
                    if (site != null && site.MirrorsToSurface) rad = Mathf.Max(rad, site.CarveRadius * 0.8f);
                    var st = site == null ? DigAimRing.State.Neutral : (inReach && !refusedHere ? DigAimRing.State.Valid : DigAimRing.State.Invalid);
                    _ring.Show(hit.point, hit.normal, rad, st);
                }
            }
            if (!inReach) site = null;
            UpdateHud(site, hit);
        }

        static readonly RaycastHit[] _hits = new RaycastHit[12];

        /// <summary>Nearest hit along the view ray (up to 6 m), skipping the player's own colliders, triggers and buried
        /// find visuals, so a freshly carved Digger chunk or an exposed artifact never steals the aim.</summary>
        bool Aim(Camera cam, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.distance >= bd) continue;
                if (h.collider.transform.IsChildOf(transform.root)) continue;           // the player rig, held tools
                if (h.collider.GetComponentInParent<BuriedArtifact>() != null) continue; // finds: aim through to the soil
                bd = h.distance; best = h;
            }
            return bd < float.MaxValue;
        }

        static ExcavationSite SiteFor(in RaycastHit hit)
        {
            var s = hit.collider.GetComponentInParent<ExcavationSite>();
            return s != null ? s : ExcavationSite.At(hit.point);
        }

        static readonly System.Random _strataRng = new System.Random();

        /// <summary>Layered ground: the cells this stroke cut yield the materials of their depth band (into the backpack).</summary>
        static void StrataFinds(ExcavationSite site, in StrokeResult r)
        {
            var s = XenoarchRuntime.Session;
            if (s == null || site == null || site.Definition == null || r.cut == null || r.cut.Count == 0) return;
            var profile = Materials.StrataProfile.For(s.Config.strata, site.Definition.bodyId);
            if (profile == null) return;
            float frac = r.removed / r.cut.Count;
            var cuts = new System.Collections.Generic.List<(int, float)>();
            foreach (var c in r.cut) cuts.Add((c.y, frac));
            foreach (var (id, n) in Materials.StrataYield.Dig(s.Materials, profile, site.Definition.Id, site.Grid.cell, cuts, _strataRng, s.Config.digYieldMultiplier))
            {
                int lost = s.Inventory.Add(id, n);
                string name = s.Materials.Name(id);
                UI.Toast.Show(lost >= n ? $"Backpack full: {name} left in the spoil" : $"+{n - lost} {name}");
            }
        }

        /// <summary>Which tools can cut this hardness, split into owned and not yet owned (crafting hint).</summary>
        /// <summary>Faster tools for a hard band (owned ones with their key, else what to craft).</summary>
        static string ToolsFor(float hardness)
        {
            string owned = null, missing = null;
            var all = DigTools.All;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t.maxHardness < hardness) continue;
                int key = KeyFor(i);
                string n = key > 0 ? $"{t.name.ToLowerInvariant()} ({key})" : $"{t.name.ToLowerInvariant()} (put it on the hotbar)";
                if (Has(t.itemId)) owned = owned == null ? n : owned + " or " + n; else missing = missing == null ? t.name.ToLowerInvariant() : missing + ", " + t.name.ToLowerInvariant();
            }
            if (owned != null) return $"The {owned} cuts it faster.";
            return missing != null ? $"The {missing} cuts it faster (craft it from the inventory, I)." : "";
        }

        void Flash(string text) { _flash = text; _flashUntil = Time.time + 2.5f; }

        public static string Label(SoilMaterial m) => m switch { SoilMaterial.Dust => "surface dust", SoilMaterial.Loose => "loose regolith", SoilMaterial.Compacted => "compacted layer", SoilMaterial.Ice => "ice", SoilMaterial.Rock => "rock", _ => "bedrock" };

        void UpdateHud(ExcavationSite site, RaycastHit hit)
        {
            if (_line1 == null) return;
            string l1 = $"{Current.name}   [hold LMB to dig]   1 scanner  {ToolKeys()}";
            string l2;
            if (site == null) l2 = "Aim at a dig site within 3 m.";
            else
            {
                // Material at the actual aim depth (a lump above the dug floor reads as the band it sits in).
                var lay = site.LayerAtPoint(hit.point, out float depth, out bool leftover);
                var cur = Current;
                string verb = Time.time < _digUntil ? "Digging" : "Aiming";
                string toolState;
                if (lay.kind == SoilMaterial.Bedrock) toolState = "site floor reached";
                else if (!DigTools.CanCut(lay, cur)) toolState = $"needs a tool rated {lay.minToolRating:0.#}+";
                else if (lay.hardness > cur.maxHardness) toolState = $"slow with the {cur.shortName ?? cur.name}";
                else toolState = "tool OK";
                l2 = $"{verb}: {lay.label}  (hardness {lay.hardness:0.#}, {toolState})   depth {depth:0.00} m{(leftover ? "   leftover lump: levelling it" : "")}   |   {site.Definition.displayName}";
                if (lay.kind != SoilMaterial.Bedrock && (!DigTools.CanCut(lay, cur) || lay.hardness > cur.maxHardness))
                {
                    string more = ToolsFor(!DigTools.CanCut(lay, cur) ? lay.minToolRating : lay.hardness);
                    if (!string.IsNullOrEmpty(more)) l2 += "   " + more;
                }
                GridArtifact near = null; float best = 1.2f;
                foreach (var a in site.Grid.artifacts)
                {
                    if (a.recovered) continue;
                    float d = Vector3.Distance(site.CellCentre(a.x, a.y, a.z), hit.point);
                    if (d < best && site.Exposure(a) > 0.01f) { best = d; near = a; }
                }
                if (near != null)
                {
                    float e = site.Exposure(near);
                    string state = e >= BuriedArtifact.SafeExposure ? "READY: hold E to recover"
                        : e >= BuriedArtifact.MinExposure ? $"can pry out (E), safe at {BuriedArtifact.SafeExposure:P0}"
                        : $"recoverable at {BuriedArtifact.MinExposure:P0}";
                    l2 += $"   |   find {Bar(e)} {e:P0} exposed, condition {near.condition:P0}, {state}";
                }
            }
            if (_flash != null && Time.time < _flashUntil) l2 = _flash;
            _line1.text = l1; _line2.text = l2;
        }

        /// <summary>Tool keys; tools not in the inventory are marked "(none)".</summary>
        static string ToolKeys()
        {
            var sb = new System.Text.StringBuilder();
            var all = DigTools.All;
            for (int i = 0; i < all.Length && i < SlotKeys.Length; i++)
            {
                int key = KeyFor(i); if (key <= 0) continue;
                sb.Append($"{key} {(string.IsNullOrEmpty(all[i].shortName) ? all[i].name : all[i].shortName)}{(Has(all[i].itemId) ? "" : " (none)")}  ");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Ten-step text progress bar.</summary>
        static string Bar(float f)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(f * 10f), 0, 10);
            return "[" + new string('|', n) + new string('.', 10 - n) + "]";
        }

        void BuildUi()
        {
            var host = new GameObject("DigTool HUD");
            host.transform.SetParent(transform, false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 30;
            var root = doc.rootVisualElement; DMI.Xenoarch.UI.XenoarchUI.FillRoot(root);
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            root.style.justifyContent = Justify.FlexEnd; root.style.alignItems = Align.Center;
            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            _panel.style.marginBottom = 150; _panel.style.paddingLeft = 14; _panel.style.paddingRight = 14; _panel.style.paddingTop = 6; _panel.style.paddingBottom = 6;
            _panel.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDark, 0.85f);
            _line1 = new Label { pickingMode = PickingMode.Ignore }; _line1.style.color = XenoarchPalette.Primary; _line1.style.fontSize = 17;
            _line2 = new Label { pickingMode = PickingMode.Ignore }; _line2.style.color = XenoarchPalette.Text; _line2.style.fontSize = 17;
            _panel.style.maxWidth = Length.Percent(72);   // long hints wrap instead of running under the side HUD
            _line2.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(_line1); _panel.Add(_line2);
            root.Add(_panel);
            XenoarchUI.Show(_panel, Equipped >= 0);
        }

        static AudioClip Noise(string name, float seconds, float lowpass)
        {
            int rate = 22050, n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                y += lowpass * ((float)rng.NextDouble() * 2f - 1f - y);
                data[i] = y * Mathf.Sin(Mathf.PI * t) * 0.8f;
            }
            var c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
