using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    public enum ProceduralPlanetType { Rocky, Desert, Ice, Barren, Volcanic, GasGiant }

    /// <summary>Everything needed to rebuild a generated planet's surface identically every session (terrain, map, sites).</summary>
    public sealed class ProceduralPlanetRecipe
    {
        public string bodyId;
        public int seed;
        public ProceduralPlanetType type;
        public float relief;      // terrain height range (m)
        public float frequency;   // noise frequency (1/m)
        public Color low, high, rock;
        public Vector2 noiseOffset;
        /// <summary>Flat pads (world x, z, radius): base zone, dig sites, landing hints.</summary>
        public readonly List<Vector3> pads = new List<Vector3>();
        /// <summary>Square terrain holes (world x, z, half size) over each dig site; the dig grid's trench mesh fills them.</summary>
        public readonly List<Vector3> holes = new List<Vector3>();

        /// <summary>Terrain height (m above the terrain origin) at world x/z. Pads are flattened to their centre height.</summary>
        public float Height(float x, float z)
        {
            float h = Raw(x, z);
            foreach (var p in pads)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(p.x, p.y));
                float r0 = p.z, r1 = p.z * 2.2f;
                if (d >= r1) continue;
                float k = d <= r0 ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - r0) / (r1 - r0));
                h = Mathf.Lerp(h, Raw(p.x, p.y), k);
            }
            return h;
        }

        public float Raw(float x, float z)
        {
            float amp = 1f, f = frequency, sum = 0f, norm = 0f;
            for (int o = 0; o < 5; o++)
            {
                sum += amp * Mathf.PerlinNoise(x * f + noiseOffset.x + o * 17.3f, z * f + noiseOffset.y - o * 11.1f);
                norm += amp; amp *= 0.5f; f *= 2.03f;
            }
            float n = sum / norm;
            // Raised rim toward the terrain edge so the play area reads as a basin.
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) / (ProceduralGalaxy.TerrainSize * 0.5f);
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1f, edge));
            return Mathf.Clamp01(n * 0.75f + rim * 0.25f) * relief;
        }
    }

    /// <summary>
    /// t186u: procedural planets and systems. Fills every authored star system that has no bodies (Proxima Centauri,
    /// Barnard's Star, Epsilon Eridani, Tau Ceti, Sirius, Alpha Centauri) with a star and planets, and adds a few uncharted
    /// systems revealed by the navigation drive. Everything is derived from the galaxy seed and stable ids, so saves
    /// (discovery, sites, excavation) keep working; nothing is written to assets (runtime lists are NonSerialized).
    /// Generated planets land in one shared scene, <see cref="SurfaceScene"/>, whose terrain is rebuilt from the
    /// recipe of the ship's current body (<see cref="DMI.Xenoarch.World.ProceduralSurface"/>).
    /// </summary>
    public static class ProceduralGalaxy
    {
        public const string SurfaceScene = DMI.Xenoarch.Core.SceneNames.ProceduralSurface;
        public const float TerrainSize = 1024f;
        public const int HeightRes = 513;
        /// <summary>Published when a navigation drive (installed Vael navigation device) is fitted.</summary>
        public const string NavDriveFact = "tech.navdrive";
        public const int UnchartedSystems = 3;

        static GalaxyDefinition _done;
        static readonly Dictionary<string, ProceduralPlanetRecipe> _recipes = new Dictionary<string, ProceduralPlanetRecipe>();
        static readonly List<ArchaeologicalSiteDefinition> _sites = new List<ArchaeologicalSiteDefinition>();
        static readonly Dictionary<string, ArchaeologicalSiteDefinition> _siteById = new Dictionary<string, ArchaeologicalSiteDefinition>();

        public static IReadOnlyList<ArchaeologicalSiteDefinition> Sites => _sites;
        public static bool IsGenerated(string bodyId) => !string.IsNullOrEmpty(bodyId) && _recipes.ContainsKey(bodyId);
        public static ProceduralPlanetRecipe Recipe(string bodyId) => bodyId != null && _recipes.TryGetValue(bodyId, out var r) ? r : null;
        public static ArchaeologicalSiteDefinition Site(string id) => id != null && _siteById.TryGetValue(id, out var s) ? s : null;

        /// <summary>First landable planet of a system (interstellar arrival point), else its first body.</summary>
        public static CelestialBodyDefinition ArrivalBody(StarSystemDefinition sys)
        {
            if (sys == null) return null;
            CelestialBodyDefinition first = null;
            foreach (var b in sys.AllBodies)
            {
                if (b.kind == CelestialKind.Star) continue;
                if (first == null) first = b;
                if (b.HasLanding) return b;
            }
            return first;
        }

        /// <summary>Builds the generated content once per galaxy asset. <paramref name="artifactPool"/>: artifact ids that
        /// may be buried at generated sites (common to rare; chain artifacts excluded by the caller).</summary>
        public static void Ensure(GalaxyDefinition g, IList<string> artifactPool)
        {
            if (g == null || _done == g) return;
            _done = g;
            _recipes.Clear(); _sites.Clear(); _siteById.Clear();
            g.generatedSystems = new List<StarSystemDefinition>();
            var envs = new Dictionary<string, EnvironmentProfile>();
            var views = new Dictionary<string, OrbitViewDefinition>();
            foreach (var s in g.systems)
            {
                if (s == null) continue;
                s.generatedBodies = new List<CelestialBodyDefinition>();
                if (s.arrivalOrbitView != null) views[s.arrivalOrbitView.name.ToLowerInvariant()] = s.arrivalOrbitView;
                foreach (var b in s.bodies)
                {
                    if (b == null) continue;
                    if (b.environment != null) envs[b.environment.name.ToLowerInvariant()] = b.environment;
                    if (b.orbitView != null) views[b.orbitView.name.ToLowerInvariant()] = b.orbitView;
                }
            }

            // Uncharted systems (revealed by the navigation drive), 5-14 ly out.
            var grng = new System.Random(g.seed ^ 0x5EED);
            string[] cls = { "M3V", "M1V", "K5V", "K1V", "G9V", "M4.5V" };
            for (int i = 0; i < UnchartedSystems; i++)
            {
                var sys = ScriptableObject.CreateInstance<StarSystemDefinition>();
                sys.hideFlags = HideFlags.DontSave;
                sys.SetIdForTooling(GalaxyDefinition.GeneratedId("sys.gen.", g.Id, g.seed, i));
                sys.name = sys.Id;
                sys.displayName = $"Uncharted XS-{1000 + grng.Next(9000)}";
                float dist = 5f + (float)grng.NextDouble() * 9f;
                sys.galacticPosition = Random3(grng).normalized * dist;
                sys.stellarClass = cls[grng.Next(cls.Length)];
                sys.seed = grng.Next();
                sys.knownByDefault = false;
                sys.revealFact = NavDriveFact;
                sys.revealNote = "Located by the Vael navigation core's star catalogue. Uncharted by human surveys.";
                sys.minFtlRangeLy = dist;
                sys.archaeologicalPotential = 0.4f + (float)grng.NextDouble() * 0.5f;
                sys.generatedBodies = new List<CelestialBodyDefinition>();
                g.generatedSystems.Add(sys);
            }

            foreach (var s in g.AllSystems)
                if (s.bodies == null || s.bodies.Count == 0) Populate(g, s, artifactPool, envs, views);
            Debug.Log($"[Xenoarch] Procedural galaxy: {_recipes.Count} generated planet(s), {_sites.Count} dig site(s), {g.generatedSystems.Count} uncharted system(s)");
        }

        static Vector3 Random3(System.Random r) { var v = new Vector3((float)r.NextDouble() * 2f - 1f, ((float)r.NextDouble() * 2f - 1f) * 0.6f, (float)r.NextDouble() * 2f - 1f); return v.sqrMagnitude < 1e-4f ? Vector3.forward : v; }

        static void Populate(GalaxyDefinition g, StarSystemDefinition sys, IList<string> pool, Dictionary<string, EnvironmentProfile> envs, Dictionary<string, OrbitViewDefinition> views)
        {
            int seed = sys.seed != 0 ? sys.seed : Hash(sys.Id, g.seed);
            var rng = new System.Random(seed);
            string slug = sys.Id.StartsWith("sys.") ? sys.Id.Substring(4) : sys.Id;
            slug = slug.Replace("gen.", "g");

            var star = ScriptableObject.CreateInstance<CelestialBodyDefinition>();
            star.hideFlags = HideFlags.DontSave;
            star.SetIdForTooling($"body.{slug}.star"); star.name = star.Id;
            star.displayName = sys.displayName; star.kind = CelestialKind.Star; star.orbitAU = 0f;
            star.discoveredByDefault = true; star.mapColor = new Color(1f, 0.85f, 0.6f); star.seed = seed;
            star.description = $"{sys.displayName}, class {sys.stellarClass}.";
            sys.generatedBodies.Add(star);

            // Published planets keep their names and orbits (placeholders until Anthony's system decisions).
            var facts = StarFacts.For(sys.Id);
            var names = new List<(string name, float au, bool giant)>();
            if (facts != null)
                foreach (var p in facts.planets)
                    names.Add((p.name, Mathf.Max(0.02f, p.au), p.mass != null && p.mass.Contains("Jupiter")));
            if (names.Count == 0)
            {
                int n = 2 + rng.Next(3);
                float au = 0.08f + (float)rng.NextDouble() * 0.2f;
                for (int i = 0; i < n; i++)
                {
                    names.Add(($"{sys.displayName.Replace("Uncharted ", "")} {(char)('b' + i)}", au, i == n - 1 && rng.NextDouble() < 0.45));
                    au *= 1.6f + (float)rng.NextDouble() * 1.4f;
                }
            }

            int idx = 0;
            foreach (var (name, au, giant) in names)
            {
                var b = ScriptableObject.CreateInstance<CelestialBodyDefinition>();
                b.hideFlags = HideFlags.DontSave;
                b.SetIdForTooling($"body.{slug}.p{idx}"); b.name = b.Id;
                int bseed = Hash(b.Id, g.seed);
                var br = new System.Random(bseed);
                var type = giant ? ProceduralPlanetType.GasGiant : PickType(au, sys.stellarClass, br);
                b.displayName = name; b.kind = CelestialKind.Planet; b.orbitAU = au;
                b.orbitPeriodDays = Mathf.Round(365.25f * Mathf.Sqrt(au * au * au) * 10f) / 10f;
                b.seed = bseed;
                b.discoveredByDefault = false;
                b.radiusKm = giant ? 60000f + br.Next(20000) : 2500f + br.Next(5500);
                b.gravityG = giant ? 2.4f : Mathf.Round((0.2f + (float)br.NextDouble() * 0.85f) * 100f) / 100f;
                b.archaeologicalPotential = giant ? 0f : Mathf.Round((0.25f + (float)br.NextDouble() * 0.6f) * 100f) / 100f;
                b.mapColor = MapColor(type);
                b.environment = Env(type, b, envs);
                b.orbitView = View(type, b, views, br);
                b.description = Describe(type, b);
                sys.generatedBodies.Add(b);
                if (!giant) BuildSurface(b, type, br, pool);
                idx++;
            }
        }

        static ProceduralPlanetType PickType(float au, string cls, System.Random r)
        {
            bool dwarf = !string.IsNullOrEmpty(cls) && (cls[0] == 'M' || cls[0] == 'K');
            float hz = dwarf ? 0.15f : 0.9f; // rough habitable-zone distance
            if (au < hz * 0.5f) return r.NextDouble() < 0.5 ? ProceduralPlanetType.Volcanic : ProceduralPlanetType.Barren;
            if (au < hz * 1.6f) return r.NextDouble() < 0.5 ? ProceduralPlanetType.Rocky : ProceduralPlanetType.Desert;
            return r.NextDouble() < 0.7 ? ProceduralPlanetType.Ice : ProceduralPlanetType.Barren;
        }

        static Color MapColor(ProceduralPlanetType t) => t switch
        {
            ProceduralPlanetType.Desert => new Color(0.86f, 0.62f, 0.38f),
            ProceduralPlanetType.Ice => new Color(0.75f, 0.88f, 1f),
            ProceduralPlanetType.Barren => new Color(0.62f, 0.6f, 0.58f),
            ProceduralPlanetType.Volcanic => new Color(0.55f, 0.28f, 0.2f),
            ProceduralPlanetType.GasGiant => new Color(0.85f, 0.75f, 0.55f),
            _ => new Color(0.55f, 0.5f, 0.42f),
        };

        static EnvironmentProfile Env(ProceduralPlanetType t, CelestialBodyDefinition b, Dictionary<string, EnvironmentProfile> envs)
        {
            string key = t switch
            {
                ProceduralPlanetType.Ice => "icy",
                ProceduralPlanetType.Barren => "env_moon",
                ProceduralPlanetType.Volcanic => "mars",
                ProceduralPlanetType.GasGiant => "gas",
                _ => "mars",
            };
            EnvironmentProfile src = null;
            foreach (var kv in envs) if (kv.Key.Contains(key)) { src = kv.Value; break; }
            if (src == null) foreach (var kv in envs) if (kv.Key.Contains("moon")) { src = kv.Value; break; }
            if (src == null) return null;
            var e = Object.Instantiate(src);
            e.hideFlags = HideFlags.DontSave;
            e.name = src.name + " (" + b.Id + ")";
            e.SetIdForTooling("env.gen." + b.Id);
            e.displayName = b.displayName;
            e.gravity = Mathf.Max(0.5f, b.gravityG * 9.81f);
            if (t == ProceduralPlanetType.Volcanic) { e.atmosphere = "Thin, sulphurous"; e.surfaceTemperatureC = 180f; }
            else if (t == ProceduralPlanetType.Ice) e.surfaceTemperatureC = -170f;
            return e;
        }

        static OrbitViewDefinition View(ProceduralPlanetType t, CelestialBodyDefinition b, Dictionary<string, OrbitViewDefinition> views, System.Random r)
        {
            string key = t switch
            {
                ProceduralPlanetType.Ice => "ice",
                ProceduralPlanetType.Desert => "mars",
                ProceduralPlanetType.Barren => "orbitview_moon",
                ProceduralPlanetType.Volcanic => "dark",
                ProceduralPlanetType.GasGiant => "jupiter",
                _ => "ocean",
            };
            OrbitViewDefinition src = null;
            foreach (var kv in views) if (kv.Key.Contains(key)) { src = kv.Value; break; }
            if (src == null) foreach (var kv in views) { src = kv.Value; break; }
            if (src == null) return null;
            var v = Object.Instantiate(src);
            v.hideFlags = HideFlags.DontSave;
            v.name = src.name + " (" + b.Id + ")";
            if (src.planetMaterial != null)
            {
                var m = new Material(src.planetMaterial) { name = src.planetMaterial.name + " (" + b.Id + ")", hideFlags = HideFlags.DontSave };
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.Lerp(Color.white, b.mapColor, 0.35f));
                v.planetMaterial = m;
            }
            v.apparentDiameterDeg = Mathf.Clamp(src.apparentDiameterDeg * (0.8f + (float)r.NextDouble() * 0.4f), 20f, 90f);
            v.tiltDeg = (float)r.NextDouble() * 30f;
            return v;
        }

        static string Describe(ProceduralPlanetType t, CelestialBodyDefinition b) => t switch
        {
            ProceduralPlanetType.Desert => "Dry, wind-carved plains under a thin atmosphere. Generated survey data.",
            ProceduralPlanetType.Ice => "Frozen crust with buried structures showing through the ice. Generated survey data.",
            ProceduralPlanetType.Barren => "Airless, cratered rock. Old surfaces keep old ruins. Generated survey data.",
            ProceduralPlanetType.Volcanic => "Close to its star: dark basalt flats and vents. Generated survey data.",
            ProceduralPlanetType.GasGiant => "Gas giant: no surface to land on. Orbit only.",
            _ => "Rocky world with a thin atmosphere. Generated survey data.",
        };

        static void BuildSurface(CelestialBodyDefinition b, ProceduralPlanetType type, System.Random r, IList<string> pool)
        {
            var rec = new ProceduralPlanetRecipe
            {
                bodyId = b.Id, seed = b.seed, type = type,
                relief = type == ProceduralPlanetType.Ice ? 35f : type == ProceduralPlanetType.Volcanic ? 60f : 45f + (float)r.NextDouble() * 25f,
                frequency = 0.0025f + (float)r.NextDouble() * 0.0025f,
                noiseOffset = new Vector2((float)r.NextDouble() * 500f, (float)r.NextDouble() * 500f),
            };
            var c = b.mapColor;
            rec.low = Color.Lerp(c, Color.black, 0.25f); rec.high = Color.Lerp(c, Color.white, 0.2f); rec.rock = Color.Lerp(c, new Color(0.3f, 0.3f, 0.32f), 0.6f);
            _recipes[b.Id] = rec;

            b.sceneName = SurfaceScene;
            b.baseLanding = new Vector3(0f, 0f, -40f); b.hasBaseLanding = true;
            rec.pads.Add(new Vector3(0f, -40f, 26f));
            int n = 2 + r.Next(2);
            var made = new List<ArchaeologicalSiteDefinition>();
            float a0 = (float)r.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float ang = a0 + i * Mathf.PI * 2f / n + ((float)r.NextDouble() - 0.5f) * 0.6f;
                float dist = 200f + (float)r.NextDouble() * 140f;
                // Site centres on even metres so the dig grid (2 x radius) lines up with the 2 m terrain hole cells.
                float x = Mathf.Round(Mathf.Cos(ang) * dist / 2f) * 2f, z = Mathf.Round(Mathf.Sin(ang) * dist / 2f) * 2f;
                var site = ScriptableObject.CreateInstance<ArchaeologicalSiteDefinition>();
                site.hideFlags = HideFlags.DontSave;
                site.SetIdForTooling($"site.{b.Id.Substring(5)}.s{i}"); site.name = site.Id;
                site.displayName = SiteName(type, r);
                site.bodyId = b.Id; site.sceneName = SurfaceScene;
                site.radius = 4f; site.cellSize = 0.25f; site.depth = 1.5f + (float)r.NextDouble() * 0.5f;
                site.difficulty = 1 + r.Next(3); site.orbitalPassesNeeded = 1 + r.Next(2);
                site.signalStrength = 0.5f + (float)r.NextDouble() * 0.4f; site.seed = r.Next();
                site.description = "Generated archaeological signature.";
                rec.pads.Add(new Vector3(x, z, 14f));
                rec.holes.Add(new Vector3(x, z, site.radius));
                var toBase = new Vector2(0f - x, -40f - z).normalized;
                var hint = new Vector2(x, z) + toBase * 30f;
                rec.pads.Add(new Vector3(hint.x, hint.y, 12f));
                site.landingHint = new Vector3(hint.x, 0f, hint.y); site.hasLandingHint = true;
                if (pool != null && pool.Count > 0)
                {
                    int k = 2 + r.Next(2);
                    for (int a = 0; a < k; a++)
                        site.artifacts.Add(new SiteArtifactSpawn { artifactId = pool[r.Next(pool.Count)], cell = new Vector3Int(6 + r.Next(20), 1 + r.Next(4), 6 + r.Next(20)), footprint = 1 });
                }
                _sites.Add(site); _siteById[site.Id] = site; made.Add(site);
            }
            // Heights after every pad is known.
            b.baseLanding.y = rec.Height(0f, -40f);
            for (int i = 0; i < made.Count; i++)
            {
                var h = rec.holes[i];
                made[i].position = new Vector3(h.x, rec.Height(h.x, h.y), h.y);
                made[i].landingHint.y = rec.Height(made[i].landingHint.x, made[i].landingHint.z);
            }
            float half = 420f;
            b.surfaceMapRect = new Rect(-half, -half, half * 2f, half * 2f);
            b.surfaceMap = BakeMap(rec, b.surfaceMapRect, 256);
        }

        static string SiteName(ProceduralPlanetType t, System.Random r)
        {
            string[] a = { "Buried", "Sunken", "Shattered", "Silent", "Old", "Fused", "Hollow" };
            string[] n = t == ProceduralPlanetType.Ice ? new[] { "ice vault", "frozen spire", "rime shrine" }
                : t == ProceduralPlanetType.Volcanic ? new[] { "basalt hall", "vent relay", "ash cairn" }
                : new[] { "relay", "shrine", "cache", "spire", "outpost" };
            return $"{a[r.Next(a.Length)]} {n[r.Next(n.Length)]}";
        }

        /// <summary>Top-down shaded relief of the landing region (orbit scan map, minimap, M map).</summary>
        public static Texture2D BakeMap(ProceduralPlanetRecipe rec, Rect rect, int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { name = "SurfaceMap " + rec.bodyId, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[res * res];
            float step = rect.width / res;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float wx = rect.xMin + (x + 0.5f) * step, wz = rect.yMin + (y + 0.5f) * step;
                    float h = rec.Height(wx, wz), hx = rec.Height(wx + step, wz), hz = rec.Height(wx, wz + step);
                    var nrm = new Vector3(h - hx, step, h - hz).normalized;
                    float shade = Mathf.Clamp01(Vector3.Dot(nrm, new Vector3(-0.5f, 0.75f, 0.45f).normalized));
                    var col = Color.Lerp(rec.low, rec.high, h / Mathf.Max(1f, rec.relief));
                    px[x + y * res] = col * (0.45f + 0.75f * shade);
                    px[x + y * res].a = 1f;
                }
            tex.SetPixels(px); tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// Phase 2 M1: an independent, named RNG stream for one stable id (body, site, POI...). The seed depends only on
        /// (stableId, stream name, galaxy seed), so adding a new stream or drawing more from one stream never shifts any
        /// other stream or the original generator order. Use for all new procedural content (materials, pool v2, POIs,
        /// story inserts); never draw new values from the existing per-system / per-body generators.
        /// </summary>
        public static System.Random Stream(string stableId, string streamName, int galaxySeed) =>
            new System.Random(Hash((stableId ?? "") + ":" + (streamName ?? ""), galaxySeed));

        /// <summary>Editor tooling only (Galaxy Snapshot): forget the generated content so the next <see cref="Ensure"/>
        /// rebuilds it from scratch. In Edit Mode it also destroys the runtime-only (DontSave) objects it made.</summary>
        public static void ResetForTooling(GalaxyDefinition g)
        {
            if (g != null && !Application.isPlaying)
            {
                var doomed = new List<Object>();
                void Mark(Object o) { if (o != null && (o.hideFlags & HideFlags.DontSave) == HideFlags.DontSave && !doomed.Contains(o)) doomed.Add(o); }
                if (g.systems != null)
                    foreach (var s in g.systems) if (s != null && s.generatedBodies != null) foreach (var b in s.generatedBodies) MarkBody(b, Mark);
                if (g.generatedSystems != null)
                    foreach (var s in g.generatedSystems) { if (s == null) continue; if (s.generatedBodies != null) foreach (var b in s.generatedBodies) MarkBody(b, Mark); Mark(s); }
                foreach (var site in _sites) Mark(site);
                foreach (var o in doomed) Object.DestroyImmediate(o);
                if (g.systems != null) foreach (var s in g.systems) if (s != null) s.generatedBodies = new List<CelestialBodyDefinition>();
                g.generatedSystems = new List<StarSystemDefinition>();
            }
            _done = null;
            _recipes.Clear(); _sites.Clear(); _siteById.Clear();
        }

        static void MarkBody(CelestialBodyDefinition b, System.Action<Object> mark)
        {
            if (b == null || (b.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) return;
            mark(b.surfaceMap);
            mark(b.environment);
            if (b.orbitView != null) { mark(b.orbitView.planetMaterial); mark(b.orbitView); }
            mark(b);
        }

        public static int Hash(string s, int seed)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s ?? "") h = (h ^ ch) * 16777619;
                h = (h ^ (uint)seed) * 16777619;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}
