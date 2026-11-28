using UnityEngine;

namespace DMI.Xenoarch.Weather
{
    /// <summary>
    /// Original, license-free weather sound: seamless filtered-noise loops (wind, rain, grit, helmet static) and a
    /// synthesised thunder roll, generated in code the first time they are needed. Used unless the catalog has
    /// hand-picked clips. Mono, 22.05 kHz, 8 s loops (about 350 KB each).
    /// </summary>
    public static class WeatherSynth
    {
        public const int Rate = 22050;
        const float LoopSeconds = 8f, FadeSeconds = 1.2f;

        public enum Kind { WindSoft, WindStrong, WindHowl, RainSoft, RainHeavy, Grit, Static }

        static readonly AudioClip[] _cache = new AudioClip[7];
        static AudioClip _thunder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { for (int i = 0; i < _cache.Length; i++) _cache[i] = null; _thunder = null; }

        public static AudioClip Get(Kind k)
        {
            int i = (int)k;
            if (_cache[i] != null) return _cache[i];
            int n = (int)(LoopSeconds * Rate), fade = (int)(FadeSeconds * Rate);
            var raw = new float[n + fade];
            var rng = new System.Random(1000 + i * 77);
            switch (k)
            {
                case Kind.WindSoft: Wind(raw, rng, 320f, 0.35f, 0.18f, false); break;
                case Kind.WindStrong: Wind(raw, rng, 800f, 0.5f, 0.3f, false); break;
                case Kind.WindHowl: Wind(raw, rng, 1500f, 0.65f, 0.4f, true); break;
                case Kind.RainSoft: Rain(raw, rng, 0.22f, 55f); break;
                case Kind.RainHeavy: Rain(raw, rng, 0.5f, 420f); break;
                case Kind.Grit: Grit(raw, rng); break;
                case Kind.Static: Static(raw, rng); break;
            }
            var data = Loopify(raw, n, fade);
            Normalize(data, k == Kind.Static ? 0.8f : 0.9f);
            var clip = AudioClip.Create("XA_Weather_" + k, n, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            _cache[i] = clip;
            return clip;
        }

        public static AudioClip Thunder()
        {
            if (_thunder != null) return _thunder;
            int n = Rate * 7;
            var d = new float[n];
            var rng = new System.Random(4242);
            float brown = 0f, lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float w = (float)(rng.NextDouble() * 2 - 1);
                brown = (brown + 0.02f * w) / 1.02f;
                float cutoff = Mathf.Lerp(0.18f, 0.015f, Mathf.Clamp01(t / 5f));      // rumble darkens as it rolls away
                lp += (brown * 12f - lp) * cutoff; lp2 += (lp - lp2) * cutoff;
                float crack = t < 0.35f ? w * Mathf.Exp(-t * 14f) * 0.9f : 0f;       // sharp opening crack
                float roll = 0.55f + 0.45f * Mathf.Sin(t * 7.1f) * Mathf.Sin(t * 2.3f + 1f); // rolling swell
                float env = Mathf.Clamp01(t / 0.08f) * Mathf.Exp(-t * 0.55f);
                d[i] = (lp2 * roll * 1.4f + crack) * env;
            }
            Normalize(d, 0.95f);
            _thunder = AudioClip.Create("XA_Weather_Thunder", n, 1, Rate, false);
            _thunder.SetData(d, 0); _thunder.hideFlags = HideFlags.DontSave;
            return _thunder;
        }

        // ---- generators

