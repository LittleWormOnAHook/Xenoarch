using System.Collections;
using UnityEngine;

namespace DMI.Xenoarch.Weather
{
    /// <summary>
    /// Weather sound: layered wind (soft / strong / howl crossfaded by wind strength, as in ASH), rain (soft / heavy),
    /// blowing grit, helmet static for airless worlds and distance-delayed thunder. Wind volume, pitch and dullness
    /// come from the atmosphere density of the body, so thin air (Mars) is faint, low and muffled and a vacuum is
    /// silent. Uses hand-picked clips from the catalog when set, otherwise the generated loops (<see cref="WeatherSynth"/>).
    /// </summary>
    public sealed class WeatherAudio : MonoBehaviour
    {
        sealed class Loop
        {
            public AudioSource src; public AudioLowPassFilter lp; public WeatherSynth.Kind kind; public AudioClip custom; public bool ready;
        }

        const float Master = 0.55f;
        Loop _windSoft, _windStrong, _windHowl, _rainSoft, _rainHeavy, _grit, _static;
        AudioSource _thunderSrc; AudioLowPassFilter _thunderLp;
        WeatherCatalog _cat;
        float _muffle;   // 0 = open air, 1 = sealed cab
        int _genBudgetFrame = -1;

        public static WeatherAudio Create(Transform parent, WeatherCatalog cat)
        {
            var go = new GameObject("[Xenoarch] Weather Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<WeatherAudio>();
            a._cat = cat;
            a.Build();
            return a;
        }

        void Build()
        {
            _windSoft = Make("WindSoft", WeatherSynth.Kind.WindSoft, _cat != null ? _cat.windSoft : null);
            _windStrong = Make("WindStrong", WeatherSynth.Kind.WindStrong, _cat != null ? _cat.windStrong : null);
            _windHowl = Make("WindHowl", WeatherSynth.Kind.WindHowl, _cat != null ? _cat.windHowl : null);
            _rainSoft = Make("RainSoft", WeatherSynth.Kind.RainSoft, _cat != null ? _cat.rainSoft : null);
            _rainHeavy = Make("RainHeavy", WeatherSynth.Kind.RainHeavy, _cat != null ? _cat.rainHeavy : null);
            _grit = Make("Grit", WeatherSynth.Kind.Grit, null);
            _static = Make("HelmetStatic", WeatherSynth.Kind.Static, null);
            var tg = new GameObject("Thunder");
            tg.transform.SetParent(transform, false);
            _thunderSrc = tg.AddComponent<AudioSource>(); _thunderSrc.spatialBlend = 0f; _thunderSrc.playOnAwake = false; _thunderSrc.dopplerLevel = 0f;
            _thunderLp = tg.AddComponent<AudioLowPassFilter>();
        }

        Loop Make(string n, WeatherSynth.Kind k, AudioClip custom)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f; s.dopplerLevel = 0f; s.priority = 140;
            var lp = go.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 22000f;
            return new Loop { src = s, lp = lp, kind = k, custom = custom };
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>Sets the target mix. All volumes are linear 0-1 before the master; <paramref name="muffle"/> 0-1 for a sealed cab.</summary>
        public struct Mix
        {
            public bool active; public bool hasAir; public float density01; public float intensity;
            public float wind, gust01, rain, dust, ash, snow, motes;
            public float windGain, windPitch, windLowPassHz, rainGain, staticGain;
            public float muffle;
        }

        public void Tick(Mix m)
        {
            float dt = Time.unscaledDeltaTime;
            _muffle = Mathf.MoveTowards(_muffle, m.muffle, dt * 2f);
            float act = m.active ? 1f : 0f;
            float inten = Mathf.Clamp(m.intensity, 0f, 2f);
            float dens = m.hasAir ? Mathf.Lerp(0.18f, 1f, Mathf.Sqrt(m.density01)) : 0f;

            float wv = Mathf.Clamp01(m.wind * inten * (1f + 0.3f * m.gust01));
            float swell = Mathf.Lerp(1f, 0.55f + 1.1f * m.gust01, 0.6f);
            float wvol = m.windGain * dens * swell * act * Master;
            float pitch = m.windPitch * (0.94f + 0.14f * m.gust01);
            float cut = Mathf.Lerp(m.windLowPassHz, 900f, _muffle);
            float cabVol = Mathf.Lerp(1f, 0.55f, _muffle);
            Drive(_windSoft, wvol * (1f - Smooth(0.35f, 0.7f, wv)) * Mathf.Clamp01(0.25f + wv * 3f) * cabVol, pitch, cut);
            Drive(_windStrong, wvol * Smooth(0.25f, 0.6f, wv) * (1f - Smooth(0.75f, 1f, wv)) * cabVol, pitch, cut);
            Drive(_windHowl, wvol * Smooth(0.7f, 1f, wv) * cabVol, pitch, cut);

            float r = Mathf.Clamp01(m.rain * inten);
            float rdens = m.hasAir ? Mathf.Lerp(0.35f, 1f, Mathf.Sqrt(m.density01)) : 0f;
            float rvol = m.rainGain * rdens * act * Master * cabVol;
            float rcut = Mathf.Lerp(12000f, 1800f, _muffle);
            Drive(_rainSoft, rvol * (1f - Smooth(0.4f, 0.8f, r)) * Mathf.Clamp01(r * 4f), 1f, rcut);
            Drive(_rainHeavy, rvol * Smooth(0.4f, 0.8f, r), 1f, rcut);

            float gr = Mathf.Clamp01(Mathf.Max(m.dust, m.ash * 0.5f + m.snow * 0.25f) * inten);
            Drive(_grit, gr * Mathf.Clamp01(0.3f + wv) * dens * 0.5f * m.windGain * act * Master * cabVol, Mathf.Lerp(0.85f, 1.05f, wv), Mathf.Lerp(m.windLowPassHz + 2000f, 1100f, _muffle));

            float st = m.hasAir ? 0f : m.staticGain * (1f + Mathf.Clamp01(m.motes) * 1.5f);
            Drive(_static, st * 0.22f * act * Master * Mathf.Lerp(1f, 0.6f, _muffle), 1f, 12000f);
        }

        void Drive(Loop l, float vol, float pitch, float cutoff)
        {
            if (l == null) return;
            var s = l.src;
            if (vol > 0.004f && !l.ready)
            {
                // generate at most one loop per frame (each takes a few ms)
                if (Time.frameCount == _genBudgetFrame) return;
                _genBudgetFrame = Time.frameCount;
                s.clip = l.custom != null ? l.custom : WeatherSynth.Get(l.kind);
                l.ready = true;
            }
            if (!l.ready) return;
            float cur = s.volume;
            s.volume = Mathf.MoveTowards(cur, Mathf.Clamp01(vol), Time.unscaledDeltaTime * (vol < cur ? 0.9f : 0.35f));
            s.pitch = Mathf.Lerp(s.pitch, pitch, Time.unscaledDeltaTime * 2f);
            l.lp.cutoffFrequency = Mathf.Lerp(l.lp.cutoffFrequency, Mathf.Clamp(cutoff, 300f, 22000f), Time.unscaledDeltaTime * 3f);
            if (s.volume > 0.001f) { if (!s.isPlaying) { s.time = Random.value * Mathf.Max(0.1f, s.clip.length - 0.2f); s.Play(); } }
            else if (s.isPlaying) s.Pause();
        }

        /// <summary>Silences everything at once (scene change / menu).</summary>
        public void Silence()
        {
            foreach (var l in new[] { _windSoft, _windStrong, _windHowl, _rainSoft, _rainHeavy, _grit, _static })
                if (l != null) { l.src.volume = 0f; if (l.src.isPlaying) l.src.Pause(); }
        }

        /// <summary>Distance-delayed thunder (speed of sound ~343 m/s, capped), duller the farther it is.</summary>
        public void Thunder(float distance, float gain)
        {
            StartCoroutine(PlayThunder(distance, gain));
        }

        IEnumerator PlayThunder(float dist, float gain)
        {
            yield return new WaitForSeconds(Mathf.Min(6f, dist / 343f));
            AudioClip clip = null;
            if (_cat != null && _cat.thunder != null && _cat.thunder.Length > 0) clip = _cat.thunder[Random.Range(0, _cat.thunder.Length)];
            if (clip == null) clip = WeatherSynth.Thunder();
            float far = Mathf.InverseLerp(300f, 3000f, dist);
            _thunderLp.cutoffFrequency = Mathf.Lerp(Mathf.Lerp(16000f, 1500f, _muffle), 600f, far);
            _thunderSrc.pitch = Random.Range(0.9f, 1.05f);
            _thunderSrc.PlayOneShot(clip, Mathf.Clamp01(gain) * Mathf.Lerp(1f, 0.35f, far) * Mathf.Lerp(1f, 0.5f, _muffle) * 0.9f);
        }
    }
}
