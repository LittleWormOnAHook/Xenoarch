using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// UITK progress/timer bar at the bottom of the screen with an audible cue: optional looping hum (pitch rises with
    /// progress), a beep every second (twice a second in the last 3 s) and a chime when it completes. Used for the ship's
    /// landing/take-off descent and the FTL arrival countdown. All audio is generated at runtime.
    /// </summary>
    public sealed class CountdownHud : MonoBehaviour
    {
        public static CountdownHud Current { get; private set; }
        public static bool Running => Current != null && !Current._finished;

        string _title; float _seconds, _elapsed, _nextBeep; bool _auto, _finished, _hum; Action _onDone; string _doneText;
        Label _titleL, _timeL; VisualElement _fill, _panel; AudioSource _humSrc, _sfx;

        /// <summary>Show a bar you drive yourself with <see cref="Tick"/> and <see cref="Finish"/>.</summary>
        public static CountdownHud Show(string title, float seconds, bool hum = true)
        {
            if (Current != null) Current.Cancel();
            var go = new GameObject("[Countdown HUD]");
            var active = SceneManager.GetActiveScene();
            if (active.IsValid()) SceneManager.MoveGameObjectToScene(go, active);
            var h = go.AddComponent<CountdownHud>();
            h.Init(title, seconds, hum);
            Current = h;
            return h;
        }

        /// <summary>Show a bar that runs itself for <paramref name="seconds"/>, then chimes and calls <paramref name="onDone"/>.</summary>
        public static CountdownHud Run(string title, float seconds, Action onDone, string doneText = null, bool hum = true)
        {
            var h = Show(title, seconds, hum);
            h._auto = true; h._onDone = onDone; h._doneText = doneText;
            return h;
        }

        void Init(string title, float seconds, bool hum)
        {
            _title = title; _seconds = Mathf.Max(0.1f, seconds); _hum = hum; _nextBeep = 1f;
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 55;
            var root = doc.rootVisualElement;
            if (root != null)
            {
                root.pickingMode = PickingMode.Ignore;
                var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
                wrap.style.position = Position.Absolute; wrap.style.left = 0; wrap.style.right = 0; wrap.style.bottom = 120;
                wrap.style.alignItems = Align.Center;
                _panel = new VisualElement { pickingMode = PickingMode.Ignore };
                _panel.style.width = 460; _panel.style.paddingLeft = 14; _panel.style.paddingRight = 14; _panel.style.paddingTop = 8; _panel.style.paddingBottom = 10;
                _panel.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDark, 0.9f);
                SetBorder(_panel, XenoarchPalette.Frame);
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.justifyContent = Justify.SpaceBetween;
                _titleL = new Label(title); _titleL.style.color = XenoarchPalette.Primary; _titleL.style.fontSize = 16; _titleL.style.unityFontStyleAndWeight = FontStyle.Bold;
                _timeL = new Label(""); _timeL.style.color = XenoarchPalette.Text; _timeL.style.fontSize = 16;
                row.Add(_titleL); row.Add(_timeL); _panel.Add(row);
                var track = new VisualElement(); track.style.height = 10; track.style.marginTop = 6;
                track.style.backgroundColor = XenoarchPalette.PanelDeep;
                _fill = new VisualElement(); _fill.style.height = Length.Percent(100); _fill.style.width = Length.Percent(0);
                _fill.style.backgroundColor = XenoarchPalette.Highlight;
                track.Add(_fill); _panel.Add(track);
                wrap.Add(_panel); root.Add(wrap);
            }
            _sfx = gameObject.AddComponent<AudioSource>(); _sfx.spatialBlend = 0f; _sfx.playOnAwake = false; _sfx.volume = 0.5f;
            if (hum)
            {
                _humSrc = gameObject.AddComponent<AudioSource>();
                _humSrc.clip = HumClip(); _humSrc.loop = true; _humSrc.spatialBlend = 0f; _humSrc.volume = 0.22f; _humSrc.pitch = 0.8f;
                _humSrc.Play();
            }
            Tick(0f);
        }

        static void SetBorder(VisualElement e, Color c)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = 1;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = 4;
        }

        /// <summary>Set elapsed seconds (bar, time-remaining readout, beeps).</summary>
        public void Tick(float elapsed)
        {
            if (_finished) return;
            _elapsed = Mathf.Clamp(elapsed, 0f, _seconds);
            float k = _elapsed / _seconds, left = _seconds - _elapsed;
            if (_fill != null) _fill.style.width = Length.Percent(k * 100f);
            if (_timeL != null) _timeL.text = $"T-{left:0.0}s";
            if (_humSrc != null) _humSrc.pitch = 0.8f + 0.5f * k;
            if (_elapsed >= _nextBeep && left > 0.05f)
            {
                bool last = left <= 3f;
                _sfx.pitch = last ? 1.35f : 1f;
                _sfx.PlayOneShot(BeepClip(), last ? 0.55f : 0.35f);
                _nextBeep += last ? 0.5f : 1f;
            }
        }

        public void SetTitle(string t) { _title = t; if (_titleL != null) _titleL.text = t; }

        /// <summary>Fill the bar, play the chime, show <paramref name="doneText"/> briefly, then remove itself.</summary>
        public void Finish(string doneText = null, bool chime = true)
        {
            if (_finished) return;
            Tick(_seconds);
            _finished = true;
            if (_titleL != null && !string.IsNullOrEmpty(doneText)) _titleL.text = doneText;
            if (_timeL != null) _timeL.text = "";
            if (_fill != null) _fill.style.backgroundColor = XenoarchPalette.Primary;
            if (_humSrc != null) _humSrc.Stop();
            if (chime) { _sfx.pitch = 1f; _sfx.PlayOneShot(ChimeClip(), 0.7f); }
            if (Current == this) Current = null;
            Destroy(gameObject, 1.6f);
        }

        public void Cancel()
        {
            _finished = true;
            if (Current == this) Current = null;
            Destroy(gameObject);
        }

        void Update()
        {
            if (!_auto || _finished) return;
            Tick(_elapsed + Time.deltaTime);
            if (_elapsed >= _seconds)
            {
                var cb = _onDone; _onDone = null;
                Finish(_doneText);
                try { cb?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // ---------- generated audio ----------
        const int Rate = 22050;
        static AudioClip _humClip, _beep, _chime;

        static AudioClip HumClip()
        {
            if (_humClip != null) return _humClip;
            int n = Rate * 2; var d = new float[n]; var rng = new System.Random(3); float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.02f;
                // 60/120/180 Hz all complete whole cycles in 2 s, so the loop is seamless
                d[i] = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.2f + Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.08f + lp * 0.6f;
            }
            _humClip = AudioClip.Create("Countdown Hum", n, 1, Rate, false); _humClip.SetData(d, 0);
            return _humClip;
        }

        static AudioClip BeepClip()
        {
            if (_beep != null) return _beep;
            int n = (int)(Rate * 0.09f); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, env = Mathf.Min(1f, t / 0.005f) * Mathf.Clamp01((0.09f - t) / 0.03f);
                d[i] = Mathf.Sin(2f * Mathf.PI * 1250f * t) * env * 0.6f;
            }
            _beep = AudioClip.Create("Countdown Beep", n, 1, Rate, false); _beep.SetData(d, 0);
            return _beep;
        }

        static AudioClip ChimeClip()
        {
            if (_chime != null) return _chime;
            int n = (int)(Rate * 1.1f); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 4f);
                float t2 = Mathf.Max(0f, t - 0.16f);
                float b = t >= 0.16f ? Mathf.Sin(2f * Mathf.PI * 1318.5f * t2) * Mathf.Exp(-t2 * 3.5f) : 0f;
                d[i] = (a + b) * 0.45f * Mathf.Min(1f, t / 0.004f);
            }
            _chime = AudioClip.Create("Countdown Chime", n, 1, Rate, false); _chime.SetData(d, 0);
            return _chime;
        }
    }
}
