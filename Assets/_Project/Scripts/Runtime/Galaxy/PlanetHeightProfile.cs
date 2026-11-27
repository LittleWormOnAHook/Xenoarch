using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>Visual height style for a baked generated planet. One unique kind per landable body.</summary>
    public enum HeightShapeKind
    {
        FlatPlains = 0,
        GentleUndulation = 1,
        RollingHills = 2,
        RuggedFoothills = 3,
        Highlands = 4,
        BrokenPlateau = 5,
        TerracedSteps = 6,
        MesaTablelands = 7,
        CanyonLands = 8,
        BasinAndRange = 9,
        DuneSeas = 10,
        CrateredBasin = 11,
        HeavyCraters = 12,
        IsolatedPeaks = 13,
        MountainRange = 14,
        JaggedCliffs = 15,
        FissureRidges = 16,
        VolcanicCaldera = 17,
        IceSheetNunataks = 18,
        FrozenFjords = 19,
        CrevassedGlacier = 20,
        RollingIce = 21,
    }

    /// <summary>
    /// Per-planet heightmap profile. Wide ranges are intentional so values can be pushed to extremes in the Inspector,
    /// then rebuilt with Xenoarch/Planets/Rebake Terrain Only. Future scatter, vegetation and ice dressing fields are
    /// stored here for later work and are not applied yet.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/World/Planet Height Profile", fileName = "HeightProfile")]
    public sealed class PlanetHeightProfile : ScriptableObject
    {
        public string bodyId;
        [TextArea(2, 4)] public string description;
        public HeightShapeKind shape;

        [Tooltip("Baked terrain heightmap resolution (2^n+1). Moon and Mars use 2049.")]
        [Range(33, 4097)] public int heightmapResolution = 2049;

        [Header("Form (wide ranges for experimentation)")]
        [Range(8f, 400f)] public float relief = 80f;
        [Range(0.0002f, 0.04f)] public float frequency = 0.003f;
        [Range(1, 10)] public int octaves = 5;
        [Range(0.2f, 0.85f)] public float persistence = 0.5f;
        [Range(1.4f, 3.6f)] public float lacunarity = 2.03f;
        [Range(0f, 1f)] public float ridgeWeight;
        [Range(0f, 1f)] public float warp;
        [Range(0, 24)] public int terraceSteps;
        [Range(0f, 1f)] public float cliffSharpness;
        [Range(0f, 1f)] public float mesaCoverage;
        [Range(0f, 1f)] public float canyonDepth;
        [Range(0f, 1f)] public float craterDensity;
        [Range(0f, 1f)] public float peakDensity;
        [Range(0f, 80f)] public float rimHeight = 12f;
        [Range(0.6f, 0.98f)] public float rimStart = 0.82f;
        public Vector2 noiseOffset;
        public int seedOffset;

        [Header("Dig and landing pads")]
        [Tooltip("Minimum flatten radius (m) around landing zones and dig sites so excavations sit on reasonably flat ground.")]
        [Range(8f, 80f)] public float flattenRadius = 20f;
        [Range(1.2f, 4f)] public float flattenFalloff = 2.2f;

        [Header("Future (not applied yet)")]
        public bool futureRockPrefabs = true;
        public bool futureVegetation;
        public bool futureSnowLayer;
        public bool futureGlacierSheets;
        public bool futureIcebergs;
        public bool futureSnowDrifts;

        [NonSerialized] Crater[] _craters;
        [NonSerialized] Peak[] _peaks;
        [NonSerialized] int _cacheKey = int.MinValue;

        struct Crater { public float x, z, r, depth, rim; }
        struct Peak { public float x, z, r, h; }

        int Seed => (bodyId != null ? bodyId.GetHashCode() : 0) ^ seedOffset * 397;

        public float Sample(float x, float z)
        {
            EnsureCaches();
            float n = Shape(x, z);
            n = Mathf.Clamp01(n);
            float half = ProceduralGalaxy.TerrainSize * 0.5f;
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) / half;
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rimStart, 1f, edge));
            float h = n * Mathf.Max(0f, relief - rimHeight) + rim * rimHeight;
            return Mathf.Clamp(h, 0f, relief);
        }

        float Shape(float x, float z)
        {
            switch (shape)
            {
                case HeightShapeKind.FlatPlains: return 0.42f + (Fbm(x, z, frequency * 0.7f, Mathf.Max(2, octaves - 2)) - 0.5f) * 0.12f;
                case HeightShapeKind.GentleUndulation: return Mathf.Lerp(0.35f, 0.7f, Fbm(x, z, frequency, octaves));
                case HeightShapeKind.RollingHills: return Roll(x, z, 0.28f, 0.85f, 1f);
                case HeightShapeKind.RuggedFoothills: return Mathf.Pow(Mathf.Lerp(Fbm(x, z, frequency, octaves), Ridge(x, z, frequency * 1.2f), 0.45f + ridgeWeight * 0.4f), 0.85f);
                case HeightShapeKind.Highlands: return 0.55f + (Fbm(x, z, frequency * 0.6f, octaves) - 0.5f) * 0.4f + Ridge(x, z, frequency * 2.2f) * 0.08f;
                case HeightShapeKind.BrokenPlateau: return Broken(x, z);
                case HeightShapeKind.TerracedSteps: return Terrace(Mathf.Lerp(Fbm(x, z, frequency, octaves), Ridge(x, z, frequency), ridgeWeight), Mathf.Max(3, terraceSteps));
                case HeightShapeKind.MesaTablelands: return Mesas(x, z);
                case HeightShapeKind.CanyonLands: return Canyons(x, z);
                case HeightShapeKind.BasinAndRange: return BasinRange(x, z);
                case HeightShapeKind.DuneSeas: return Dunes(x, z);
                case HeightShapeKind.CrateredBasin: return Craters(Mathf.Lerp(0.38f, 0.62f, Fbm(x, z, frequency * 0.8f, octaves)), x, z, 0.7f);
                case HeightShapeKind.HeavyCraters: return Craters(0.5f + (Fbm(x, z, frequency, 3) - 0.5f) * 0.16f, x, z, 1f);
                case HeightShapeKind.IsolatedPeaks: return Isolated(x, z);
                case HeightShapeKind.MountainRange: return Mountains(x, z);
                case HeightShapeKind.JaggedCliffs: return Cliffs(x, z);
                case HeightShapeKind.FissureRidges: return Fissures(x, z);
                case HeightShapeKind.VolcanicCaldera: return Caldera(x, z);
                case HeightShapeKind.IceSheetNunataks: return IceSheet(x, z);
                case HeightShapeKind.FrozenFjords: return Fjords(x, z);
                case HeightShapeKind.CrevassedGlacier: return Crevasses(x, z);
                case HeightShapeKind.RollingIce: return 0.4f + (Fbm(x, z, frequency, octaves) - 0.5f) * 0.35f + Ridge(x, z, frequency * 3f) * 0.06f;
                default: return Fbm(x, z, frequency, octaves);
            }
        }

        float Roll(float x, float z, float lo, float hi, float sharp)
        {
            float n = Fbm(x, z, frequency, octaves);
            n = Mathf.Pow(Mathf.Clamp01(n), sharp);
            return Mathf.Lerp(lo, hi, n);
        }

        float Broken(float x, float z)
        {
            float baseN = Fbm(x, z, frequency * 0.7f, octaves);
            float fault = Mathf.Abs(Mathf.Sin((x * 0.008f + z * 0.003f + N(x, z, 0.002f, 20f, 8f) * 12f * warp)));
            fault = Mathf.Pow(1f - Mathf.Clamp01(fault * 1.4f), 4f);
            float plat = Terrace(Mathf.Lerp(0.35f, 0.85f, baseN), Mathf.Max(3, terraceSteps > 0 ? terraceSteps : 5));
            return Mathf.Clamp01(plat - fault * 0.35f);
        }

        float Mesas(float x, float z)
        {
            float n = Fbm(Wx(x, z), Wz(x, z), frequency, octaves);
            float cover = Mathf.Lerp(0.42f, 0.7f, mesaCoverage);
            float top = Mathf.SmoothStep(cover - 0.08f, cover + 0.02f, n);
            float slope = Mathf.SmoothStep(cover - 0.2f, cover - 0.08f, n) * 0.55f;
            return 0.18f + slope * 0.5f + top * 0.55f + Ridge(x, z, frequency * 3f) * 0.04f;
        }

        float Canyons(float x, float z)
        {
            float wx = Wx(x, z), wz = Wz(x, z);
            float plateau = 0.62f + (Fbm(x, z, frequency * 0.5f, 3) - 0.5f) * 0.16f;
            float v = Mathf.Abs(Mathf.Sin(wx * frequency * 22f + N(wx, wz, frequency * 0.8f, 40f, 9f) * 2f));
            float cut = Mathf.Pow(1f - Mathf.Clamp01(v * 1.15f), Mathf.Lerp(2.2f, 8f, 0.4f + canyonDepth * 0.6f));
            return Mathf.Clamp01(plateau - cut * Mathf.Lerp(0.28f, 0.7f, canyonDepth));
        }

        float BasinRange(float x, float z)
        {
            float ridge = Ridge(x * 0.35f + z * 0.08f, z, frequency * 0.9f);
            float valley = Fbm(x, z, frequency * 0.6f, 4);
            return Mathf.Lerp(valley * 0.35f + 0.15f, 0.35f + ridge * 0.65f, Mathf.SmoothStep(0.35f, 0.7f, ridge));
        }

        float Dunes(float x, float z)
        {
            float w = N(x, z, frequency * 0.4f, 3f, 11f) * 18f * (0.3f + warp);
            float a = Mathf.Sin((x + w) * frequency * 28f);
            float b = Mathf.Sin((x * 0.7f + z * 0.2f + w * 0.5f) * frequency * 51f) * 0.35f;
            float c = Fbm(x, z, frequency * 1.4f, 3) * 0.12f;
            return 0.28f + (a * 0.5f + 0.5f) * 0.45f + b * 0.12f + c;
        }

        float Isolated(float x, float z)
        {
            float floor = 0.18f + Fbm(x, z, frequency * 0.8f, 3) * 0.16f;
            float h = floor;
            if (_peaks == null) return floor;
            for (int i = 0; i < _peaks.Length; i++)
            {
                var p = _peaks[i];
                float d = Mathf.Sqrt((x - p.x) * (x - p.x) + (z - p.z) * (z - p.z));
                if (d >= p.r) continue;
                float t = 1f - d / p.r;
                h = Mathf.Max(h, floor + p.h * t * t * (3f - 2f * (1f - t)));
            }
            return Mathf.Clamp01(h);
        }

        float Mountains(float x, float z)
        {
            float r = Ridge(Wx(x, z), Wz(x, z), frequency);
            r = Mathf.Pow(r, Mathf.Lerp(0.75f, 1.6f, ridgeWeight));
            float detail = Ridge(x, z, frequency * 2.4f) * 0.18f;
            return Mathf.Clamp01(r * 0.85f + detail);
        }

        float Cliffs(float x, float z)
        {
            float n = Fbm(Wx(x, z), Wz(x, z), frequency, octaves);
            float sharp = Mathf.Lerp(1.4f, 6f, cliffSharpness);
            float step = Mathf.Pow(Mathf.Clamp01((n - 0.42f) / 0.35f), sharp);
            float jag = Ridge(x, z, frequency * 3.5f) * 0.12f * cliffSharpness;
            return Mathf.Clamp01(0.2f + n * 0.25f + step * 0.55f + jag);
        }

        float Fissures(float x, float z)
        {
            float baseN = Fbm(x, z, frequency, 4) * 0.45f + 0.2f;
            float f = Mathf.Abs(Mathf.Sin((z + N(x, z, 0.003f, 15f, 4f) * 30f * (0.2f + warp)) * frequency * 18f));
            float cut = Mathf.Pow(1f - Mathf.Clamp01(f * 1.3f), 5f);
            float ridge = Ridge(x, z, frequency * 1.6f) * 0.2f;
            return Mathf.Clamp01(baseN + ridge - cut * 0.4f);
        }

        float Caldera(float x, float z)
        {
            float cone = 0f;
            if (_peaks != null)
                for (int i = 0; i < _peaks.Length; i++)
                {
                    var p = _peaks[i];
                    float d = Mathf.Sqrt((x - p.x) * (x - p.x) + (z - p.z) * (z - p.z));
                    float t = 1f - Mathf.Clamp01(d / p.r);
                    float mountain = p.h * t * t;
                    float throat = p.r * 0.28f;
                    if (d < throat)
                    {
                        float u = 1f - d / throat;
                        mountain -= p.h * 0.55f * u * u;
                    }
                    cone = Mathf.Max(cone, mountain);
                }
            float floor = 0.16f + Fbm(x, z, frequency * 1.2f, 3) * 0.12f;
            return Mathf.Clamp01(floor + cone);
        }

        float IceSheet(float x, float z)
        {
            float sheet = 0.28f + Fbm(x, z, frequency * 0.45f, 3) * 0.12f;
            return Mathf.Clamp01(sheet + Isolated(x, z) * 0.7f);
        }

        float Fjords(float x, float z)
        {
            float high = 0.55f + (Fbm(x, z, frequency * 0.5f, 4) - 0.5f) * 0.2f;
            float w = N(x, z, frequency * 0.35f, 8f, 19f) * 40f * (0.25f + warp);
            float v = Mathf.Abs(Mathf.Sin((z + w) * frequency * 10f));
            float cut = Mathf.Pow(1f - Mathf.Clamp01(v * 1.05f), Mathf.Lerp(2.5f, 7f, canyonDepth));
            return Mathf.Clamp01(high - cut * Mathf.Lerp(0.3f, 0.62f, canyonDepth));
        }

        float Crevasses(float x, float z)
        {
            float flow = 0.4f + (Fbm(x, z, frequency * 0.6f, 4) - 0.5f) * 0.25f;
            float c = Mathf.Abs(Mathf.Sin((x * 0.8f + z * 0.2f) * frequency * 40f + N(x, z, frequency, 6f, 2f) * 4f));
            float cut = Mathf.Pow(1f - Mathf.Clamp01(c * 1.25f), 8f);
            return Mathf.Clamp01(flow - cut * 0.22f);
        }

        float Craters(float baseH, float x, float z, float scale)
        {
            if (_craters == null) return baseH;
            float h = baseH;
            for (int i = 0; i < _craters.Length; i++)
            {
                var c = _craters[i];
                float d = Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z));
                float reach = c.r * 1.45f;
                if (d >= reach) continue;
                float bowlR = c.r * 0.78f;
                if (d <= bowlR)
                {
                    float u = d / bowlR;
                    h -= c.depth * scale * (1f - u * u);
                }
                else
                {
                    float u = (d - bowlR) / (reach - bowlR);
                    h += c.rim * scale * (1f - u) * (1f - u) * 4f * u;
                }
            }
            return Mathf.Clamp01(h);
        }

        float Wx(float x, float z) => x + (N(x, z, frequency * 0.35f, 13f, 7f) - 0.5f) * 80f * warp;
        float Wz(float x, float z) => z + (N(x, z, frequency * 0.35f, 31f, 17f) - 0.5f) * 80f * warp;

        float Terrace(float n, int steps)
        {
            if (steps <= 1) return n;
            float s = steps;
            float i = Mathf.Floor(n * s);
            float f = n * s - i;
            // Soft treads, steep risers.
            f = Mathf.SmoothStep(0f, 1f, Mathf.Pow(f, 0.45f));
            return (i + f) / s;
        }

        float Fbm(float x, float z, float freq, int oct)
        {
            float amp = 1f, f = freq, sum = 0f, norm = 0f;
            float ox = noiseOffset.x, oz = noiseOffset.y;
            int n = Mathf.Clamp(oct, 1, 10);
            for (int o = 0; o < n; o++)
            {
                sum += amp * N(x, z, f, ox + o * 17.3f, oz - o * 11.1f);
                norm += amp; amp *= persistence; f *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0.5f;
        }

        float Ridge(float x, float z, float freq)
        {
            float r = Fbm(x, z, freq, Mathf.Max(3, octaves - 1));
            r = 1f - Mathf.Abs(r * 2f - 1f);
            return r * r;
        }

        float N(float x, float z, float f, float ox, float oz) => Mathf.PerlinNoise(x * f + ox, z * f + oz);

        void EnsureCaches()
        {
            int key = Seed;
            if (_cacheKey == key && _craters != null && _peaks != null) return;
            _cacheKey = key;
            var rng = new System.Random(key);
            float half = ProceduralGalaxy.TerrainSize * 0.42f;
            int nc = Mathf.RoundToInt(Mathf.Lerp(6f, 70f, craterDensity));
            _craters = new Crater[nc];
            for (int i = 0; i < nc; i++)
            {
                _craters[i] = new Crater
                {
                    x = (float)(rng.NextDouble() * 2.0 - 1.0) * half,
                    z = (float)(rng.NextDouble() * 2.0 - 1.0) * half,
                    r = 18f + (float)rng.NextDouble() * (craterDensity > 0.6f ? 70f : 48f),
                    depth = 0.08f + (float)rng.NextDouble() * 0.22f,
                    rim = 0.03f + (float)rng.NextDouble() * 0.08f,
                };
            }
            int np = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(2f, 12f, peakDensity)));
            _peaks = new Peak[np];
            for (int i = 0; i < np; i++)
            {
                _peaks[i] = new Peak
                {
                    x = (float)(rng.NextDouble() * 2.0 - 1.0) * half * 0.85f,
                    z = (float)(rng.NextDouble() * 2.0 - 1.0) * half * 0.85f,
                    r = 70f + (float)rng.NextDouble() * 160f,
                    h = 0.45f + (float)rng.NextDouble() * 0.5f,
                };
            }
        }
    }

    [CreateAssetMenu(menuName = "Xenoarch/World/Planet Height Catalog", fileName = "PlanetHeightCatalog")]
    public sealed class PlanetHeightCatalog : ScriptableObject
    {
        public const string ResourcePath = "Galaxy/PlanetHeightCatalog";
        public List<PlanetHeightProfile> profiles = new List<PlanetHeightProfile>();

        static PlanetHeightCatalog _loaded;
        public static void Invalidate() { _loaded = null; }
        public static PlanetHeightCatalog Load()
        {
            if (_loaded == null) _loaded = Resources.Load<PlanetHeightCatalog>(ResourcePath);
            return _loaded;
        }

        public static PlanetHeightProfile For(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId)) return null;
            var cat = Load();
            if (cat == null || cat.profiles == null) return null;
            for (int i = 0; i < cat.profiles.Count; i++)
            {
                var p = cat.profiles[i];
                if (p != null && p.bodyId == bodyId) return p;
            }
            return null;
        }
    }
}
