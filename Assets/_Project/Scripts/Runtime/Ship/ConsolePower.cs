using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Cockpit console power state (t88u). Added at runtime by <see cref="CockpitSeat"/> to the console it faces; nothing
    /// is saved into the scene and no material asset is touched: all dimming goes through MaterialPropertyBlocks on the
    /// console's own renderers. At start the displays (materials with emission) are off: emission 0, base colour darkened.
    /// <see cref="PowerOn"/>: button click, the displays blink rapidly and fade up (~1.2 s) with a power-on sound built
    /// from the same blink pattern, then a very faint hum loops. <see cref="PowerOff"/>: hum stops, displays fade out.
    /// </summary>
    public sealed class ConsolePower : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Whole power-on (blink + fade) duration (s).")] [Range(0.3f, 4f)] public float powerOnSeconds = 1.2f;
        [Tooltip("Share of the power-on spent blinking before the steady fade-up.")] [Range(0.1f, 0.95f)] public float blinkShare = 0.6f;
        [Tooltip("Blinks per second while powering on.")] [Range(4f, 30f)] public float blinkRate = 14f;
        [Tooltip("Power-down fade (s).")] [Range(0.05f, 2f)] public float powerOffSeconds = 0.45f;
        [Header("Off look")]
        [Tooltip("Emission multiplier while off (0 = dark).")] [Range(0f, 1f)] public float offEmission = 0f;
        [Tooltip("Display base-colour multiplier while off.")] [Range(0f, 1f)] public float offBase = 0.12f;
        [Header("Sound (volumes)")]
        [Range(0f, 1f)] public float clickVolume = 0.55f;
        [Range(0f, 1f)] public float powerOnVolume = 0.4f;
        [Tooltip("Seated hum: keep very faint.")] [Range(0f, 0.3f)] public float humVolume = 0.035f;
        [Range(0f, 1f)] public float powerOffVolume = 0.3f;
        [Tooltip("Optional overrides; empty = the ship sound set (Resources/XenoarchShipSfx), else procedural.")]
        public AudioClip clickClip, powerOnClip, humClip, powerOffClip;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        struct Slot { public Renderer r; public int index; public Color emission; public Color baseCol; public int baseId; public bool hasBase; }
        readonly List<Slot> _slots = new List<Slot>();
        MaterialPropertyBlock _mpb;
        AudioSource _sfx, _hum;
        Coroutine _routine;
        AudioClip _procOn; (float, float, float) _procOnKey;
        float _level = -1f;

        /// <summary>0 = off, 1 = fully on.</summary>
        public float Level => Mathf.Max(0f, _level);
        public bool IsOn { get; private set; }
        public int DisplayCount { get { EnsureInit(); return _slots.Count; } }
        bool _init;
        void EnsureInit() { if (_init) return; _init = true; Collect(); SetLevel(0f); }

        void Awake() { EnsureInit(); Debug.Log($"[Xenoarch] Console power: {name} starts off ({_slots.Count} display material slot(s))"); }
        void OnDestroy() { ClearBlocks(); }

        /// <summary>Displays = material slots with an emission colour above black (and emission enabled).</summary>
        void Collect()
        {
            _slots.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(EmissionId)) continue;
                    var e = m.GetColor(EmissionId);
                    bool emissive = e.maxColorComponent > 0.01f && (m.IsKeywordEnabled("_EMISSION") || m.globalIlluminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack);
                    if (!emissive) continue;
                    int bid = m.HasProperty(BaseId) ? BaseId : m.HasProperty(ColorId) ? ColorId : -1;
                    _slots.Add(new Slot { r = r, index = i, emission = e, hasBase = bid != -1, baseId = bid, baseCol = bid != -1 ? m.GetColor(bid) : Color.white });
                }
            }
        }

        void SetLevel(float k)
        {
            k = Mathf.Clamp01(k);
            if (Mathf.Approximately(k, _level)) return;
            _level = k;
            _mpb ??= new MaterialPropertyBlock();
            float em = Mathf.Lerp(offEmission, 1f, k), bc = Mathf.Lerp(offBase, 1f, k);
            foreach (var s in _slots)
            {
                if (s.r == null) continue;
                s.r.GetPropertyBlock(_mpb, s.index);
                _mpb.SetColor(EmissionId, s.emission * em);
                if (s.hasBase) { var c = s.baseCol * bc; c.a = s.baseCol.a; _mpb.SetColor(s.baseId, c); }
                s.r.SetPropertyBlock(_mpb, s.index);
            }
        }

        void ClearBlocks() { foreach (var s in _slots) if (s.r != null) s.r.SetPropertyBlock(null, s.index); }

        // ------------------------------------------------------------------ power
        /// <summary>Button click now (call at the hand's press), then blink on with the power-on sound, then hum.</summary>
        public void PowerOn()
        {
            EnsureInit();
            if (IsOn) return;
            IsOn = true;
            Play(Clip(clickClip, s => s.consoleClick, () => _procClick ??= ProceduralClick()), clickVolume);
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(PowerOnRoutine());
        }

        public void PowerOff()
        {
            if (!IsOn) return;
            IsOn = false;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(PowerOffRoutine());
        }

        /// <summary>Display brightness during power-on at time t (0..powerOnSeconds): rapid blinks that brighten, then a fade.</summary>
        public static float BlinkLevel(float t, float duration, float blinkShare, float blinkRate)
        {
            if (t <= 0f) return 0f;
            if (t >= duration) return 1f;
            float blinkEnd = duration * blinkShare;
            if (t < blinkEnd)
            {
                int step = Mathf.FloorToInt(t * blinkRate);
                float u = t / blinkEnd;
                // deterministic pseudo-random on/off, more often "on" as it warms up
                uint h = (uint)(step * 2654435761u) ^ 0x9E3779B9u; h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                // even steps flash on, odd steps stay dark early and fill in as it warms up -> a rapid, irregular flicker
                bool on = (step & 1) == 0 || (h % 1000u) / 1000f < 0.6f * u * u;
                return on ? 0.25f + 0.45f * u : 0.02f;
            }
            return Mathf.Lerp(0.7f, 1f, Mathf.SmoothStep(0f, 1f, (t - blinkEnd) / Mathf.Max(0.01f, duration - blinkEnd)));
        }

        IEnumerator PowerOnRoutine()
        {
            Play(Clip(powerOnClip, s => s.consolePowerOn, () => _procOn != null && _procOnKey == (powerOnSeconds, blinkShare, blinkRate) ? _procOn : (_procOn = ProceduralPowerOn((_procOnKey = (powerOnSeconds, blinkShare, blinkRate)).Item1, blinkShare, blinkRate))), powerOnVolume);
            for (float t = 0f; t < powerOnSeconds; t += Time.deltaTime) { SetLevel(BlinkLevel(t, powerOnSeconds, blinkShare, blinkRate)); yield return null; }
            SetLevel(1f);
            EnsureSources();
            _hum.clip = Clip(humClip, s => s.consoleHum, () => _procHum ??= ProceduralSoftHum());
            _hum.volume = 0f; _hum.loop = true; _hum.Play();
            for (float t = 0f; t < 0.6f; t += Time.deltaTime) { _hum.volume = humVolume * t / 0.6f; yield return null; }
            _hum.volume = humVolume;
            _routine = null;
        }

        IEnumerator PowerOffRoutine()
        {
            EnsureSources();
            Play(Clip(powerOffClip, s => s.consolePowerOff, () => _procOff ??= ProceduralPowerOff()), powerOffVolume);
            float start = Level, hv = _hum.volume;
            for (float t = 0f; t < powerOffSeconds; t += Time.deltaTime)
            {
                float k = t / powerOffSeconds;
                SetLevel(start * (1f - k)); _hum.volume = hv * (1f - k);
                yield return null;
            }
            SetLevel(0f); _hum.Stop();
            _routine = null;
        }

        // ------------------------------------------------------------------ audio
        void EnsureSources()
        {
            if (_sfx != null) return;
            _sfx = Make("ConsoleSfx"); _hum = Make("ConsoleHum");
            AudioSource Make(string n)
            {
                var go = new GameObject(n); go.transform.SetParent(transform, false);
                var b = Bounds(); go.transform.position = b.center;
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 0.6f; a.maxDistance = 7f; a.dopplerLevel = 0f;
                return a;
            }
        }

        Bounds Bounds()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        void Play(AudioClip c, float vol) { if (c == null || vol <= 0f) return; EnsureSources(); _sfx.PlayOneShot(c, vol); }

        static AudioClip Clip(AudioClip own, System.Func<ShipSfx, AudioClip> fromSet, System.Func<AudioClip> fallback)
        {
            if (own != null) return own;
            var set = ShipSfx.Get(); var c = set != null ? fromSet(set) : null;
            return c != null ? c : fallback();
        }

        static AudioClip _procClick, _procHum, _procOff;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _procClick = _procHum = _procOff = null; }

        const int Rate = 44100;

        /// <summary>Crisp switch click: two short transients.</summary>
        public static AudioClip ProceduralClick()
        {
            int n = (int)(Rate * 0.08f); var d = new float[n]; var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Exp(-t * 900f) + 0.5f * (t > 0.022f ? Mathf.Exp(-(t - 0.022f) * 1200f) : 0f);
                d[i] = (((float)rng.NextDouble() * 2f - 1f) * 0.6f + Mathf.Sin(2f * Mathf.PI * 3200f * t) * 0.4f) * a * 0.9f;
            }
            var c = AudioClip.Create("ConsoleClick", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Display power-on: electrical buzz gated by the same blink pattern as the screens, then a rising whine.</summary>
        public static AudioClip ProceduralPowerOn(float duration, float blinkShare, float blinkRate)
        {
            int n = (int)(Rate * (duration + 0.35f)); var d = new float[n]; var rng = new System.Random(11);
            float phase = 0f; float smooth = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float lvl = BlinkLevel(t, duration, blinkShare, blinkRate);
                smooth += (lvl - smooth) * 0.02f; // de-click the gate
                float f = 900f + 2600f * Mathf.Clamp01(t / duration);
                phase += 2f * Mathf.PI * f / Rate;
                float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 100f * t)) * 0.25f + ((float)rng.NextDouble() * 2f - 1f) * 0.2f;
                float whine = Mathf.Sin(phase) * 0.18f;
                float tail = t > duration ? Mathf.Exp(-(t - duration) * 10f) : 1f;
                d[i] = (buzz * (t < duration * blinkShare ? 1f : 0.35f) + whine) * smooth * tail * 0.8f;
            }
            var c = AudioClip.Create("ConsolePowerOn", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Very soft electronics hum loop (2 s, whole cycles, light hiss).</summary>
        public static AudioClip ProceduralSoftHum()
        {
            int n = Rate * 2; var d = new float[n]; var rng = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                d[i] = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.2f + Mathf.Sin(2f * Mathf.PI * 7500f * t) * 0.02f
                     + ((float)rng.NextDouble() * 2f - 1f) * 0.04f;
            }
            var c = AudioClip.Create("ConsoleHum", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Short falling power-down blip.</summary>
        public static AudioClip ProceduralPowerOff()
        {
            int n = (int)(Rate * 0.4f); var d = new float[n]; float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                ph += 2f * Mathf.PI * Mathf.Lerp(1800f, 120f, t / 0.4f) / Rate;
                d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * 7f) * 0.35f;
            }
            var c = AudioClip.Create("ConsolePowerOff", n, 1, Rate, false); c.SetData(d, 0); return c;
        }
    }
}
