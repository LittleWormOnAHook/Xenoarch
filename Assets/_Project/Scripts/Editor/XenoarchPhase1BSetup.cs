using System;
using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1B data: environment profiles, the Sol system (18 navigational bodies + TradeHub Sol), galaxy root,
    /// starter ship (Constellation Mk I), Moon/Mars archaeological sites, handheld scanner stats and the ship interior
    /// URP Volume profile; wires them into XenoarchConfig. Idempotent: existing assets keep their (hand-tuned) values.
    /// Does not open or modify any scene.
    /// </summary>
    public static class XenoarchPhase1BSetup
    {
        public const string GalaxyFolder = XenoarchSetup.Root + "/Data/Galaxy";
        public const string EnvFolder = GalaxyFolder + "/Environments";
        public const string BodyFolder = GalaxyFolder + "/Sol";
        public const string SiteFolder = GalaxyFolder + "/Sites";
        public const string ShipFolder = XenoarchSetup.Root + "/Data/Ships";
        public const string ToolFolder = XenoarchSetup.Root + "/Data/Tools";
        public const string RenderingFolder = XenoarchSetup.Root + "/Data/Rendering";
        public const string GalaxyPath = GalaxyFolder + "/Galaxy_MilkyWay.asset";
        public const string SolPath = GalaxyFolder + "/System_Sol.asset";
        public const string SiteRegistryPath = SiteFolder + "/SiteRegistry.asset";
        public const string ShipPath = ShipFolder + "/Ship_ConstellationMk1.asset";
        public const string ScannerPath = ToolFolder + "/Scanner_HandheldMk1.asset";
        public const string ShipVolumePath = RenderingFolder + "/VP_Ship_Interior.asset";
        public const string SkyboxFolder = XenoarchSetup.Root + "/Art/Skyboxes";
        public const string CreepySkyboxes = "Assets/_Creepy_Cat/_3D Scifi Kit Vol 3/Textures/_Skyboxes";
        /// <summary>Xenoarch skybox material -> Creepy Cat face-texture prefix ("*" = one texture on all faces).</summary>
        public static readonly (string mat, string prefix)[] Skies =
        {
            ("Sky_DeepSpace", "Space_01"),
            ("Sky_Starfield", "Space_02_AllFaces*"),
            ("Sky_EarthOrbit", "Skybox_01"),
            ("Sky_IcyOrbit", "Skybox_02"),
            ("Sky_MarsOrbit", "Skybox_03"),
            ("Sky_MoonOrbit", "Skybox_04"),
        };
        public const string DefaultSky = "Sky_DeepSpace";
        public static readonly Dictionary<string, string> OrbitSkies = new Dictionary<string, string>
        {
            ["body.sol.earth"] = "Sky_EarthOrbit", [TradeHubId] = "Sky_EarthOrbit", ["body.sol.moon"] = "Sky_MoonOrbit",
            ["body.sol.mars"] = "Sky_MarsOrbit", ["body.sol.europa"] = "Sky_IcyOrbit", ["body.sol.enceladus"] = "Sky_IcyOrbit",
        };
        public static string SkyPath(string mat) => $"{SkyboxFolder}/{mat}.mat";
        public const string CreepyShips = "Assets/_Creepy_Cat/_3D Scifi Kit Vol 3/Prefabs/Props/_Update 1.11-Spaceship Constellation_R1";

        public static readonly string[] SolBodyIds =
        {
            "body.sol.sun", "body.sol.mercury", "body.sol.venus", "body.sol.earth", "body.sol.moon", "body.sol.mars",
            "body.sol.asteroid_belt", "body.sol.jupiter", "body.sol.io", "body.sol.europa", "body.sol.ganymede",
            "body.sol.callisto", "body.sol.saturn", "body.sol.titan", "body.sol.enceladus", "body.sol.uranus",
            "body.sol.neptune", "body.sol.pluto",
        };
        public const string TradeHubId = "body.sol.tradehub";

        [MenuItem("Xenoarch/Setup/7. Phase 1B Data (galaxy, ship, sites, scanner)", priority = 27)]
        public static void CreateAll()
        {
            foreach (var f in new[] { EnvFolder, BodyFolder, SiteFolder, ShipFolder, ToolFolder, RenderingFolder }) XenoarchSetup.EnsureFolder(f);

            var shipVp = EnsureShipVolumeProfile();
            var env = new Dictionary<string, EnvironmentProfile>
            {
                ["ship"] = Env("env.ship_interior", "Ship interior (artificial gravity)", 9.81f, "Ship atmosphere", true, 21f, false, default, false, default, 0f, shipVp),
                ["station"] = Env("env.station", "Orbital station", 9.81f, "Station atmosphere", true, 20f, false, default, false, default, 0f, null),
                ["vacuum"] = Env("env.vacuum", "Vacuum (default)", 9.81f, "None", false, -270f, false, default, false, default, 0f, null),
                ["moon"] = Env("env.moon", "Lunar surface", 1.62f, "None (vacuum)", false, -20f, true, new Color(0.03f, 0.03f, 0.04f), false, default, 0f, null),
                ["mars"] = Env("env.mars", "Martian surface", 3.71f, "Thin CO2 (0.6 kPa)", false, -60f, true, new Color(0.45f, 0.31f, 0.24f), true, new Color(0.72f, 0.5f, 0.36f), 0.004f, null),
                ["venus"] = Env("env.venus", "Venusian (inaccessible)", 8.87f, "Dense CO2, sulfuric acid clouds", false, 465f, false, default, false, default, 0f, null),
                ["gas"] = Env("env.gas_giant", "Gas giant upper atmosphere", 24.8f, "H2/He", false, -110f, false, default, false, default, 0f, null),
                ["icy"] = Env("env.icy_moon", "Icy moon surface", 1.3f, "None / trace", false, -170f, false, default, false, default, 0f, null),
                ["earth"] = Env("env.earth", "Earth (restricted)", 9.81f, "N2/O2", true, 15f, false, default, false, default, 0f, null),
            };

            // name, id, kind, parent, AU (moons/stations: offset from parent), period d, radius km, g, env, scene, potential, color, desc
            var b = new List<CelestialBodyDefinition>
            {
                Body("Sun", "body.sol.sun", CelestialKind.Star, null, 0f, 0f, 696340f, 28f, null, null, 0f, new Color(1f, 0.85f, 0.4f), "G2V main-sequence star."),
                Body("Mercury", "body.sol.mercury", CelestialKind.Planet, null, 0.39f, 88f, 2440f, 0.38f, env["vacuum"], null, 0.1f, new Color(0.6f, 0.58f, 0.55f), "Scorched, airless. No surveyed landing zone."),
                Body("Venus", "body.sol.venus", CelestialKind.Planet, null, 0.72f, 225f, 6052f, 0.9f, env["venus"], null, 0.05f, new Color(0.9f, 0.8f, 0.55f), "Surface conditions exceed current equipment limits."),
                Body("Earth", "body.sol.earth", CelestialKind.Planet, null, 1f, 365.25f, 6371f, 1f, env["earth"], null, 0f, new Color(0.3f, 0.55f, 0.95f), "Home. Surface excavation is restricted by the Heritage Authority."),
                Body("Moon", "body.sol.moon", CelestialKind.Moon, "body.sol.earth", 0.00257f, 27.3f, 1737f, 0.165f, env["moon"], SceneNames.MoonExcavation01, 0.55f, new Color(0.8f, 0.8f, 0.8f), "Training ground for licensed xenoarchaeologists. Several old survey anomalies remain unexplained."),
                Body("TradeHub Sol", TradeHubId, CelestialKind.Station, "body.sol.earth", 0.0004f, 0.07f, 0f, 1f, env["station"], SceneNames.TradeHubSol, 0f, new Color(0.95f, 0.66f, 0.22f), "Orbital trade hub: artifact buyers, equipment, refuelling."),
                Body("Mars", "body.sol.mars", CelestialKind.Planet, null, 1.52f, 687f, 3390f, 0.38f, env["mars"], SceneNames.MarsExcavation01, 0.8f, new Color(0.85f, 0.42f, 0.25f), "Dust-buried structures predate every human mission."),
                Body("Asteroid Belt", "body.sol.asteroid_belt", CelestialKind.Belt, null, 2.7f, 1680f, 0f, 0f, env["vacuum"], null, 0.3f, new Color(0.55f, 0.5f, 0.45f), "Navigational entry. Mining claims; occasional anomalous returns."),
                Body("Jupiter", "body.sol.jupiter", CelestialKind.Planet, null, 5.2f, 4333f, 69911f, 2.53f, env["gas"], null, 0f, new Color(0.85f, 0.7f, 0.55f), "Gas giant. Orbit only."),
                Body("Io", "body.sol.io", CelestialKind.Moon, "body.sol.jupiter", 0.0028f, 1.77f, 1821f, 0.18f, env["icy"], null, 0.2f, new Color(0.95f, 0.85f, 0.35f), "Volcanic. Surface hazards exceed current equipment."),
                Body("Europa", "body.sol.europa", CelestialKind.Moon, "body.sol.jupiter", 0.0045f, 3.55f, 1561f, 0.134f, env["icy"], null, 0.45f, new Color(0.85f, 0.82f, 0.75f), "Ice shell over a subsurface ocean."),
                Body("Ganymede", "body.sol.ganymede", CelestialKind.Moon, "body.sol.jupiter", 0.0072f, 7.15f, 2634f, 0.146f, env["icy"], null, 0.35f, new Color(0.65f, 0.6f, 0.55f), "Largest moon in the system."),
                Body("Callisto", "body.sol.callisto", CelestialKind.Moon, "body.sol.jupiter", 0.0126f, 16.7f, 2410f, 0.126f, env["icy"], null, 0.4f, new Color(0.45f, 0.42f, 0.4f), "Ancient, heavily cratered surface."),
                Body("Saturn", "body.sol.saturn", CelestialKind.Planet, null, 9.54f, 10759f, 58232f, 1.07f, env["gas"], null, 0f, new Color(0.9f, 0.82f, 0.6f), "Ringed gas giant. Orbit only."),
                Body("Titan", "body.sol.titan", CelestialKind.Moon, "body.sol.saturn", 0.0082f, 15.9f, 2575f, 0.138f, env["icy"], null, 0.5f, new Color(0.85f, 0.65f, 0.35f), "Thick nitrogen haze, hydrocarbon lakes."),
                Body("Enceladus", "body.sol.enceladus", CelestialKind.Moon, "body.sol.saturn", 0.0016f, 1.37f, 252f, 0.0113f, env["icy"], null, 0.25f, new Color(0.95f, 0.95f, 1f), "Cryovolcanic plumes."),
                Body("Uranus", "body.sol.uranus", CelestialKind.Planet, null, 19.2f, 30687f, 25362f, 0.89f, env["gas"], null, 0f, new Color(0.6f, 0.85f, 0.9f), "Ice giant. Orbit only."),
                Body("Neptune", "body.sol.neptune", CelestialKind.Planet, null, 30.1f, 60190f, 24622f, 1.14f, env["gas"], null, 0f, new Color(0.3f, 0.45f, 0.95f), "Ice giant. Orbit only."),
                Body("Pluto", "body.sol.pluto", CelestialKind.DwarfPlanet, null, 39.5f, 90560f, 1188f, 0.063f, env["icy"], null, 0.35f, new Color(0.8f, 0.7f, 0.6f), "Edge of charted space."),
            };

            var sol = LoadOrCreate<StarSystemDefinition>(SolPath, out bool newSol);
            if (newSol || sol.bodies.Count == 0)
            {
                sol.SetIdForTooling("sys.sol");
                sol.displayName = "Sol"; sol.stellarClass = "G2V"; sol.galacticPosition = Vector3.zero; sol.seed = 1;
                sol.knownByDefault = true; sol.minFtlRangeLy = 0f; sol.archaeologicalPotential = 0.6f;
            }
            foreach (var body in b) if (!sol.bodies.Contains(body)) sol.bodies.Add(body);
            EditorUtility.SetDirty(sol);

            var galaxy = LoadOrCreate<GalaxyDefinition>(GalaxyPath, out bool newGal);
            if (newGal) { galaxy.SetIdForTooling("galaxy.milky_way"); galaxy.seed = 1337; }
            if (!galaxy.systems.Contains(sol)) galaxy.systems.Add(sol);
            EditorUtility.SetDirty(galaxy);

            var ship = LoadOrCreate<ShipDefinition>(ShipPath, out bool newShip);
            if (newShip)
            {
                ship.SetIdForTooling("ship.constellation_mk1");
                ship.displayName = "Constellation Mk I";
                ship.fuelCapacity = 100f; ship.fuelPerAU = 10f; ship.minimumHopFuel = 1f; ship.ftlRangeLy = 0f;
                ship.scannerLevel = 1; ship.moduleSlots = 4; ship.cargoSlots = 12;
                ship.startingModules = new List<string> { "module.drive.ion_mk1", "module.scanner.orbital_mk1", "module.lab.basic" };
                ship.startingBodyId = "body.sol.earth";
            }
            if (ship.interiorEnvironment == null) ship.interiorEnvironment = env["ship"];
            if (ship.exteriorPrefab == null) ship.exteriorPrefab = FindPrefab(CreepyShips, "fake");
            if (ship.interiorPrefab == null) ship.interiorPrefab = FindPrefab(CreepyShips, "stand");
            EditorUtility.SetDirty(ship);

            var sites = new List<ArchaeologicalSiteDefinition>
            {
                Site("site.moon.tranquility_cache", "Tranquility Cache", "body.sol.moon", SceneNames.MoonExcavation01, new Vector3(0f, 0f, 10f), 3f, 1.2f, 1, 1, 0.85f, 101, "A shallow cache under the regolith beside the old survey marker."),
                Site("site.moon.ridge_vault", "Ridge Vault", "body.sol.moon", SceneNames.MoonExcavation01, new Vector3(45f, 0f, -38f), 4f, 2.4f, 2, 2, 0.6f, 102, "Regular voids below a low ridge."),
                Site("site.moon.deep_echo", "Deep Echo", "body.sol.moon", SceneNames.MoonExcavation01, new Vector3(-70f, 0f, 60f), 5f, 4.5f, 3, 3, 0.45f, 103, "Faint, deep return. Beyond Mk I scanner depth."),
                Site("site.mars.dune_shrine", "Dune Shrine", "body.sol.mars", SceneNames.MarsExcavation01, new Vector3(0f, 0f, 10f), 3f, 1.0f, 1, 1, 0.8f, 201, "Geometric shapes under shifting dust."),
                Site("site.mars.canyon_relay", "Canyon Relay", "body.sol.mars", SceneNames.MarsExcavation01, new Vector3(-55f, 0f, 30f), 4f, 2.2f, 2, 2, 0.6f, 202, "Metallic signature with a periodic echo."),
                Site("site.mars.buried_spire", "Buried Spire", "body.sol.mars", SceneNames.MarsExcavation01, new Vector3(60f, 0f, 70f), 6f, 3.8f, 3, 3, 0.5f, 203, "Tall, narrow structure, mostly buried. Needs a better scanner."),
            };
            var reg = LoadOrCreate<SiteRegistry>(SiteRegistryPath, out _);
            var merged = reg.Entries.Where(e => e != null).ToList();
            foreach (var s in sites) if (!merged.Contains(s)) merged.Add(s);
            reg.SetEntries(merged);
            EditorUtility.SetDirty(reg);

            var scanner = LoadOrCreate<SurfaceScannerDefinition>(ScannerPath, out bool newScan);
            if (newScan)
            {
                scanner.SetIdForTooling("scanner.handheld_mk1");
                scanner.itemId = "item.tool.handheld_scanner";
                scanner.range = 30f; scanner.maxDepth = 3f; scanner.accuracy = 0.7f; scanner.pinpointRadius = 3f; scanner.cooldown = 2f;
            }
            EditorUtility.SetDirty(scanner);

            int skies = EnsureSkyboxes();
            foreach (var kv in OrbitSkies)
            {
                var body = sol.FindBody(kv.Key);
                if (body != null && body.orbitSkybox == null) { body.orbitSkybox = AssetDatabase.LoadAssetAtPath<Material>(SkyPath(kv.Value)); EditorUtility.SetDirty(body); }
            }

            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config != null)
            {
                if (config.defaultSpaceSkybox == null) config.defaultSpaceSkybox = AssetDatabase.LoadAssetAtPath<Material>(SkyPath(DefaultSky));
                if (config.galaxy == null) config.galaxy = galaxy;
                if (config.starterShip == null) config.starterShip = ship;
                if (config.siteRegistry == null) config.siteRegistry = reg;
                if (config.surfaceScanner == null) config.surfaceScanner = scanner;
                if (config.defaultEnvironment == null) config.defaultEnvironment = env["vacuum"];
                EditorUtility.SetDirty(config);
            }
            else Debug.LogWarning("[Xenoarch] 1B setup: XenoarchConfig missing (run Setup 1 first)");

            AssetDatabase.SaveAssets();
            Debug.Log($"[Xenoarch] 1B setup: {sol.bodies.Count} bodies in Sol, {reg.Entries.Count} sites, ship '{ship.displayName}' (exterior {(ship.exteriorPrefab ? ship.exteriorPrefab.name : "none")}, interior {(ship.interiorPrefab ? ship.interiorPrefab.name : "none")}), scanner range {scanner.range} m");
        }

        [MenuItem("Xenoarch/Setup/7b. Space Skyboxes (from Creepy Cat textures)", priority = 27)]
        public static void SkyboxMenu() { int n = EnsureSkyboxes(); Debug.Log($"[Xenoarch] Skyboxes: {n} material(s) in {SkyboxFolder}"); }

        /// <summary>Builds Skybox/6 Sided materials (built-in shader, URP-compatible) from the kit's face textures. The kit's own
        /// .mat files point at a shader that isn't in this project, so they are not used. Returns materials present.</summary>
        public static int EnsureSkyboxes()
        {
            XenoarchSetup.EnsureFolder(SkyboxFolder);
            var shader = Shader.Find("Skybox/6 Sided");
            if (shader == null) { Debug.LogError("[Xenoarch] Skyboxes: shader 'Skybox/6 Sided' not found"); return 0; }
            int n = 0;
            foreach (var (matName, prefixRaw) in Skies)
            {
                bool single = prefixRaw.EndsWith("*");
                string prefix = prefixRaw.TrimEnd('*');
                string path = SkyPath(matName);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) { n++; continue; }
                var faces = new Dictionary<string, Texture2D>();
                foreach (var (prop, face) in new[] { ("_FrontTex", "Front"), ("_BackTex", "Back"), ("_LeftTex", "Left"), ("_RightTex", "Right"), ("_UpTex", "Top"), ("_DownTex", "Bottom") })
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CreepySkyboxes}/{(single ? prefix : prefix + "_" + face)}.png");
                    if (tex == null) { faces = null; break; }
                    faces[prop] = tex;
                }
                if (faces == null) { Debug.LogWarning($"[Xenoarch] Skyboxes: textures for {prefix} missing, {matName} skipped"); continue; }
                mat = new Material(shader) { name = matName };
                foreach (var kv in faces) mat.SetTexture(kv.Key, kv.Value);
                mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                mat.SetFloat("_Exposure", 1f);
                AssetDatabase.CreateAsset(mat, path);
                n++;
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        public static VolumeProfile EnsureShipVolumeProfile()
        {
            var vp = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShipVolumePath);
            if (vp != null) return vp;
            vp = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(vp, ShipVolumePath);
            // Recreates the Creepy Cat PPv2 "look" with URP overrides: soft bloom on emissive panels, ACES, slight cool grade, vignette.
            var bloom = vp.Add<Bloom>(true); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.65f);
            var tone = vp.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
            var ca = vp.Add<ColorAdjustments>(true); ca.postExposure.Override(0.1f); ca.contrast.Override(8f); ca.saturation.Override(-5f); ca.colorFilter.Override(new Color(0.96f, 0.99f, 1f));
            var vig = vp.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.smoothness.Override(0.4f);
            foreach (var c in vp.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, vp); }
            EditorUtility.SetDirty(vp);
            return vp;
        }

        // ------------------------------------------------------------------ helpers

        static EnvironmentProfile Env(string id, string name, float g, string atmo, bool breathable, float tempC, bool lighting, Color ambient, bool fog, Color fogColor, float fogDensity, VolumeProfile vp)
        {
            var path = $"{EnvFolder}/Env_{id.Substring(4)}.asset";
            var e = LoadOrCreate<EnvironmentProfile>(path, out bool created);
            if (created)
            {
                e.SetIdForTooling(id); e.displayName = name; e.gravity = g; e.atmosphere = atmo; e.breathable = breathable; e.surfaceTemperatureC = tempC;
                e.applyLighting = lighting; e.ambientColor = ambient; e.fog = fog; e.fogColor = fogColor; e.fogDensity = fogDensity; e.volumeProfile = vp;
                EditorUtility.SetDirty(e);
            }
            return e;
        }

        static CelestialBodyDefinition Body(string name, string id, CelestialKind kind, string parent, float au, float periodDays, float radiusKm, float g, EnvironmentProfile env, string scene, float potential, Color color, string desc)
        {
            var path = $"{BodyFolder}/Body_{id.Substring(9)}.asset";
            var b = LoadOrCreate<CelestialBodyDefinition>(path, out bool created);
            if (created)
            {
                b.SetIdForTooling(id); b.displayName = name; b.kind = kind; b.parentId = parent ?? ""; b.orbitAU = au; b.orbitPeriodDays = periodDays;
                b.radiusKm = radiusKm; b.gravityG = g; b.environment = env; b.sceneName = scene ?? ""; b.archaeologicalPotential = potential;
                b.mapColor = color; b.description = desc; b.discoveredByDefault = true; b.seed = StableHash(id);
                EditorUtility.SetDirty(b);
            }
            return b;
        }

        static ArchaeologicalSiteDefinition Site(string id, string name, string body, string scene, Vector3 pos, float radius, float depth, int difficulty, int passes, float strength, int seed, string desc)
        {
            var path = $"{SiteFolder}/Site_{id.Substring(5).Replace('.', '_')}.asset";
            var s = LoadOrCreate<ArchaeologicalSiteDefinition>(path, out bool created);
            if (created)
            {
                s.SetIdForTooling(id); s.displayName = name; s.bodyId = body; s.sceneName = scene; s.position = pos; s.radius = radius; s.depth = depth;
                s.difficulty = difficulty; s.orbitalPassesNeeded = passes; s.signalStrength = strength; s.seed = seed; s.description = desc;
                s.artifactTableId = $"loot.{id.Substring(5)}";
                EditorUtility.SetDirty(s);
            }
            return s;
        }

        static int StableHash(string s) { unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return Mathf.Abs(h % 100000); } }

        static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            created = a == null;
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static GameObject FindPrefab(string folder, string nameContains)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab Mark", new[] { folder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var file = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (file.Contains(nameContains)) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        /// <summary>Smoke checks for Phase 1B data (no scenes).</summary>
        public static void Validate(Action<bool, string> check)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            check(config != null && config.galaxy != null && config.starterShip != null && config.siteRegistry != null && config.surfaceScanner != null, "1B: config wires galaxy, starter ship, site registry, surface scanner");
            if (config == null || config.galaxy == null) return;
            var sol = config.galaxy.FindSystem("sys.sol");
            check(sol != null, "1B: galaxy contains sys.sol");
            if (sol == null) return;
            var missing = SolBodyIds.Where(id => sol.FindBody(id) == null).ToList();
            check(missing.Count == 0, $"1B: all 18 Sol navigational bodies present{(missing.Count > 0 ? " (missing " + string.Join(", ", missing) + ")" : "")}");
            check(sol.FindBody(TradeHubId) != null, "1B: TradeHub Sol station present");
            var ids = sol.bodies.Where(x => x != null).Select(x => x.Id).ToList();
            check(ids.Count == ids.Distinct().Count() && ids.All(StableId.IsValidDefinitionId), "1B: body ids unique and valid");
            bool parentsOk = sol.bodies.All(x => x == null || string.IsNullOrEmpty(x.parentId) || sol.FindBody(x.parentId) != null);
            check(parentsOk, "1B: every moon/station parent resolves");
            var moon = sol.FindBody("body.sol.moon"); var mars = sol.FindBody("body.sol.mars");
            check(moon != null && moon.sceneName == SceneNames.MoonExcavation01 && moon.environment != null && Mathf.Abs(moon.environment.gravity - 1.62f) < 0.05f, "1B: Moon lands in Moon_Excavation_01 with 1.62 m/s^2");
            check(mars != null && mars.sceneName == SceneNames.MarsExcavation01 && mars.environment != null && Mathf.Abs(mars.environment.gravity - 3.71f) < 0.05f, "1B: Mars lands in Mars_Excavation_01 with 3.71 m/s^2");
            var reg = config.siteRegistry;
            var siteErrors = reg.Validate();
            check(siteErrors.Count == 0 && reg.Entries.Count >= 6, $"1B: site registry valid ({reg.Entries.Count} sites){(siteErrors.Count > 0 ? ": " + string.Join("; ", siteErrors) : "")}");
            check(reg.Entries.All(s => s == null || sol.FindBody(s.bodyId) != null), "1B: every site's body exists");
            var ship = config.starterShip;
            check(ship.fuelCapacity > 0 && ship.interiorEnvironment != null && sol.FindBody(ship.startingBodyId) != null, "1B: starter ship has fuel, interior environment and a valid start body");
            check(ship.interiorEnvironment != null && ship.interiorEnvironment.volumeProfile != null && ship.interiorEnvironment.volumeProfile.Has<Bloom>(), "1B: ship interior URP Volume profile (Bloom/Tonemapping/Color/Vignette)");
            check(ship.exteriorPrefab != null && ship.interiorPrefab != null, $"1B: ship prefabs linked (exterior={(ship.exteriorPrefab ? ship.exteriorPrefab.name : "none")}, interior={(ship.interiorPrefab ? ship.interiorPrefab.name : "none")})");

            var sky = config.defaultSpaceSkybox;
            check(sky != null && sky.shader != null && sky.shader.name == "Skybox/6 Sided" && sky.GetTexture("_FrontTex") != null, $"1B sky: default deep-space skybox ({(sky ? sky.name : "none")})");
            check(moon.orbitSkybox != null && mars.orbitSkybox != null && sol.FindBody("body.sol.earth")?.orbitSkybox != null, "1B sky: Earth/Moon/Mars orbit skyboxes assigned");

            // Rules (pure C#, in-memory save storage)
            var session = new GameSession(config, config.itemRegistry, new DMI.Xenoarch.Save.MemorySaveStorage());
            session.StartNewGame();
            var x = session.Exploration;
            check(x.Ship.currentBodyId == ship.startingBodyId && Mathf.Approximately(x.Ship.fuel, ship.fuelCapacity), "1B rules: new game starts at the start body with full fuel");
            float before = x.Ship.fuel;
            bool moved = x.CommitTravel(mars, out var why);
            check(moved && x.Ship.fuel < before && x.Ship.currentBodyId == mars.Id, $"1B rules: travel Earth->Mars spends fuel ({before - x.Ship.fuel:0.#}){(moved ? "" : " - " + why)}");
            var neptune = sol.FindBody("body.sol.neptune");
            check(!x.CanTravel(neptune, out _), "1B rules: Neptune is out of Mk I fuel range");
            int r1 = x.OrbitalScanPass(); int r2 = x.OrbitalScanPass();
            var dune = reg.Get("site.mars.dune_shrine"); var spire = reg.Get("site.mars.buried_spire");
            check(r1 > 0 && x.Site(dune.Id).revealLevel >= 2 && x.Site(spire.Id).revealLevel == 0, "1B rules: orbital passes reveal progressively (dune shrine resolved, buried spire hidden at Mk I)");
            session.Saves.Save("smoke");
            var reloaded = new GameSession(config, config.itemRegistry, session.Saves.Storage);
            reloaded.StartNewGame();
            bool read = reloaded.Saves.TryRead("smoke", out var file, out _);
            if (read) reloaded.Saves.Apply(file);
            check(read && reloaded.Exploration.Ship.currentBodyId == mars.Id && Mathf.Approximately(reloaded.Exploration.Ship.fuel, x.Ship.fuel) && reloaded.Exploration.Site(dune.Id).revealLevel == x.Site(dune.Id).revealLevel,
                "1B rules: ship position, fuel and site reveal survive save/load");
        }
    }
}
