using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship sound set (Resources/XenoarchShipSfx). Clips reference licensed project audio (Creepy Cat Sci-Fi Kit Vol 3,
    /// referenced only, never edited); any empty clip falls back to a procedural sound. Per-object fields override these.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Ship/Ship SFX", fileName = "XenoarchShipSfx")]
    public sealed class ShipSfx : ScriptableObject
    {
        public const string ResourcePath = "XenoarchShipSfx";
        [Header("Shuttle bay lift")]
        public AudioClip liftStart;
        public AudioClip liftLoop;
        public AudioClip liftStop;

        static ShipSfx _cached; static bool _looked;
        public static ShipSfx Get() { if (!_looked) { _looked = true; _cached = Resources.Load<ShipSfx>(ResourcePath); } return _cached; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { _cached = null; _looked = false; }

        // ------------------------------------------------------------------ procedural fallbacks
        const int Rate = 44100;

        /// <summary>Short metallic clunk: a low thump with a fast-decaying ring.</summary>
        public static AudioClip ProceduralClunk(string name, float pitch = 1f)
        {
            int n = (int)(Rate * 0.45f); var d = new float[n]; var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float thump = Mathf.Sin(2f * Mathf.PI * 62f * pitch * t) * Mathf.Exp(-t * 14f);
                float ring = Mathf.Sin(2f * Mathf.PI * 410f * pitch * t) * Mathf.Exp(-t * 22f) * 0.25f;
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f) * 0.35f;
                d[i] = (thump * 0.8f + ring + noise) * 0.9f;
            }
            var c = AudioClip.Create(name, n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Seamless 2 s motor hum loop (whole-cycle frequencies, light wobble).</summary>
        public static AudioClip ProceduralHum(string name)
        {
            int n = Rate * 2; var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float wob = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 3f * t);
                d[i] = (Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.25f * wob
                      + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.08f + Mathf.Sin(2f * Mathf.PI * 330f * t) * 0.04f) * 0.6f;
            }
            var c = AudioClip.Create(name, n, 1, Rate, false); c.SetData(d, 0); return c;
        }
    }
}