        static void Wind(float[] o, System.Random r, float center, float amDepth, float q, bool howl)
        {
            // pink-ish noise through a state-variable band-pass whose centre drifts, plus a low body.
            float b0 = 0, b1 = 0, b2 = 0, low = 0, band = 0, lp = 0;
            float ph1 = (float)r.NextDouble() * 6.28f, ph2 = (float)r.NextDouble() * 6.28f, ph3 = (float)r.NextDouble() * 6.28f;
            float fq = howl ? 0.045f : 0.12f; // damping: lower = more resonant
            for (int i = 0; i < o.Length; i++)
            {
                float t = i / (float)Rate;
                float w = (float)(r.NextDouble() * 2 - 1);
                b0 = 0.99765f * b0 + w * 0.0990460f; b1 = 0.96300f * b1 + w * 0.2965164f; b2 = 0.57000f * b2 + w * 1.0526913f;
                float pink = (b0 + b1 + b2 + w * 0.1848f) * 0.2f;
                float sweep = 1f + 0.35f * Mathf.Sin(t * 0.37f * 6.2832f + ph1) + 0.2f * Mathf.Sin(t * 0.91f * 6.2832f + ph2);
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(center * sweep, 80f, 3500f) / Rate);
                low += f * band; float high = pink - low - fq * band; band += f * high;
                lp += (pink - lp) * 0.02f;                       // sub body
                float am = 1f - amDepth + amDepth * (0.5f + 0.5f * Mathf.Sin(t * 0.21f * 6.2832f + ph3)) * (0.7f + 0.3f * Mathf.Sin(t * 0.77f * 6.2832f + ph1));
                float s = howl ? band * 0.9f + lp * 0.5f : band * (1f - q) * 0.8f + lp * (1.5f - q);
                o[i] = s * am;
            }
        }

        static void Rain(float[] o, System.Random r, float bed, float dropsPerSec)
        {
            float hp = 0, prev = 0, lp = 0, dropEnv = 0, dropFreq = 3000f, dropPh = 0f;
            float dropChance = dropsPerSec / Rate;
            for (int i = 0; i < o.Length; i++)
            {
                float w = (float)(r.NextDouble() * 2 - 1);
                hp = 0.82f * (hp + w - prev); prev = w;          // high-pass hiss
                lp += (hp - lp) * 0.55f;                         // soften the very top
                if (r.NextDouble() < dropChance) { dropEnv = 0.3f + (float)r.NextDouble() * 0.7f; dropFreq = 1800f + (float)r.NextDouble() * 4500f; }
                dropEnv *= 0.9965f; dropPh += dropFreq / Rate * 6.2832f;
                o[i] = lp * bed + Mathf.Sin(dropPh) * dropEnv * 0.5f * (0.5f + bed);
            }
        }

        static void Grit(float[] o, System.Random r)
        {
            float low = 0, band = 0, ph = (float)r.NextDouble() * 6.28f;
            for (int i = 0; i < o.Length; i++)
            {
                float t = i / (float)Rate;
                float w = (float)(r.NextDouble() * 2 - 1);
                float f = 2f * Mathf.Sin(Mathf.PI * (2600f + 500f * Mathf.Sin(t * 0.5f * 6.2832f + ph)) / Rate);
                low += f * band; float high = w - low - 0.35f * band; band += f * high;
                float am = 0.6f + 0.4f * Mathf.Sin(t * 1.3f * 6.2832f + ph) * Mathf.Sin(t * 0.37f * 6.2832f);
                o[i] = band * am;
            }
        }

        static void Static(float[] o, System.Random r)
        {
            // very quiet hiss bed plus sparse short ticks (regolith / micrometeorite hits on the suit).
            float lp = 0, env = 0, ph = 0, fr = 3000f;
            for (int i = 0; i < o.Length; i++)
            {
                float w = (float)(r.NextDouble() * 2 - 1);
                lp += (w - lp) * 0.08f;
                if (r.NextDouble() < 2.5f / Rate) { env = 0.4f + (float)r.NextDouble() * 0.6f; fr = 2200f + (float)r.NextDouble() * 3000f; }
                env *= 0.9925f; ph += fr / Rate * 6.2832f;
                o[i] = lp * 0.08f + Mathf.Sin(ph) * env * 0.9f;
            }
        }

        // ---- helpers

        static float[] Loopify(float[] raw, int n, int fade)
        {
            var d = new float[n];
            System.Array.Copy(raw, d, n);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;                      // head fades up, the extra tail fades down underneath it
                d[i] = d[i] * t + raw[n + i] * (1f - t);
            }
            return d;
        }

        static void Normalize(float[] d, float peak)
        {
            float mean = 0f; for (int i = 0; i < d.Length; i++) mean += d[i]; mean /= d.Length;
            float max = 1e-5f; for (int i = 0; i < d.Length; i++) { d[i] -= mean; max = Mathf.Max(max, Mathf.Abs(d[i])); }
            float g = peak / max; for (int i = 0; i < d.Length; i++) d[i] *= g;
        }
    }
}
