using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>Plays the sonic tool sounds: one-shots plus a fading beam / fan loop. Uses the Pro Sound clips when present,
    /// else the committed fallback, else a generated hum. Added by <see cref="DigTool"/>.</summary>
    public sealed class SonicAudio : MonoBehaviour
    {
        AudioSource _loop, _shot;
        SonicToolAudioCatalog _cat;
        AudioClip _humBeam, _humFan;
        bool _on; SonicSfx _id; float _pitch = 1f, _target, _loopVol = 0.45f;

        void Awake()
        {
            _cat = Resources.Load<SonicToolAudioCatalog>(SonicToolAudioCatalog.Resource);
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false; _loop.loop = true; _loop.spatialBlend = 0f; _loop.volume = 0f;
            _shot = gameObject.AddComponent<AudioSource>();
            _shot.playOnAwake = false; _shot.spatialBlend = 0f;
        }

        public void Shot(SonicSfx id)
        {
            float v = 0.6f;
            AudioClip c = _cat != null ? _cat.Pick(id, out v) : null;
            if (c == null) return;
            _shot.pitch = 1f;
            _shot.PlayOneShot(c, v * 0.7f);
        }

        /// <summary>Beam / fan loop on or off; pitch follows Power.</summary>
        public void SetLoop(bool on, SonicSfx id, float pitch)
        {
            if (on && (!_on || _id != id)) StartLoop(id);
            _on = on; _id = id; _pitch = pitch;
            _target = on ? _loopVol : 0f;
        }

        public void StopLoops() { _on = false; _target = 0f; }

        void StartLoop(SonicSfx id)
        {
            float v = 0.6f;
            AudioClip c = _cat != null ? _cat.Pick(id, out v) : null;
            if (c != null) _loopVol = Mathf.Clamp01(v) * 0.6f;
            else { c = Hum(id == SonicSfx.FanLoop); _loopVol = 0.2f; }
            if (_loop.clip != c) { _loop.clip = c; _loop.Stop(); }
            if (!_loop.isPlaying) _loop.Play();
        }

        void Update()
        {
            if (_loop == null) return;
            _loop.volume = Mathf.MoveTowards(_loop.volume, _target, Time.unscaledDeltaTime * 5f);
            if (_on) _loop.pitch = Mathf.Lerp(_loop.pitch, _pitch, Time.unscaledDeltaTime * 8f);
            if (!_on && _loop.isPlaying && _loop.volume <= 0.001f) _loop.Pause();
        }

        AudioClip Hum(bool fan)
        {
            if (fan && _humFan != null) return _humFan;
            if (!fan && _humBeam != null) return _humBeam;
            const int rate = 22050;
            var data = new float[rate];                       // exactly 1 s, integer cycle counts: loops without a click
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate, tau = 2f * Mathf.PI;
                float s = fan
                    ? 0.35f * Mathf.Sin(tau * 880f * t) + 0.25f * Mathf.Sin(tau * 1320f * t) + 0.15f * Mathf.Sin(tau * 1760f * t)
                    : 0.45f * Mathf.Sin(tau * 110f * t) + 0.3f * Mathf.Sin(tau * 165f * t) + 0.2f * Mathf.Sin(tau * 220f * t);
                float am = fan ? 0.65f + 0.35f * Mathf.Sin(tau * 12f * t) : 0.85f + 0.15f * Mathf.Sin(tau * 4f * t);
                data[i] = s * am * 0.6f;
            }
            var clip = AudioClip.Create(fan ? "sonic_fan_hum" : "sonic_beam_hum", rate, 1, rate, false);
            clip.SetData(data, 0);
            if (fan) _humFan = clip; else _humBeam = clip;
            return clip;
        }
    }
}
