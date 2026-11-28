using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Weather
{
    /// <summary>
    /// Resources/Weather/WeatherCatalog: which <see cref="WeatherProfile"/> belongs to which body, the shared particle
    /// materials / thunder clips, and the global test controls (intensity multiplier, extreme preset). If the asset or a
    /// profile is missing, <see cref="WeatherDefaults"/> builds one in memory so weather still works.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Weather/Weather Catalog", fileName = "WeatherCatalog")]
    public sealed class WeatherCatalog : ScriptableObject
    {
        public const string ResourcePath = "Weather/WeatherCatalog";

        [Serializable] public sealed class Entry { public string bodyId; public WeatherProfile profile; }

        [Header("Global switches (also changeable at runtime, see WeatherSettings)")]
        public bool weatherEnabled = true;
        [Tooltip("Multiplies precipitation, wind and weather sound everywhere (1 = as authored, 0 = off, 3 = very heavy).")]
        [Range(0f, 3f)] public float intensityMultiplier = 1f;
        [Tooltip("Extreme test preset: every world sits in its strongest storm at full intensity.")] public bool extremePreset;
        [Tooltip("Particle count multiplier (performance).")] [Range(0.1f, 3f)] public float particleBudgetScale = 1f;

        [Header("Profiles")]
        public List<Entry> bodies = new List<Entry>();
        [Tooltip("Used for a body with no entry: matched by archetype.")] public List<WeatherProfile> archetypeDefaults = new List<WeatherProfile>();

        [Header("Shared assets (created by Xenoarch > Weather > Create Weather Assets)")]
        public Material alphaMaterial;
        public Material additiveMaterial;
        public Material streakMaterial;
        [Tooltip("CC0 thunder (from ASH). Empty = synthesised.")] public AudioClip[] thunder;
        [Tooltip("Optional hand-picked loops. Empty = the generated wind / rain loops are used.")]
        public AudioClip windSoft, windStrong, windHowl, rainSoft, rainHeavy;

        static WeatherCatalog _loaded;
        public static WeatherCatalog Load()
        {
            if (_loaded == null) _loaded = Resources.Load<WeatherCatalog>(ResourcePath);
            return _loaded;
        }
        public static void Invalidate() { _loaded = null; }

        public WeatherProfile Find(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId)) return null;
            foreach (var e in bodies) if (e != null && e.profile != null && e.bodyId == bodyId) return e.profile;
            return null;
        }

        public WeatherProfile ForArchetype(WeatherArchetype a)
        {
            foreach (var p in archetypeDefaults) if (p != null && p.archetype == a) return p;
            return null;
        }
    }

    /// <summary>Global weather controls for testing. Initialised from the catalog; changes made at runtime persist (PlayerPrefs).</summary>
    public static class WeatherSettings
    {
        const string KEnabled = "xenoarch.weather.enabled", KIntensity = "xenoarch.weather.intensity", KExtreme = "xenoarch.weather.extreme";
        static bool _init;
        static bool _enabled = true, _extreme;
        static float _intensity = 1f;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _init = false; Changed = null; }

        static void Init()
        {
            if (_init) return;
            _init = true;
            var cat = WeatherCatalog.Load();
            if (cat != null) { _enabled = cat.weatherEnabled; _intensity = cat.intensityMultiplier; _extreme = cat.extremePreset; }
            if (PlayerPrefs.HasKey(KEnabled)) _enabled = PlayerPrefs.GetInt(KEnabled) != 0;
            if (PlayerPrefs.HasKey(KIntensity)) _intensity = PlayerPrefs.GetFloat(KIntensity);
            if (PlayerPrefs.HasKey(KExtreme)) _extreme = PlayerPrefs.GetInt(KExtreme) != 0;
        }

        public static bool Enabled { get { Init(); return _enabled; } set { Init(); _enabled = value; PlayerPrefs.SetInt(KEnabled, value ? 1 : 0); Changed?.Invoke(); } }
        public static float Intensity { get { Init(); return _intensity; } set { Init(); _intensity = Mathf.Clamp(value, 0f, 3f); PlayerPrefs.SetFloat(KIntensity, _intensity); Changed?.Invoke(); } }
        public static bool Extreme { get { Init(); return _extreme; } set { Init(); _extreme = value; PlayerPrefs.SetInt(KExtreme, value ? 1 : 0); Changed?.Invoke(); } }
        public static float ParticleScale { get { var c = WeatherCatalog.Load(); return c != null ? c.particleBudgetScale : 1f; } }
        /// <summary>Back to the catalog defaults (forgets the saved test values).</summary>
        public static void ResetToDefaults() { PlayerPrefs.DeleteKey(KEnabled); PlayerPrefs.DeleteKey(KIntensity); PlayerPrefs.DeleteKey(KExtreme); _init = false; Init(); Changed?.Invoke(); }
    }
}
