using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Weather
{
    /// <summary>Broad climate class of a body; drives the default weather mix (see <see cref="WeatherDefaults"/>).</summary>
    public enum WeatherArchetype { Airless, ThinDusty, Desert, Volcanic, Ice, Temperate, None }

    /// <summary>
    /// One weather state as TARGET values. Ported from ASH's ASHWeatherProfile (rain/snow/ashfall/wind/darkness/thunder
    /// per state, blended by the director) and extended with blowing dust, dust devils and micrometeorite motes for
    /// worlds with thin or no air. Everything is 0-1 unless noted.
    /// </summary>
    [Serializable]
    public sealed class WeatherPreset
    {
        public string name = "Clear";
        [Tooltip("Relative chance of being picked by the scheduler.")] [Min(0f)] public float weight = 1f;
        [Tooltip("A storm-class event: the scheduler alternates calm states and major events.")] public bool major;
        [Tooltip("Minutes this state lasts (min, max) before the scheduler moves on.")] public Vector2 minutes = new Vector2(3f, 8f);

        [Header("Precipitation")]
        [Range(0f, 1f)] public float rain;
        [Range(0f, 1f)] public float snow;
        [Range(0f, 1f)] public float ashfall;
        [Tooltip("Blowing dust / sand / ice dust (horizontal streaks and grit).")] [Range(0f, 1f)] public float dust;
        [Tooltip("Dust devils (swirling columns wandering across the ground).")] [Range(0f, 1f)] public float devils;
        [Tooltip("Airless bodies: micrometeorite / regolith motes (tiny ballistic sparkles) and helmet static.")] [Range(0f, 1f)] public float motes;

        [Header("Air")]
        [Range(0f, 1f)] public float wind = 0.2f;
        [Tooltip("Extra scene fog (exp-squared density, in units of 0.0015). 0 = scene as tuned.")] [Range(0f, 12f)] public float fog;
        [Tooltip("0 = sun as tuned, 1 = sun almost gone.")] [Range(0f, 1f)] public float darkness;
        [Tooltip("Lightning strikes per minute.")] [Range(0f, 12f)] public float thunderPerMinute;
        [Tooltip("Flake / grain size multiplier.")] [Range(0.3f, 3f)] public float particleSize = 1f;

        public WeatherPreset Clone() => (WeatherPreset)MemberwiseClone();

        public WeatherState ToState() => new WeatherState
        {
            rain = rain, snow = snow, ashfall = ashfall, dust = dust, devils = devils, motes = motes, wind = wind,
            fog = fog, darkness = darkness, thunderPerMinute = thunderPerMinute, particleSize = particleSize,
        };
    }

    /// <summary>Blendable weather numbers (what the outputs read each frame).</summary>
    [Serializable]
    public struct WeatherState
    {
        public float rain, snow, ashfall, dust, devils, motes, wind, fog, darkness, thunderPerMinute, particleSize;

        public static WeatherState Lerp(WeatherState a, WeatherState b, float t) => new WeatherState
        {
            rain = Mathf.Lerp(a.rain, b.rain, t), snow = Mathf.Lerp(a.snow, b.snow, t), ashfall = Mathf.Lerp(a.ashfall, b.ashfall, t),
            dust = Mathf.Lerp(a.dust, b.dust, t), devils = Mathf.Lerp(a.devils, b.devils, t), motes = Mathf.Lerp(a.motes, b.motes, t),
            wind = Mathf.Lerp(a.wind, b.wind, t), fog = Mathf.Lerp(a.fog, b.fog, t), darkness = Mathf.Lerp(a.darkness, b.darkness, t),
            thunderPerMinute = Mathf.Lerp(a.thunderPerMinute, b.thunderPerMinute, t), particleSize = Mathf.Lerp(a.particleSize, b.particleSize, t),
        };
    }

    /// <summary>
    /// Weather of one body: atmosphere, temperature, wind and sound character, particle colours and the set of
    /// weather states the director cycles through. One asset per landable planet/moon (Assets/_Project/Data/Weather);
    /// all fields have wide ranges so they can be pushed to extremes in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Weather/Weather Profile", fileName = "Weather_")]
    public sealed class WeatherProfile : ScriptableObject
    {
        public string bodyId;
        public string displayName;
        public WeatherArchetype archetype = WeatherArchetype.Airless;
        [TextArea(2, 4)] public string notes;

        [Header("Atmosphere (drives wind sound, drag and visibility)")]
        [Tooltip("Surface pressure in kPa (Earth 101, Mars 0.6, vacuum 0). Wind volume and pitch scale with the density.")] [Range(0f, 400f)] public float pressureKPa = 0f;
        [Range(-273f, 600f)] public float temperatureC = -20f;

        [Header("Wind")]
        [Tooltip("Wind speed at wind = 1 (m/s): drives particle drift and dust devils.")] [Range(0f, 80f)] public float windSpeedMax = 12f;
        [Range(0f, 1f)] public float gustiness = 0.35f;
        [Tooltip("Gusts per second-ish (higher = more frequent).")] [Range(0.02f, 3f)] public float gustFrequency = 0.3f;
        [Tooltip("Multiplier on the wind sound volume on top of the density curve.")] [Range(0f, 3f)] public float windVolume = 1f;
        [Tooltip("Pitch offset multiplier of the wind sound (1 = from the density curve).")] [Range(0.4f, 1.6f)] public float windPitch = 1f;
        [Tooltip("Low-pass of the wind sound (Hz): thin air sounds dull.")] [Range(300f, 22000f)] public float windLowPassHz = 6000f;
        [Range(0f, 3f)] public float rainVolume = 1f;

        [Header("Airless bodies")]
        [Tooltip("Faint helmet static / micrometeorite ticks (0 = silent).")] [Range(0f, 1f)] public float staticVolume = 0f;
        [Tooltip("Always-on baseline of regolith motes (tiny sparkles) even in a clear state.")] [Range(0f, 1f)] public float baselineMotes = 0f;

        [Header("Scheduler")]
        [Range(1f, 600f)] public float transitionSeconds = 45f;
        [Tooltip("Minutes of calm states between storm-class events.")] public Vector2 calmBetweenMajorMinutes = new Vector2(4f, 10f);
        [Tooltip("Chance (0-1) that a calm period is followed by a storm-class event at all (0 = never, 1 = always).")] [Range(0f, 1f)] public float majorChance = 0.6f;
        public List<WeatherPreset> presets = new List<WeatherPreset>();

        [Header("Look")]
        public Color dustColor = new Color(0.78f, 0.55f, 0.36f, 0.5f);
        public Color ashColor = new Color(0.34f, 0.32f, 0.31f, 0.85f);
        public Color snowColor = new Color(0.95f, 0.97f, 1f, 0.9f);
        public Color rainColor = new Color(0.72f, 0.78f, 0.86f, 0.5f);
        [Tooltip("Fog tint while the weather adds fog.")] public Color fogTint = new Color(0.62f, 0.56f, 0.5f);
        [Tooltip("Share of glowing embers among the ash flakes (volcanic).")] [Range(0f, 0.2f)] public float emberShare = 0f;

        public float Density01 => Mathf.Clamp01(pressureKPa / 101f);
        public bool HasAir => pressureKPa >= 0.05f && archetype != WeatherArchetype.Airless && archetype != WeatherArchetype.None;

        public WeatherPreset Find(string presetName)
        {
            if (presets == null || string.IsNullOrEmpty(presetName)) return null;
            foreach (var p in presets) if (p != null && string.Equals(p.name, presetName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>The strongest storm-class state (testing / extreme preset).</summary>
        public WeatherPreset Strongest()
        {
            WeatherPreset best = null; float bestScore = -1f;
            if (presets != null)
                foreach (var p in presets)
                {
                    if (p == null) continue;
                    float s = p.rain + p.snow + p.ashfall + p.dust + p.motes + p.wind * 0.5f + p.thunderPerMinute * 0.1f + (p.major ? 0.5f : 0f);
                    if (s > bestScore) { bestScore = s; best = p; }
                }
            return best;
        }
    }
}
