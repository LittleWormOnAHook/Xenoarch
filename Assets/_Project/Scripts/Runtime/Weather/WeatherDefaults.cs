using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Weather
{
    /// <summary>
    /// Authoring defaults: the weather table for every landable body (Moon, Mars and the 22 generated planets) and the
    /// state sets per climate. The Editor command "Xenoarch > Weather > Create Weather Assets" writes these into
    /// WeatherProfile assets; the runtime also uses them directly when an asset is missing. The atmosphere numbers here
    /// are the weather layer's own (a planet's EnvironmentProfile text is not changed).
    /// </summary>
    public static class WeatherDefaults
    {
        public sealed class Row
        {
            public string bodyId, name; public WeatherArchetype type; public float kPa, tempC, windMax; public string note;
        }

        const string MoonId = "body.sol.moon", MarsId = "body.sol.mars";

        /// <summary>Hand-authored table (planet type comes from the generator's recipe type).</summary>
        public static readonly Row[] Table =
        {
            new Row { bodyId = "body.sol.moon", name = "Moon", type = WeatherArchetype.Airless, kPa = 0f, tempC = -20f, windMax = 0f, note = "Vacuum. No wind or precipitation; faint helmet static and rare micrometeorite motes." },
            new Row { bodyId = "body.sol.mars", name = "Mars", type = WeatherArchetype.ThinDusty, kPa = 0.6f, tempC = -60f, windMax = 24f, note = "Thin CO2 air: dull, quiet wind, dust devils, dust storms, rare CO2 frost flurries." },
            new Row { bodyId = "body.alpha_centauri.p0", name = "Alpha Centauri b", type = WeatherArchetype.Airless, note = "Barren cratered basin, airless." },
            new Row { bodyId = "body.alpha_centauri.p1", name = "Alpha Centauri c", type = WeatherArchetype.Temperate, kPa = 82f, tempC = 11f, windMax = 20f, note = "Temperate rocky world: rain, fog, thunderstorms." },
            new Row { bodyId = "body.barnards_star.p0", name = "Barnard d", type = WeatherArchetype.Airless, note = "Barren flats, airless." },
            new Row { bodyId = "body.barnards_star.p1", name = "Barnard b", type = WeatherArchetype.Volcanic, kPa = 18f, tempC = 210f, windMax = 22f, note = "Volcanic: ash fall, sulphur haze, dirty lightning." },
            new Row { bodyId = "body.barnards_star.p2", name = "Barnard c", type = WeatherArchetype.Volcanic, kPa = 11f, tempC = 165f, windMax = 17f, note = "Volcanic: thin ash fall and gritty gusts." },
            new Row { bodyId = "body.barnards_star.p3", name = "Barnard e", type = WeatherArchetype.Airless, note = "Barren foothills, airless." },
            new Row { bodyId = "body.sirius.p0", name = "Sirius b", type = WeatherArchetype.Airless, note = "Barren highlands, airless." },
            new Row { bodyId = "body.epsilon_eridani.p1", name = "Epsilon Eridani c", type = WeatherArchetype.Ice, kPa = 24f, tempC = -168f, windMax = 30f, note = "Ice world: frost snow, blizzards, spindrift." },
            new Row { bodyId = "body.tau_ceti.p0", name = "Tau Ceti b", type = WeatherArchetype.Volcanic, kPa = 26f, tempC = 240f, windMax = 28f, note = "Volcanic escarpments: heavy ash storms with lightning." },
            new Row { bodyId = "body.tau_ceti.p1", name = "Tau Ceti c", type = WeatherArchetype.Desert, kPa = 22f, tempC = 38f, windMax = 30f, note = "Desert dune seas: dust storms and dust devils." },
            new Row { bodyId = "body.tau_ceti.p2", name = "Tau Ceti d", type = WeatherArchetype.Desert, kPa = 14f, tempC = 24f, windMax = 26f, note = "Desert mesas: thin dust, devils, storms." },
            new Row { bodyId = "body.tau_ceti.p3", name = "Tau Ceti e", type = WeatherArchetype.Airless, note = "Barren plateau, airless." },
            new Row { bodyId = "body.proxima_centauri.p0", name = "Proxima d", type = WeatherArchetype.Volcanic, kPa = 14f, tempC = 190f, windMax = 24f, note = "Volcanic mountains: ash fall and sulphur gusts." },
            new Row { bodyId = "body.proxima_centauri.p1", name = "Proxima b", type = WeatherArchetype.Airless, note = "Barren cratered highland, airless." },
            new Row { bodyId = "body.proxima_centauri.p2", name = "Proxima c", type = WeatherArchetype.Airless, note = "Barren terraces, airless." },
            new Row { bodyId = "body.g93ce5fd0.p0", name = "XS-9104 b", type = WeatherArchetype.Temperate, kPa = 64f, tempC = 3f, windMax = 24f, note = "Cold rocky world: cold rain, sleet and snow, fog, storms." },
            new Row { bodyId = "body.g93ce5fd0.p1", name = "XS-9104 c", type = WeatherArchetype.Ice, kPa = 38f, tempC = -152f, windMax = 36f, note = "Ice world (fjords): snow, strong blizzards." },
            new Row { bodyId = "body.g93ce5fd0.p2", name = "XS-9104 d", type = WeatherArchetype.Ice, kPa = 17f, tempC = -181f, windMax = 26f, note = "Ice world (rolling ice): soft snow, light wind." },
            new Row { bodyId = "body.g94ce6163.p0", name = "XS-3787 b", type = WeatherArchetype.Desert, kPa = 9f, tempC = 61f, windMax = 34f, note = "Hot desert basins: thin dust storms, devils." },
            new Row { bodyId = "body.g94ce6163.p1", name = "XS-3787 c", type = WeatherArchetype.Ice, kPa = 29f, tempC = -140f, windMax = 40f, note = "Glacial world: crevasse winds, blizzards." },
            new Row { bodyId = "body.g95ce62f6.p0", name = "XS-6970 b", type = WeatherArchetype.Desert, kPa = 31f, tempC = 47f, windMax = 32f, note = "Open desert hills: dust haze and storms." },
            new Row { bodyId = "body.g95ce62f6.p1", name = "XS-6970 c", type = WeatherArchetype.Temperate, kPa = 71f, tempC = -4f, windMax = 22f, note = "Frozen temperate world: snow and sleet instead of rain." },
        };

        public static Row RowFor(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId)) return null;
            foreach (var r in Table) if (r.bodyId == bodyId) return r;
            return null;
        }

        /// <summary>Stable 0-1 value per body and salt (so every world differs a little, the same way every session).</summary>
        public static float Rand01(string id, int salt)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in id ?? "") h = (h ^ c) * 16777619;
                h = (h ^ (uint)salt) * 16777619; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
        static float Var(string id, int salt, float lo = 0.85f, float hi = 1.15f) => Mathf.Lerp(lo, hi, Rand01(id, salt));

        /// <summary>The archetype for a generated body type name ("Barren" etc.).</summary>
        public static WeatherArchetype ArchetypeForPlanetType(string typeName)
        {
            switch (typeName)
            {
                case "Barren": return WeatherArchetype.Airless;
                case "Volcanic": return WeatherArchetype.Volcanic;
                case "Desert": return WeatherArchetype.Desert;
                case "Ice": return WeatherArchetype.Ice;
                case "Rocky": return WeatherArchetype.Temperate;
                case "GasGiant": return WeatherArchetype.None;
                default: return WeatherArchetype.Airless;
            }
        }

        /// <summary>Builds (not saved) the profile for a row; used by the asset creator and as the runtime fallback.</summary>
        public static WeatherProfile Build(Row r)
        {
            var p = ScriptableObject.CreateInstance<WeatherProfile>();
            p.name = "Weather_" + r.bodyId.Replace("body.", "").Replace('.', '_');
            Fill(p, r.bodyId, r.name, r.type, r.kPa, r.tempC, r.windMax);
            p.notes = r.note;
            return p;
        }

        /// <summary>Fallback for a body with no table row (archetype defaults, mid values).</summary>
        public static WeatherProfile BuildGeneric(string bodyId, string displayName, WeatherArchetype type, float gravityMS2)
        {
            var p = ScriptableObject.CreateInstance<WeatherProfile>();
            p.name = "Weather_" + (bodyId ?? "generic").Replace("body.", "").Replace('.', '_');
            float kPa = 0f, tc = -20f, wm = 15f;
            switch (type)
            {
                case WeatherArchetype.ThinDusty: kPa = 0.6f; tc = -60f; wm = 24f; break;
                case WeatherArchetype.Desert: kPa = 20f; tc = 40f; wm = 28f; break;
                case WeatherArchetype.Volcanic: kPa = 16f; tc = 200f; wm = 22f; break;
                case WeatherArchetype.Ice: kPa = 25f; tc = -160f; wm = 32f; break;
                case WeatherArchetype.Temperate: kPa = 70f; tc = 8f; wm = 20f; break;
            }
            Fill(p, bodyId ?? "generic", displayName ?? "Generic", type, kPa, tc, wm);
            return p;
        }

        static void Fill(WeatherProfile p, string id, string name, WeatherArchetype type, float kPa, float tempC, float windMax)
        {
            p.bodyId = id; p.displayName = name; p.archetype = type; p.pressureKPa = kPa; p.temperatureC = tempC;
            p.windSpeedMax = windMax * Var(id, 1);
            p.gustiness = Mathf.Clamp01(0.3f * Var(id, 2, 0.7f, 1.4f));
            p.gustFrequency = 0.3f * Var(id, 3, 0.7f, 1.3f);
            float d = p.Density01;
            // Thin air: quiet, low and dull. Dense air: full range.
            p.windVolume = 1f;
            p.windPitch = Mathf.Lerp(0.78f, 1.02f, Mathf.Sqrt(d)) * Var(id, 4, 0.96f, 1.04f);
            p.windLowPassHz = Mathf.Lerp(750f, 9000f, Mathf.Sqrt(d));
            p.rainVolume = 1f;
            p.transitionSeconds = 40f;
            p.presets = new List<WeatherPreset>();
            switch (type)
            {
                case WeatherArchetype.Airless: Airless(p); break;
                case WeatherArchetype.ThinDusty: Mars(p); break;
                case WeatherArchetype.Desert: Desert(p); break;
                case WeatherArchetype.Volcanic: Volcanic(p); break;
                case WeatherArchetype.Ice: Ice(p); break;
                case WeatherArchetype.Temperate: Temperate(p, tempC); break;
                default: p.presets.Add(Clear()); break;
            }
        }

        static WeatherPreset Clear(float wind = 0.15f, float weight = 3f) => new WeatherPreset { name = "Clear", weight = weight, wind = wind, minutes = new Vector2(4f, 9f) };
        static WeatherPreset Add(WeatherProfile p, WeatherPreset s) { p.presets.Add(s); return s; }

        static void Airless(WeatherProfile p)
        {
            p.windSpeedMax = 0f; p.gustiness = 0f;
            p.staticVolume = Mathf.Lerp(0.4f, 0.7f, Rand01(p.bodyId, 11));
            p.baselineMotes = 0.04f;
            p.majorChance = 0.25f; p.calmBetweenMajorMinutes = new Vector2(8f, 20f);
            Add(p, new WeatherPreset { name = "Vacuum", weight = 4f, wind = 0f, motes = 0.05f, minutes = new Vector2(6f, 14f) });
            Add(p, new WeatherPreset { name = "Micrometeorite Shower", weight = 1f, major = true, wind = 0f, motes = 0.8f, minutes = new Vector2(1f, 2.5f) });
        }

        static void Mars(WeatherProfile p)
        {
            p.fogTint = new Color(0.72f, 0.5f, 0.38f); p.dustColor = new Color(0.8f, 0.52f, 0.34f, 0.45f);
            p.snowColor = new Color(0.95f, 0.88f, 0.86f, 0.7f);
            p.majorChance = 0.35f; p.calmBetweenMajorMinutes = new Vector2(6f, 14f);
            Add(p, Clear(0.12f, 3f));
            Add(p, new WeatherPreset { name = "Breeze", weight = 2f, wind = 0.35f, dust = 0.1f });
            Add(p, new WeatherPreset { name = "Dust Devils", weight = 2f, wind = 0.3f, dust = 0.08f, devils = 1f, minutes = new Vector2(3f, 7f) });
            Add(p, new WeatherPreset { name = "CO2 Frost Flurry", weight = 0.4f, wind = 0.2f, snow = 0.12f, particleSize = 0.6f, fog = 0.5f, minutes = new Vector2(1.5f, 3.5f) });
            Add(p, new WeatherPreset { name = "Dust Storm", weight = 1.2f, major = true, wind = 0.9f, dust = 0.85f, devils = 0.3f, fog = 5f, darkness = 0.5f, particleSize = 1.1f, minutes = new Vector2(3f, 7f) });
        }

        static void Desert(WeatherProfile p)
        {
            p.fogTint = new Color(0.78f, 0.6f, 0.4f); p.dustColor = new Color(0.82f, 0.62f, 0.38f, 0.5f);
            p.majorChance = 0.7f; p.calmBetweenMajorMinutes = new Vector2(3f, 8f);
            Add(p, Clear(0.18f, 3f));
            Add(p, new WeatherPreset { name = "Breezy Dust", weight = 2.5f, wind = 0.4f, dust = 0.25f });
            Add(p, new WeatherPreset { name = "Dust Haze", weight = 1.5f, wind = 0.25f, dust = 0.3f, fog = 2.2f, darkness = 0.15f });
            Add(p, new WeatherPreset { name = "Dust Devils", weight = 2f, wind = 0.3f, dust = 0.1f, devils = 1f, minutes = new Vector2(3f, 7f) });
            Add(p, new WeatherPreset { name = "Dust Storm", weight = 2f, major = true, wind = 0.85f, dust = 1f, devils = 0.3f, fog = 6f, darkness = 0.6f, particleSize = 1.2f, minutes = new Vector2(3f, 7f) });
            Add(p, new WeatherPreset { name = "Sandstorm", weight = 0.7f, major = true, wind = 1f, dust = 1f, fog = 9f, darkness = 0.75f, particleSize = 1.6f, minutes = new Vector2(2f, 4.5f) });
        }

        static void Volcanic(WeatherProfile p)
        {
            p.fogTint = new Color(0.46f, 0.4f, 0.32f); p.dustColor = new Color(0.62f, 0.55f, 0.3f, 0.45f);
            p.ashColor = new Color(0.3f, 0.28f, 0.27f, 0.9f); p.emberShare = 0.012f;
            p.majorChance = 0.75f; p.calmBetweenMajorMinutes = new Vector2(3f, 8f);
            Add(p, Clear(0.15f, 2f));
            Add(p, new WeatherPreset { name = "Ash Haze", weight = 2f, wind = 0.25f, ashfall = 0.12f, fog = 1.6f, darkness = 0.15f });
            Add(p, new WeatherPreset { name = "Sulphur Gusts", weight = 1.2f, wind = 0.6f, dust = 0.3f, fog = 2f, darkness = 0.1f });
            Add(p, new WeatherPreset { name = "Ash Fall", weight = 2.5f, wind = 0.3f, ashfall = 0.5f, fog = 2.5f, darkness = 0.3f });
            Add(p, new WeatherPreset { name = "Heavy Ash Fall", weight = 1.5f, major = true, wind = 0.5f, ashfall = 1f, fog = 5f, darkness = 0.6f, particleSize = 1.5f, minutes = new Vector2(3f, 7f) });
            Add(p, new WeatherPreset { name = "Ash Storm", weight = 1f, major = true, wind = 0.85f, ashfall = 0.9f, dust = 0.3f, fog = 7f, darkness = 0.8f, thunderPerMinute = 3f, particleSize = 1.4f, minutes = new Vector2(2.5f, 6f) });
        }

        static void Ice(WeatherProfile p)
        {
            p.fogTint = new Color(0.78f, 0.86f, 0.94f); p.dustColor = new Color(0.86f, 0.92f, 1f, 0.45f);
            p.snowColor = new Color(0.94f, 0.97f, 1f, 0.95f);
            p.majorChance = 0.65f; p.calmBetweenMajorMinutes = new Vector2(3f, 9f);
            Add(p, Clear(0.2f, 2.5f));
            Add(p, new WeatherPreset { name = "Diamond Dust", weight = 1.2f, wind = 0.1f, snow = 0.08f, particleSize = 0.5f });
            Add(p, new WeatherPreset { name = "Light Snow", weight = 2.5f, wind = 0.25f, snow = 0.3f, fog = 0.8f, darkness = 0.1f });
            Add(p, new WeatherPreset { name = "Snow Flurry", weight = 2f, wind = 0.45f, snow = 0.55f, fog = 1.8f, darkness = 0.2f });
            Add(p, new WeatherPreset { name = "Spindrift", weight = 1.2f, wind = 0.75f, dust = 0.45f, snow = 0.1f, fog = 1.5f });
            Add(p, new WeatherPreset { name = "Blizzard", weight = 1.5f, major = true, wind = 1f, snow = 1f, dust = 0.3f, fog = 8f, darkness = 0.5f, particleSize = 1.2f, minutes = new Vector2(3f, 7f) });
        }

        static void Temperate(WeatherProfile p, float tempC)
        {
            bool cold = tempC <= 0f;
            p.fogTint = new Color(0.66f, 0.7f, 0.74f);
            p.majorChance = 0.65f; p.calmBetweenMajorMinutes = new Vector2(4f, 10f);
            Add(p, Clear(0.15f, 3f));
            Add(p, new WeatherPreset { name = "Overcast Breeze", weight = 2f, wind = 0.35f, darkness = 0.25f });
            Add(p, new WeatherPreset { name = "Fog", weight = 1.2f, wind = 0.05f, fog = 4f, darkness = 0.15f });
            if (!cold)
            {
                Add(p, new WeatherPreset { name = "Drizzle", weight = 2f, wind = 0.25f, rain = 0.25f, fog = 1f, darkness = 0.3f });
                Add(p, new WeatherPreset { name = "Light Rain", weight = 2f, wind = 0.3f, rain = 0.5f, fog = 1.5f, darkness = 0.4f });
                Add(p, new WeatherPreset { name = "Heavy Rain", weight = 1.5f, major = true, wind = 0.6f, rain = 0.9f, fog = 3f, darkness = 0.6f, minutes = new Vector2(3f, 7f) });
                Add(p, new WeatherPreset { name = "Thunderstorm", weight = 1.2f, major = true, wind = 0.8f, rain = 1f, fog = 3f, darkness = 0.8f, thunderPerMinute = 3f, minutes = new Vector2(2.5f, 6f) });
            }
            else
            {
                Add(p, new WeatherPreset { name = "Cold Drizzle", weight = 1f, wind = 0.25f, rain = 0.2f, snow = 0.1f, fog = 1f, darkness = 0.3f });
                Add(p, new WeatherPreset { name = "Light Snow", weight = 2f, wind = 0.3f, snow = 0.35f, fog = 1f, darkness = 0.25f });
                Add(p, new WeatherPreset { name = "Sleet", weight = 1.5f, wind = 0.5f, rain = 0.4f, snow = 0.4f, fog = 2f, darkness = 0.4f });
                Add(p, new WeatherPreset { name = "Snowstorm", weight = 1.5f, major = true, wind = 0.9f, snow = 1f, fog = 6f, darkness = 0.55f, particleSize = 1.2f, minutes = new Vector2(3f, 7f) });
                Add(p, new WeatherPreset { name = "Thundersnow", weight = 0.5f, major = true, wind = 0.85f, snow = 0.9f, fog = 5f, darkness = 0.7f, thunderPerMinute = 1.5f, minutes = new Vector2(2f, 4f) });
            }
        }
    }
}
