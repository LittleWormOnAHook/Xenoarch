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

        [Header("Dig strength (tunable)")]
        [Tooltip("Removal multiplier for the crude tools (shovel, drill): >1 digs deeper per stroke.")] [Range(0.5f, 6f)] public float crudeStrength = 3f;
        [Tooltip("Most cells (0.25 m each) one crude stroke can cut down through per column.")] [Range(1, 6)] public int crudeCellsPerStroke = 3;
        [Tooltip("Removal multiplier for the fine tools (precision tool, brush).")] [Range(0.5f, 4f)] public float fineStrength = 1.5f;
        [Tooltip("Show the projected aim ring on the ground while a dig tool is out.")] public bool aimRing = true;

        /// <summary>The equipped tool with the tunable strength applied.</summary>
        public DigToolSpec Current
        {
            get
            {
                if (Equipped < 0) return default;
                var t = DigTools.All[Equipped];
                if (t.crude) { t.removal *= crudeStrength; t.cells = Mathf.Max(t.cells, crudeCellsPerStroke); }
                else { t.removal *= fineStrength; t.cells = Mathf.Max(t.cells, 1); }
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

        static bool Has(string itemId) => XenoarchRuntime.Session != null && XenoarchRuntime.Session.Inventory.Count(itemId) > 0;

        public void Equip(int index)
        {
            if (index < 0 || index >= DigTools.All.Length || index == Equipped) { Holster(); return; }
            if (!Has(DigTools.All[index].itemId)) { Toast.Show($"No {DigTools.All[index].name} in your inventory.", 2.5f); return; }
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

        void Update()
        {
            if (XenoarchRuntime.Session == null) return;
            var kb = Keyboard.current; var gp = Gamepad.current; var mouse = Mouse.current;
            if (!UIState.BlocksGameplay && kb != null)
            {
                if (kb.digit2Key.wasPressedThisFrame) Equip(Equipped == 0 ? -1 : 0);
                if (kb.digit3Key.wasPressedThisFrame) Equip(Equipped == 1 ? -1 : 1);
                if (kb.digit4Key.wasPressedThisFrame) Equip(Equipped == 2 ? -1 : 2);
                if (kb.digit5Key.wasPressedThisFrame) Equip(Equipped == 3 ? -1 : 3);
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
                    _audio.pitch = Random.Range(0.85f, 1.15f);
                    _audio.PlayOneShot(_scrape);
                    if (_arms != null && !_arms.Reaching) _arms.Reach(hit.point, null);
                    StrataFinds(site, r);
                    if (r.damageDealt > 0.001f) Flash($"Damaging the find! Condition {r.damaged.condition:P0}. Switch to the precision tool (3) or brush (4).");
                }
                else if (r.refused)
                {
                    refusedHere = true;
                    _audio.PlayOneShot(_clunk);
                    Flash(r.material == SoilMaterial.Bedrock ? "Bedrock: the site floor. Nothing below." : $"Too hard for the {tool.name.ToLowerInvariant()} ({r.layer.label}). {(r.layer.hardness > 2f ? "Use the drill." : "Try the precision tool or drill.")}");
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
            foreach (var (id, n) in Materials.StrataYield.Dig(s.Materials, profile, site.Definition.Id, site.Grid.cell, cuts, _strataRng))
            {
                int lost = s.Inventory.Add(id, n);
                string name = s.Materials.Name(id);
                UI.Toast.Show(lost >= n ? $"Backpack full: {name} left in the spoil" : $"+{n - lost} {name}");
            }
        }

        void Flash(string text) { _flash = text; _flashUntil = Time.time + 2.5f; }

        public static string Label(SoilMaterial m) => m switch { SoilMaterial.Dust => "surface dust", SoilMaterial.Loose => "loose regolith", SoilMaterial.Compacted => "compacted layer", SoilMaterial.Ice => "ice", SoilMaterial.Rock => "rock", _ => "bedrock" };

        void UpdateHud(ExcavationSite site, RaycastHit hit)
        {
            if (_line1 == null) return;
            string l1 = $"{Current.name}   [hold LMB to dig]   2 shovel  3 precision  4 brush  5 drill";
            string l2;
            if (site == null) l2 = "Aim at a dig site within 3 m.";
            else
            {
                var l = site.transform.InverseTransformPoint(hit.point);
                int x = Mathf.Clamp(Mathf.FloorToInt(l.x / site.Grid.cell), 0, site.Grid.nx - 1), z = Mathf.Clamp(Mathf.FloorToInt(l.z / site.Grid.cell), 0, site.Grid.nz - 1);
                int top = site.Grid.TopLayer(x, z);
                string band = top >= site.Grid.ny ? "bedrock" : site.Grid.LayerAt(top).label;
                l2 = $"{site.Definition.displayName}: {band}, depth {site.Grid.TopDepth(x, z):0.00} m";
                GridArtifact near = null; float best = 1.2f;
                foreach (var a in site.Grid.artifacts)
                {
                    if (a.recovered) continue;
                    float d = Vector3.Distance(site.CellCentre(a.x, a.y, a.z), hit.point);
                    if (d < best && site.Exposure(a) > 0.01f) { best = d; near = a; }
                }
                if (near != null) l2 += $"   |   find {site.Exposure(near):P0} exposed, condition {near.condition:P0}";
            }
            if (_flash != null && Time.time < _flashUntil) l2 = _flash;
            _line1.text = l1; _line2.text = l2;
        }

        void BuildUi()
        {
            var host = new GameObject("DigTool HUD");
            host.transform.SetParent(transform, false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 30;
            var root = doc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            root.style.justifyContent = Justify.FlexEnd; root.style.alignItems = Align.Center;
            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            _panel.style.marginBottom = 150; _panel.style.paddingLeft = 14; _panel.style.paddingRight = 14; _panel.style.paddingTop = 6; _panel.style.paddingBottom = 6;
            _panel.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 0.7f);
            _line1 = new Label { pickingMode = PickingMode.Ignore }; _line1.style.color = new Color(0.95f, 0.66f, 0.22f); _line1.style.fontSize = 17;
            _line2 = new Label { pickingMode = PickingMode.Ignore }; _line2.style.color = new Color(0.85f, 0.9f, 0.95f); _line2.style.fontSize = 17;
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
