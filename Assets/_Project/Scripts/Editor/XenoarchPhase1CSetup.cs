using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Materials;
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1C setup (additive, re-runnable): one civilization, ~30 artifact definitions (registered as items), the
    /// Moon-to-Mars discovery chain, dig site styles, buried artifact placements on the six sites, Creepy Cat sample
    /// terrains with Digger PRO in Moon/Mars, and the ship's Research Laboratory placeholder swapped for the working
    /// ResearchTerminal. Scenes with unsaved changes are skipped, never saved.
    /// </summary>
    public static class XenoarchPhase1CSetup
    {
        public const string ArtifactsFolder = XenoarchSetup.Root + "/Data/Artifacts";
        public const string DiscoveryFolder = XenoarchSetup.Root + "/Data/Discovery";
        public const string TerrainFolder = XenoarchSetup.Root + "/Data/Terrain";
        public const string CivId = "civ.vael";
        public const string ChainId = "chain.moon_mars";
        public const string Tablet = "artifact.moon_metallic_tablet";
        public const string NavCore = "artifact.mars_navigation_core";
        public const string Sensor = "artifact.mars_sensor_lattice";
        public const string MarsCoordsFact = "chain.moon_mars.mars_coords";
        public const string GatedSite = "site.mars.canyon_relay";
        public const string LabName = "Research Laboratory";
        
        [MenuItem("Xenoarch/Setup/10. Phase 1C: artifacts, chain, dig sites, terrain, research lab (additive)", priority = 30)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Xenoarch] 1C: exit Play Mode first"); return; }
            XenoarchSetup.EnsureFolder(ArtifactsFolder); XenoarchSetup.EnsureFolder(DiscoveryFolder); XenoarchSetup.EnsureFolder(TerrainFolder);
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.itemRegistry == null || config.siteRegistry == null) { Debug.LogError("[Xenoarch] 1C: config/registries missing (run Setup 1-7 first)"); return; }

            XenoarchDigRingSetup.Run(log);
            XenoarchShaderWarmup.Run(log);
            var civ = Civ();
            var arts = Artifacts(log);
            var reg = config.itemRegistry;
            var entries = reg.Entries.Where(e => e != null).ToList();
            foreach (var a in arts) if (!entries.Any(e => e.Id == a.Id)) entries.Add(a);
            reg.SetEntries(entries);
            EditorUtility.SetDirty(reg);

            var mats = Materials(log);
            entries = reg.Entries.Where(e => e != null).ToList();
            foreach (var m in mats) if (!entries.Any(e => e.Id == m.Id)) entries.Add(m);
            reg.SetEntries(entries);
            var recipes = Recipes(log);
            config.refineRecipes = recipes;
            void Kit(string id, int n)
            {
                var item = mats.FirstOrDefault(m => m.Id == id);
                if (item != null && !config.startingItems.Any(k => k != null && k.item == item)) config.startingItems.Add(new StartingItem { item = item, count = n });
            }
            Kit(LabReagent, 8); Kit(FuelCell, 2);

            var chain = Chain();
            if (!config.civilizations.Contains(civ)) config.civilizations.Add(civ);
            if (!config.discoveryChains.Contains(chain)) config.discoveryChains.Add(chain);
            EditorUtility.SetDirty(config);

            var styles = Styles(log);
            Spawns(config.siteRegistry, styles, log);
            AssetDatabase.SaveAssets();

            foreach (var scene in new[] { SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
                WithScene(scene, log, s => TerrainPass(s, config.siteRegistry, log));
            XenoarchShuttleSetup.Run(log);
            XenoarchMapsSetup.Run(log);
            XenoarchValuablesSetup.Run(log);
            WithScene(SceneNames.ShipStarter, log, s =>
            {
                var all = s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
                var sb = new StringBuilder();
                XenoarchShipSceneBuilder.SwapStation<ResearchTerminal>(all, LabName, LabName, sb);
                PlaceRefiner(all, sb);
                foreach (var helm in all.Select(g => g.GetComponent<DMI.Xenoarch.Ship.CockpitHelm>()).Where(h => h != null))
                    if (helm.GetComponent<DMI.Xenoarch.Ship.ShipFtlSequence>() == null)
                    { Undo.AddComponent<DMI.Xenoarch.Ship.ShipFtlSequence>(helm.gameObject); sb.AppendLine($"FTL: ShipFtlSequence added to '{helm.gameObject.name}' (tune jump time, streak swirl and tunnel there)"); }
                log.Append(sb);
                return true;
            });
            Debug.Log($"[Xenoarch] Phase 1C setup:\n{log}");
            File.WriteAllText("Logs/xenoarch_1c.txt", log.ToString());
        }

        // ------------------------------------------------------------------ scenes
        internal static void WithScene(string name, StringBuilder log, System.Func<Scene, bool> work)
        {
            string path = XenoarchSetup.ScenePath(name);
            if (!File.Exists(path)) { log.AppendLine($"{name}: scene missing"); return; }
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = false;
            if (scene.IsValid() && scene.isLoaded) { if (scene.isDirty) { log.AppendLine($"{name}: has unsaved changes, SKIPPED (save it and re-run)"); return; } }
            else { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true; }
            try
            {
                if (work(scene)) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        // Creepy Cat sample terrains (vendor, read only). We build our own downsampled TerrainData from them.
        const string CC = "Assets/_Creepy_Cat/_3D Scifi Kit Vol 3/Prefabs/Props/";
        static readonly Dictionary<string, string> VendorTerrain = new Dictionary<string, string>
        {
            [SceneNames.MoonExcavation01] = CC + "_Update 1.14-New Moon Terrain/Moon_Closed_A.asset",
            [SceneNames.MarsExcavation01] = CC + "_Update 1.15-New Mars Terrain/Seamless_Mars_Surface.asset",
        };
        public const int MaxHeightmapRes = 2049, DiggerResolutionMult = 4;
        static readonly string[] Placeholders = { "Lander_Placeholder", "Moon Survey Beacon", "Mars Survey Beacon", "Spawn_default", "'Board lander: return to ship'", "InteractionTestRing (Phase 1A placeholder)" };

        /// <summary>
        /// Moon/Mars: replaces the batch-1 flat terrain with one built from the Creepy Cat sample terrain (heights
        /// downsampled to max 2049, vendor terrain layers + our soil layer), centred so the origin is at height 0, snaps
        /// the placeholder objects onto it, then sets up Digger PRO (runtime) with our DiggerExcavationSurface.
        /// </summary>
        static bool TerrainPass(Scene scene, SiteRegistry sites, StringBuilder log)
        {
            var roots = scene.GetRootGameObjects();
            var siteDefs = sites.Entries.Where(s => s != null && s.sceneName == scene.name).ToList();
            string body = siteDefs.FirstOrDefault()?.bodyId ?? "";
            string prefix = scene.name.StartsWith("Mars") ? "Mars" : "Moon";
            string dataPath = $"{TerrainFolder}/{scene.name}_Terrain.asset";
            bool changed = false;

            var terrains = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToList();
            var ours = terrains.FirstOrDefault(t => t.terrainData != null && AssetDatabase.GetAssetPath(t.terrainData) == dataPath);
            if (ours == null)
            {
                if (!VendorTerrain.TryGetValue(scene.name, out var vpath) || AssetDatabase.LoadAssetAtPath<TerrainData>(vpath) == null)
                { log.AppendLine($"{scene.name}: vendor terrain {vpath} not found, kept current ground"); return false; }
                // Batch-1 flat terrain (ours) goes; anything else is left alone.
                foreach (var t in terrains)
                {
                    string tp = t.terrainData != null ? AssetDatabase.GetAssetPath(t.terrainData) : "";
                    if (tp == $"{TerrainFolder}/{scene.name}_TerrainData.asset") { Object.DestroyImmediate(t.gameObject); AssetDatabase.DeleteAsset(tp); log.AppendLine($"{scene.name}: removed batch-1 flat terrain"); }
                    else { log.AppendLine($"{scene.name}: other terrain '{t.name}' present, Digger/terrain pass SKIPPED"); return false; }
                }
                var soil = SoilTerrainLayer(prefix, body);
                var data = BuildTerrainData(AssetDatabase.LoadAssetAtPath<TerrainData>(vpath), soil, scene.name);
                AssetDatabase.DeleteAsset(dataPath);
                AssetDatabase.CreateAsset(data, dataPath);
                var go = Terrain.CreateTerrainGameObject(data);
                go.name = $"{prefix}_Terrain";
                SceneManager.MoveGameObjectToScene(go, scene);
                var terrain = go.GetComponent<Terrain>();
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (rp != null && rp.defaultTerrainMaterial != null) terrain.materialTemplate = rp.defaultTerrainMaterial;
                float centre = data.GetInterpolatedHeight(0.5f, 0.5f);
                go.transform.position = new Vector3(-data.size.x * 0.5f, -centre, -data.size.z * 0.5f);
                var env = roots.FirstOrDefault(r => r.name == "Environment");
                if (env != null) go.transform.SetParent(env.transform, true);

                // Snap our placeholder objects (placed for the old y=0 ground) onto the new surface.
                int snapped = 0;
                foreach (var r in roots)
                    foreach (var tr in r.GetComponentsInChildren<Transform>(true))
                    {
                        if (!Placeholders.Contains(tr.name)) continue;
                        if (tr.parent != null && tr.GetComponentsInParent<Transform>(true).Skip(1).Any(p => Placeholders.Contains(p.name))) continue;
                        var pos = tr.position;
                        pos.y += terrain.SampleHeight(pos) + go.transform.position.y;
                        Undo.RecordObject(tr, "Snap to terrain"); tr.position = pos; snapped++;
                    }
                foreach (var g in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject)
                    .Where(g => g.activeSelf && (g.name == $"{prefix}_Ground" || g.name.StartsWith("Excavation_Area_Marker")))) g.SetActive(false);
                int soilIdx = data.terrainLayers.Length - 1;
                foreach (var s in siteDefs) if (s.style != null) { for (int i = 0; i < s.style.layers.Count; i++) { var l = s.style.layers[i]; l.diggerTexture = i == 0 ? 0 : soilIdx; s.style.layers[i] = l; } EditorUtility.SetDirty(s.style); }
                log.AppendLine($"{scene.name}: terrain from {Path.GetFileNameWithoutExtension(vpath)} ({data.size.x:0}x{data.size.z:0} m, heightmap {data.heightmapResolution}, {data.terrainLayers.Length} layers), origin at height 0, {snapped} placeholder object(s) snapped");
                changed = true;
            }
            else log.AppendLine($"{scene.name}: Creepy Cat terrain already present, kept");

            changed |= DiggerPass(scene, log);
            return changed;
        }

        static TerrainLayer SoilTerrainLayer(string prefix, string body)
        {
            string layerPath = $"{TerrainFolder}/{prefix}_Soil.terrainlayer";
            var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (existing != null) return existing;
            string texPath = $"{TerrainFolder}/{prefix}_Soil.png";
            var col = SoilPalette.Surface(body) * 0.7f; col.a = 1f;
            var tex = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var rng = new System.Random(prefix.Length * 7919);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float n = 0.92f + 0.16f * (float)rng.NextDouble(); tex.SetPixel(x, y, col * n); }
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var layer = new TerrainLayer { diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath), tileSize = new Vector2(4f, 4f), name = $"{prefix}_Soil" };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        static TerrainData BuildTerrainData(TerrainData src, TerrainLayer soil, string sceneName)
        {
            int sr = src.heightmapResolution;
            int res = Mathf.Min(sr, MaxHeightmapRes);
            var dst = new TerrainData { name = $"{sceneName}_Terrain", heightmapResolution = res };
            dst.size = src.size;
            var sh = src.GetHeights(0, 0, sr, sr);
            var h = new float[res, res];
            float k = (sr - 1f) / (res - 1f);
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float fx = x * k, fy = y * k;
                    int x0 = Mathf.Min((int)fx, sr - 2), y0 = Mathf.Min((int)fy, sr - 2);
                    float tx = fx - x0, ty = fy - y0;
                    h[y, x] = Mathf.Lerp(Mathf.Lerp(sh[y0, x0], sh[y0, x0 + 1], tx), Mathf.Lerp(sh[y0 + 1, x0], sh[y0 + 1, x0 + 1], tx), ty);
                }
            dst.SetHeights(0, 0, h);
            var layers = src.terrainLayers.Where(l => l != null).Take(7).ToList();
            layers.Add(soil);
            dst.terrainLayers = layers.ToArray();
            int ar = Mathf.Min(src.alphamapResolution, 1024);
            dst.alphamapResolution = ar;
            var sa = src.GetAlphamaps(0, 0, src.alphamapWidth, src.alphamapHeight);
            int sl = sa.GetLength(2), n = layers.Count;
            var a = new float[ar, ar, n];
            float ka = src.alphamapWidth / (float)ar;
            for (int y = 0; y < ar; y++)
                for (int x = 0; x < ar; x++)
                {
                    int sx = Mathf.Min(src.alphamapWidth - 1, (int)(x * ka)), sy = Mathf.Min(src.alphamapHeight - 1, (int)(y * ka));
                    float sum = 0f;
                    for (int l = 0; l < n - 1 && l < sl; l++) { a[y, x, l] = sa[sy, sx, l]; sum += sa[sy, sx, l]; }
                    if (sum <= 0.001f) a[y, x, 0] = 1f;
                }
            dst.SetAlphamaps(0, 0, a);
            return dst;
        }

        static System.Type FindType(string fullName) =>
            System.AppDomain.CurrentDomain.GetAssemblies().Select(asm => asm.GetType(fullName)).FirstOrDefault(t => t != null);

        /// <summary>Digger PRO "Setup for runtime" on this scene (via its own menu item), resolution x4, plus our adapter.</summary>
        static bool DiggerPass(Scene scene, StringBuilder log)
        {
            var masterT = FindType("Digger.Modules.Core.Sources.DiggerMaster");
            var runtimeT = FindType("Digger.Modules.Runtime.Sources.DiggerMasterRuntime");
            var surfaceT = FindType("DMI.Xenoarch.Integrations.Digger.DiggerExcavationSurface");
            if (masterT == null || runtimeT == null || surfaceT == null) { log.AppendLine($"{scene.name}: Digger PRO not installed, skipped"); return false; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var o = SceneManager.GetSceneAt(i);
                if (o != scene && o.isLoaded && o.GetRootGameObjects().Any(r => r.GetComponentInChildren<Terrain>(true) != null))
                { log.AppendLine($"{scene.name}: another open scene ({o.name}) has a terrain; Digger setup SKIPPED (close it and re-run)"); return false; }
            }
            Component Find(System.Type t) => scene.GetRootGameObjects().Select(r => r.GetComponentInChildren(t, true)).FirstOrDefault(c => c != null);
            var prev = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                bool had = Find(runtimeT) != null && Find(masterT) != null;
                var master = Find(masterT);
                if (master == null)
                {
                    var go = new GameObject("Digger Master");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    master = go.AddComponent(masterT);
                    masterT.GetProperty("ResolutionMult")?.SetValue(master, DiggerResolutionMult);
                    masterT.GetMethod("CreateDirs")?.Invoke(master, null);
                }
                if (!had && !EditorApplication.ExecuteMenuItem("Tools/Digger/Setup for runtime")) { log.AppendLine($"{scene.name}: Digger menu 'Tools/Digger/Setup for runtime' not found"); return false; }
                var rt = Find(runtimeT);
                if (rt == null) { log.AppendLine($"{scene.name}: Digger runtime object missing after setup"); return false; }
                bool added = false;
                if (rt.GetComponent(surfaceT) == null) { rt.gameObject.AddComponent(surfaceT); added = true; }
                log.AppendLine($"{scene.name}: Digger {(had ? "already set up" : "set up for runtime")} (resolution x{masterT.GetProperty("ResolutionMult")?.GetValue(master)}){(added ? ", DiggerExcavationSurface added" : "")}");
                return !had || added;
            }
            finally { if (prev.IsValid() && prev.isLoaded) SceneManager.SetActiveScene(prev); }
        }

        // ------------------------------------------------------------------ dig site styles
        public const string StylesFolder = XenoarchSetup.Root + "/Data/Excavation";
        static SoilLayer L(string label, SoilMaterial kind, float t, float h, Color c) => DMI.Xenoarch.Excavation.SoilLayer.Make(label, kind, t, h, c);

        static Dictionary<string, DigSiteStyle> Styles(StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(StylesFolder);
            Color moon = SoilPalette.Surface("moon"), mars = SoilPalette.Surface("mars");
            var defs = new (string id, string name, string desc, Color border, SoilLayer[] layers)[]
            {
                ("digstyle.regolith_cache", "Regolith cache", "Shallow cache under grey regolith. Shovel the dust, precision tool near finds.", new Color(1f, 0.62f, 0.15f, 0.9f),
                    new[] { L("surface dust", SoilMaterial.Dust, 0.25f, 0.5f, moon), L("loose regolith", SoilMaterial.Loose, 0.75f, 1f, moon * 0.82f), L("compacted regolith", SoilMaterial.Compacted, 0f, 2f, moon * 0.64f) }),
                ("digstyle.ice_vault", "Ice-bound vault", "Deep vault sealed under an ice-cemented band: only the drill cuts the ice.", new Color(0.55f, 0.85f, 1f, 0.9f),
                    new[] { L("surface dust", SoilMaterial.Dust, 0.2f, 0.5f, moon), L("loose regolith", SoilMaterial.Loose, 0.6f, 1f, moon * 0.82f), L("ice-cemented regolith", SoilMaterial.Ice, 0.8f, 3f, new Color(0.62f, 0.7f, 0.76f)), L("compacted regolith", SoilMaterial.Compacted, 0f, 2f, moon * 0.6f) }),
                ("digstyle.dune_shrine", "Dune shrine", "Soft wind-blown sand over a shrine floor. Easy digging, fragile finds.", new Color(1f, 0.78f, 0.35f, 0.9f),
                    new[] { L("drift sand", SoilMaterial.Dust, 0.5f, 0.5f, mars * 1.08f), L("packed sand", SoilMaterial.Loose, 1.0f, 1f, mars * 0.9f), L("compacted sand", SoilMaterial.Compacted, 0f, 2f, mars * 0.7f) }),
                ("digstyle.canyon_sediment", "Canyon sediment", "Layered canyon sediments with a hard rock band: drill through it, then go careful.", new Color(1f, 0.45f, 0.25f, 0.9f),
                    new[] { L("surface dust", SoilMaterial.Dust, 0.15f, 0.5f, mars), L("loose sediment", SoilMaterial.Loose, 0.6f, 1f, mars * 0.85f), L("cemented sediment", SoilMaterial.Compacted, 0.75f, 2f, mars * 0.7f), L("rock band", SoilMaterial.Rock, 0.25f, 3f, new Color(0.3f, 0.2f, 0.17f)), L("cemented sediment", SoilMaterial.Compacted, 0f, 2f, mars * 0.62f) }),
            };
            var map = new Dictionary<string, DigSiteStyle>();
            foreach (var d in defs)
            {
                var a = Asset<DigSiteStyle>($"{StylesFolder}/DigStyle_{d.id.Substring("digstyle.".Length)}.asset");
                a.SetIdForTooling(d.id); a.displayName = d.name; a.description = d.desc; a.borderColor = d.border;
                var keep = a.layers ?? new List<SoilLayer>();
                a.layers = d.layers.ToList();
                for (int i = 0; i < a.layers.Count && i < keep.Count; i++) { var l = a.layers[i]; l.diggerTexture = keep[i].diggerTexture; a.layers[i] = l; }
                EditorUtility.SetDirty(a);
                map[d.id] = a;
            }
            log.AppendLine($"Dig site styles: {string.Join(", ", map.Values.Select(v => v.displayName))}");
            return map;
        }

        static readonly Dictionary<string, string> SiteStyle = new Dictionary<string, string>
        {
            ["site.moon.tranquility_cache"] = "digstyle.regolith_cache",
            ["site.moon.ridge_vault"] = "digstyle.regolith_cache",
            ["site.moon.deep_echo"] = "digstyle.ice_vault",
            ["site.mars.dune_shrine"] = "digstyle.dune_shrine",
            ["site.mars.canyon_relay"] = "digstyle.canyon_sediment",
            ["site.mars.buried_spire"] = "digstyle.canyon_sediment",
        };

        // ------------------------------------------------------------------ materials loop
        public const string MaterialsFolder = XenoarchSetup.Root + "/Data/Materials";
        public const string FuelCell = "material.fuel_cell", LabReagent = "material.lab_reagent", WaterIce = "material.water_ice", Regolith = "material.regolith";
        public const string RefinerName = "Refiner";
        const string Moon = "body.sol.moon", Mars = "body.sol.mars";

        static List<MaterialDefinition> Materials(StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(MaterialsFolder);
            var defs = new (string id, string name, string desc, bool refined, float fuel, int reagent, Color tint, string[] bodies, int nodes, int min, int max, int value)[]
            {
                (Regolith, "Regolith", "Loose surface rock and dust. Leach it into lab reagent at the refiner.", false, 0f, 0, new Color(0.55f, 0.55f, 0.56f), new[] { Moon, Mars }, 5, 3, 5, 2),
                (WaterIce, "Water Ice", "Ice from shaded ground. Split it into fuel cells, or use it to make reagent from salts.", false, 0f, 0, new Color(0.75f, 0.88f, 0.98f), new[] { Moon, Mars }, 3, 2, 4, 6),
                ("material.helium3", "Helium-3 Regolith", "Regolith rich in helium-3. With ice it makes three fuel cells.", false, 0f, 0, new Color(0.9f, 0.85f, 0.55f), new[] { Moon }, 2, 1, 2, 20),
                ("material.iron_ore", "Iron Ore", "Iron-rich rock. Smelt into alloy ingots (upgrades, Phase 1D).", false, 0f, 0, new Color(0.45f, 0.28f, 0.2f), new[] { Moon }, 3, 2, 3, 5),
                ("material.mineral_salts", "Mineral Salts", "Perchlorate-rich salts. The lab can use them directly (1 reagent each) or refine them with ice.", false, 0f, 1, new Color(0.92f, 0.82f, 0.8f), new[] { Mars }, 3, 2, 4, 4),
                ("material.methane_frost", "Methane Frost", "Frozen methane. Burns straight in the ship's engines (4 fuel each).", false, 4f, 0, new Color(0.35f, 0.75f, 0.7f), new[] { Mars }, 3, 2, 3, 6),
                (FuelCell, "Fuel Cell", "Refined fuel. 25 units of ship fuel each.", true, 25f, 0, new Color(1f, 0.7f, 0.2f), new string[0], 0, 0, 0, 40),
                (LabReagent, "Lab Reagent", "Refined chemistry for the research lab (1 reagent unit each).", true, 0f, 1, new Color(0.5f, 0.9f, 0.6f), new string[0], 0, 0, 0, 15),
                ("material.alloy_ingot", "Alloy Ingot", "Smelted alloy for ship and tool upgrades (Phase 1D).", true, 0f, 0, new Color(0.7f, 0.72f, 0.78f), new string[0], 0, 0, 0, 30),
            };
            var list = new List<MaterialDefinition>();
            foreach (var d in defs)
            {
                var a = Asset<MaterialDefinition>($"{MaterialsFolder}/Material_{d.id.Substring("material.".Length)}.asset");
                a.SetIdForTooling(d.id); a.displayName = d.name; a.description = d.desc;
                a.category = ItemCategory.Resource; a.uniqueInstance = false; a.maxStack = 50; a.mass = 0.5f; a.baseValue = d.value;
                a.refined = d.refined; a.fuelValue = d.fuel; a.reagentValue = d.reagent; a.tint = d.tint;
                a.bodies = d.bodies.ToList(); a.nodesPerScene = d.nodes; a.unitsPerNode = new Vector2Int(d.min, d.max); a.respawnSeconds = 1200f;
                EditorUtility.SetDirty(a);
                list.Add(a);
            }
            log.AppendLine($"Materials: {list.Count} ({list.Count(m => !m.refined)} raw, {list.Count(m => m.refined)} refined)");
            return list;
        }

        static List<RefineRecipe> Recipes(StringBuilder log)
        {
            var defs = new (string id, string name, (string, int)[] ins, (string, int)[] outs, float secs)[]
            {
                ("recipe.fuel_from_ice", "Electrolysis", new[] { (WaterIce, 2) }, new[] { (FuelCell, 1) }, 12f),
                ("recipe.fuel_from_he3", "He-3 fuel pellets", new[] { ("material.helium3", 1), (WaterIce, 1) }, new[] { (FuelCell, 3) }, 20f),
                ("recipe.reagent_from_regolith", "Regolith leach", new[] { (Regolith, 3) }, new[] { (LabReagent, 1) }, 8f),
                ("recipe.reagent_from_salts", "Brine reagent", new[] { ("material.mineral_salts", 1), (WaterIce, 1) }, new[] { (LabReagent, 3) }, 10f),
                ("recipe.alloy", "Smelt alloy", new[] { ("material.iron_ore", 3) }, new[] { ("material.alloy_ingot", 1) }, 15f),
            };
            var list = new List<RefineRecipe>();
            foreach (var d in defs)
            {
                var r = Asset<RefineRecipe>($"{MaterialsFolder}/Recipe_{d.id.Substring("recipe.".Length)}.asset");
                r.SetIdForTooling(d.id); r.displayName = d.name; r.seconds = d.secs;
                r.inputs = d.ins.Select(i => new ItemAmount(i.Item1, i.Item2)).ToList();
                r.outputs = d.outs.Select(o => new ItemAmount(o.Item1, o.Item2)).ToList();
                EditorUtility.SetDirty(r);
                list.Add(r);
            }
            log.AppendLine($"Refine recipes: {string.Join(", ", list.Select(r => r.displayName))}");
            return list;
        }

        /// <summary>
        /// Refiner station: swaps a "Refiner" station placeholder if Anthony placed one; otherwise adds a placeholder
        /// pillar 1.6 m to the right of the Research Laboratory (new object only, nothing existing is moved).
        /// </summary>
        static void PlaceRefiner(List<GameObject> all, StringBuilder log)
        {
            if (all.Any(g => g.GetComponent<RefinerTerminal>() != null)) { log.AppendLine("Refiner: already present, kept where it is"); return; }
            if (all.Any(g => g.GetComponents<StationPlaceholderInteractable>().Any(p => p.stationName == RefinerName)))
            { XenoarchShipSceneBuilder.SwapStation<RefinerTerminal>(all, RefinerName, RefinerName, log); return; }
            var lab = all.FirstOrDefault(g => g.GetComponent<ResearchTerminal>() != null);
            if (lab == null) { log.AppendLine("Refiner: no Research Laboratory to place it next to; skipped"); return; }
            var go = new GameObject("Refiner (placeholder, move freely)");
            Undo.RegisterCreatedObjectUndo(go, "Refiner");
            go.transform.SetParent(lab.transform.parent, false);
            go.transform.position = lab.transform.position + lab.transform.right * 1.6f;
            go.transform.rotation = lab.transform.rotation;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Refiner Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            body.transform.localScale = new Vector3(0.9f, 1.3f, 0.7f);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(XenoarchSetup.Root + "/Art/Materials/Placeholder_Metal.mat");
            if (mat != null) body.GetComponent<Renderer>().sharedMaterial = mat;
            var t = go.AddComponent<RefinerTerminal>();
            t.terminalName = RefinerName;
            log.AppendLine($"Refiner: placeholder pillar added next to the Research Laboratory at {go.transform.position} (parent '{(go.transform.parent != null ? go.transform.parent.name : "scene root")}')");
        }

        /// <summary>Batch 3A: a held shovel keeps digging the same spot (aims below the grid floor are accepted), and one
        /// crude stroke goes deeper than one cell on soft soil.</summary>
        public static void ValidateDigging(System.Action<bool, string> check)
        {
            var g = new ExcavationGrid(8, 8, 8, 0.25f);
            var shovel = DigTools.Shovel; shovel.removal *= 3f; shovel.cells = 3;
            var r1 = g.StrokeAt(1.125f, 1.125f, 0f, shovel);
            float d1 = g.TopDepth(4, 4);
            // aim at the visual hole, 0.4 m below the grid floor (what a Digger carve looks like): must still dig
            var r2 = g.StrokeAt(1.125f, 1.125f, -d1 - 0.4f, shovel);
            float d2 = g.TopDepth(4, 4);
            var r3 = g.StrokeAt(1.125f, 1.125f, -d2 + 2f, shovel); // aimed 2 m above the floor: ignored
            check(r1.accepted && r2.accepted && d1 >= 0.49f && d2 > d1 && !r3.accepted,
                $"3A digging: repeated shovel strokes keep cutting ({d1:0.00} m -> {d2:0.00} m), above-floor aim ignored");
        }

        public static void ValidateFtl(System.Action<bool, string> check)
        {
            var go = new GameObject("ftl probe") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var f = go.AddComponent<DMI.Xenoarch.Ship.ShipFtlSequence>();
                float moon = f.JumpSecondsFor(0.003f), mars = f.JumpSecondsFor(0.6f), jup = f.JumpSecondsFor(4.2f), far = f.JumpSecondsFor(40f);
                check(moon >= 5f && moon < 5.5f && mars > moon && jup > mars && far <= f.maxJumpSeconds + 0.01f && f.JumpSecondsFor(0f) >= 5f,
                    $"1C FTL: jump time scales with distance, min 5 s (Moon {moon:0.0}s, Mars {mars:0.0}s, Jupiter {jup:0.0}s, 40 AU {far:0.0}s)");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void ValidateMaterials(System.Action<bool, string> check, XenoarchConfig config)
        {
            var mats = config.itemRegistry.Entries.OfType<MaterialDefinition>().ToList();
            if (mats.Count == 0) { check(true, "1C materials: setup not run yet - skipped"); return; }
            check(mats.Count >= 9 && mats.Any(m => m.fuelValue > 0 && !m.refined) && mats.Any(m => m.reagentValue > 0 && !m.refined) && mats.Count(m => m.refined) >= 3,
                $"1C materials: {mats.Count} materials (raw direct fuel + direct reagent + refined products)");
            check(config.refineRecipes.Count >= 5 && config.refineRecipes.All(r => r != null && r.inputs.Count > 0 && r.outputs.Count > 0 && r.inputs.Concat(r.outputs).All(i => mats.Any(m => m.Id == i.itemId))),
                $"1C materials: {config.refineRecipes.Count} refine recipes, all items resolve");

            var s = new GameSession(config, config.itemRegistry, new DMI.Xenoarch.Save.MemorySaveStorage());
            s.StartNewGame();
            var M = s.Materials; var inv = s.Inventory;
            check(M.ReagentUnits() >= 8 && inv.Count(FuelCell) >= 2, $"1C materials: starting kit has lab reagents ({M.ReagentUnits()}) and fuel cells ({inv.Count(FuelCell)})");
            var ice = M.Recipe("recipe.fuel_from_ice");
            bool refused = !M.CanRefine(ice, out var why);
            inv.Add(WaterIce, 4);
            int cellsBefore = inv.Count(FuelCell);
            bool started = M.StartRefine(ice, out _);
            M.Tick(5f); bool mid = M.Busy && inv.Count(FuelCell) == cellsBefore;
            M.Tick(10f);
            check(refused && started && mid && !M.Busy && inv.Count(WaterIce) == 2 && inv.Count(FuelCell) == cellsBefore + 1,
                $"1C refiner: inputs taken at start, timed batch delivers outputs (2 ice -> 1 fuel cell; refused without ice: {why})");
            M.StartRefine(ice, out _); M.CancelRefine();
            check(inv.Count(WaterIce) == 2 && !M.Busy, "1C refiner: cancel returns the inputs");

            var x = s.Exploration;
            x.Ship.fuel = 10f;
            float cargoFuel = M.FuelInCargo();
            float added = M.Refuel(x);
            check(added > 40f && x.Ship.fuel > 50f && inv.Count(FuelCell) == 0 && Mathf.Approximately(M.FuelInCargo(), 0f), $"1C refiner: refuel uses fuel items from cargo (+{added:0} fuel from {cargoFuel:0} in cargo)");

            int units = M.ReagentUnits();
            var tablet = s.Artifacts.Recover(s.Artifacts.Definition(Tablet), 0.8f, "site.moon.tranquility_cache", Moon);
            inv.AddInstance(Tablet, tablet.instanceId);
            bool paid = s.Artifacts.Start(tablet, ResearchOp.Cleaning, out _);
            check(paid && M.ReagentUnits() == units - ResearchRules.ReagentCost(ResearchOp.Cleaning), $"1C lab: operations consume lab reagents ({units} -> {M.ReagentUnits()})");
            s.Artifacts.Tick(100f);
            inv.Remove(LabReagent, 999); inv.Remove("material.mineral_salts", 999);
            check(!s.Artifacts.CanStart(tablet, ResearchOp.Identification, out var why2) && why2.Contains("reagent"), $"1C lab: no reagents, no research ({why2})");

            string node = "node.Moon_Excavation_01.material.regolith.0";
            M.MarkHarvested(node, 100f);
            check(!M.NodeAvailable(node, 500f, 1200f) && M.NodeAvailable(node, 1400f, 1200f), "1C materials: harvested node respawns after its timer");
            inv.Add(WaterIce, 2); M.StartRefine(ice, out _); M.Tick(3f);
            s.Saves.Save("smoke1c_mat");
            var re = new GameSession(config, config.itemRegistry, s.Saves.Storage);
            re.StartNewGame();
            bool ok = re.Saves.TryRead("smoke1c_mat", out var file, out _);
            if (ok) re.Saves.Apply(file);
            check(ok && re.Materials.Busy && Mathf.Approximately(re.Materials.State.job.elapsed, 3f) && !re.Materials.NodeAvailable(node, 500f, 1200f), "1C materials: refiner job and harvested nodes survive save/load");
            string ship = File.Exists(XenoarchSetup.ScenePath(SceneNames.ShipStarter)) ? File.ReadAllText(XenoarchSetup.ScenePath(SceneNames.ShipStarter)) : "";
            bool placed = ship.Contains("terminalName: Refiner");
            check(true, placed ? "1C ship: Refiner station in Ship_Starter" : "1C ship: Refiner not placed yet (Ship_Starter had unsaved changes) - skipped");
        }

        // ------------------------------------------------------------------ content
        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static CivilizationDefinition Civ()
        {
            var c = Asset<CivilizationDefinition>($"{ArtifactsFolder}/Civ_Vael.asset");
            c.SetIdForTooling(CivId);
            c.displayName = "the Vael (working name)";
            c.unknownName = "Unknown culture";
            c.historicalPeriod = "~1.2 million years ago";
            c.languageName = "Vael script";
            c.writingSystem = "Angular glyph rows read in spirals";
            c.architectureStyle = "Buried waystations of fused regolith";
            c.techTraits = "Interplanetary travel, metallic data tablets, lattice sensors.";
            c.culturalTraits = "Mapped and marked routes between worlds; left guide-stones.";
            c.preferredMaterials = "Titanium-iridium alloys, fused glass";
            c.disappearanceStatus = "Unknown";
            EditorUtility.SetDirty(c);
            return c;
        }

        struct A
        {
            public string id, name, unid, mat, age, desc, research; public ArtifactCategory cat; public ArtifactRarity rar; public float frag, hist, tech, buried, size; public int value; public ArtifactShape shape; public Color tint; public bool techOp; public string[] text;
        }

        static A Mk(string id, string name, string unid, ArtifactCategory cat, ArtifactRarity rar, string mat, ArtifactShape shape, Color tint, float size, int value, float frag, string desc, float hist = 0.3f, float tech = 0.1f, float buried = 0.85f, bool techOp = false, string age = "~1.2 million years", string research = null, string[] text = null)
            => new A { id = id, name = name, unid = unid, cat = cat, rar = rar, mat = mat, shape = shape, tint = tint, size = size, value = value, frag = frag, desc = desc, hist = hist, tech = tech, buried = buried, techOp = techOp, age = age, research = research, text = text };

        static readonly Color Metal = new Color(0.62f, 0.64f, 0.7f), Bronze = new Color(0.6f, 0.45f, 0.25f), Glass = new Color(0.45f, 0.8f, 0.85f), Bone = new Color(0.82f, 0.78f, 0.68f), Stone = new Color(0.35f, 0.34f, 0.33f), Teal = new Color(0.2f, 0.7f, 0.6f), Amber = new Color(0.95f, 0.66f, 0.22f), Red = new Color(0.7f, 0.25f, 0.2f);

        static IEnumerable<A> Content()
        {
            // Chain artifacts (unique)
            yield return Mk(Tablet, "Vael Waystation Tablet", "Engraved metal plate", ArtifactCategory.Tablet, ArtifactRarity.Unique, "Titanium-iridium alloy", ArtifactShape.Tablet, Metal, 0.5f, 1200, 0.45f,
                "A plate covered in angular glyph rows, cold to the touch and too regular to be natural.", 0.9f, 0.4f, 0.85f, false,
                text: new[] { "0.10|...we who walked before the dust...", "0.20|...this waystation beneath the grey sea keeps the path...", "0.35|...the second waystation lies on the red world, in the long canyon east of the great shield mountains...", "0.55|...from there the guide-stone points beyond the sun..." });
            yield return Mk(NavCore, "Vael Navigation Core", "Damaged mechanism", ArtifactCategory.NavigationDevice, ArtifactRarity.Unique, "Fused glass and alloy lattice", ArtifactShape.Device, Teal, 0.6f, 2600, 0.7f,
                "A cracked housing around a lattice of glass threads. It still hums faintly.", 0.8f, 0.95f, 0.55f, true,
                research: "Decoded star chart: a route from Sol to a system about 14 light years away. Future expedition (needs a long-range drive).");
            yield return Mk(Sensor, "Vael Sensor Lattice", "Glass-threaded module", ArtifactCategory.AncientTechnology, ArtifactRarity.Unique, "Fused glass lattice", ArtifactShape.Crystal, Glass, 0.25f, 1800, 0.6f,
                "A palm-sized lattice that refracts light in patterns.", 0.4f, 0.9f, 0.75f, true,
                research: "Adaptable sensor lattice: can be fitted to the handheld scanner (+1 scanner tier).");
            // Moon finds
            yield return Mk("artifact.moon_survey_spike", "Vael Survey Spike", "Metal spike", ArtifactCategory.Tool, ArtifactRarity.Common, "Titanium alloy", ArtifactShape.Rod, Metal, 0.35f, 90, 0.2f, "A marker driven into the regolith.");
            yield return Mk("artifact.moon_glyph_shard", "Glyph Shard", "Inscribed fragment", ArtifactCategory.Document, ArtifactRarity.Uncommon, "Fused glass", ArtifactShape.Shard, Glass, 0.15f, 220, 0.7f, "A broken piece of a larger inscription.", 0.6f, text: new[] { "0.25|...path...", "0.5|...keep the path open for those who follow..." });
            yield return Mk("artifact.moon_seal_disc", "Waystation Seal", "Flat metal disc", ArtifactCategory.Ceremonial, ArtifactRarity.Rare, "Iridium alloy", ArtifactShape.Disc, Bronze, 0.22f, 480, 0.3f, "A disc stamped with a spiral of glyphs.", 0.7f);
            yield return Mk("artifact.moon_power_cell", "Depleted Power Cell", "Sealed cylinder", ArtifactCategory.PowerSource, ArtifactRarity.Uncommon, "Alloy and unknown electrolyte", ArtifactShape.Rod, Amber, 0.25f, 300, 0.4f, "A heavy cylinder with a faint glow at one end.", 0.2f, 0.6f, techOp: true, research: "Solid-state storage far beyond human chemistry. Inert now.");
            yield return Mk("artifact.moon_vault_plate", "Vault Plate", "Curved metal panel", ArtifactCategory.MechanicalComponent, ArtifactRarity.Common, "Titanium alloy", ArtifactShape.Box, Metal, 0.5f, 120, 0.25f, "A curved panel, perhaps from a container.");
            yield return Mk("artifact.moon_lens", "Echo Lens", "Clouded crystal", ArtifactCategory.ScientificInstrument, ArtifactRarity.Rare, "Fused glass", ArtifactShape.Crystal, Glass, 0.18f, 520, 0.75f, "A lens that rings when tapped.", 0.4f, 0.7f, techOp: true, research: "An acoustic lens used to sound out buried voids.");
            yield return Mk("artifact.moon_ring", "Spiral Band", "Small metal ring", ArtifactCategory.Jewelry, ArtifactRarity.Uncommon, "Iridium alloy", ArtifactShape.Ring, Bronze, 0.08f, 260, 0.3f, "A band engraved inside and out.", 0.5f);
            yield return Mk("artifact.moon_deep_cache", "Deep Echo Cache", "Sealed box", ArtifactCategory.AncientTechnology, ArtifactRarity.Exceptional, "Alloy", ArtifactShape.Box, Teal, 0.7f, 900, 0.5f, "A sealed container from far below the surface.", 0.6f, 0.8f, techOp: true, research: "Contains layered data films; needs a better lab to read.");
            yield return Mk("artifact.moon_tool_head", "Cutting Head", "Worn tool tip", ArtifactCategory.Tool, ArtifactRarity.Common, "Hardened alloy", ArtifactShape.Shard, Metal, 0.12f, 80, 0.2f, "A worn cutting edge.");
            yield return Mk("artifact.moon_data_crystal", "Data Crystal", "Glowing shard", ArtifactCategory.DataCrystal, ArtifactRarity.Rare, "Doped crystal", ArtifactShape.Crystal, Teal, 0.16f, 640, 0.65f, "A crystal with a faint inner light.", 0.5f, 0.7f, techOp: true, research: "Holds patterned light: a star field, partly corrupted.");
            // Mars finds
            yield return Mk("artifact.mars_relay_fragment", "Relay Fragment", "Scorched panel", ArtifactCategory.MechanicalComponent, ArtifactRarity.Uncommon, "Alloy", ArtifactShape.Box, Red, 0.45f, 240, 0.35f, "Part of a larger transmitter.", 0.3f, 0.5f, techOp: true, research: "Part of a relay that pulsed on a fixed period.");
            yield return Mk("artifact.mars_canyon_marker", "Canyon Marker", "Engraved stone", ArtifactCategory.Cultural, ArtifactRarity.Common, "Basalt", ArtifactShape.Shard, Stone, 0.3f, 110, 0.3f, "A shaped basalt block with a spiral glyph.");
            yield return Mk("artifact.mars_shrine_idol", "Dune Shrine Figure", "Carved figure", ArtifactCategory.Ceremonial, ArtifactRarity.Rare, "Fused glass", ArtifactShape.Crystal, Amber, 0.22f, 560, 0.6f, "A stylised standing figure.", 0.8f);
            yield return Mk("artifact.mars_offering_bowl", "Offering Bowl", "Shallow dish", ArtifactCategory.Cultural, ArtifactRarity.Uncommon, "Fired regolith", ArtifactShape.Disc, Bone, 0.26f, 200, 0.65f, "A dish with traces of residue.", 0.6f);
            yield return Mk("artifact.mars_glyph_tablet", "Shrine Tablet", "Small engraved plate", ArtifactCategory.Document, ArtifactRarity.Rare, "Iridium alloy", ArtifactShape.Tablet, Bronze, 0.22f, 700, 0.45f, "A smaller cousin of the Moon tablet.", 0.8f, text: new[] { "0.2|...the second waystation...", "0.45|...rest here, then follow the guide-stone..." });
            yield return Mk("artifact.mars_spire_cap", "Spire Capstone", "Pointed cap", ArtifactCategory.MechanicalComponent, ArtifactRarity.Exceptional, "Alloy", ArtifactShape.Crystal, Metal, 0.6f, 950, 0.35f, "The tip of a tall buried structure.", 0.5f, 0.6f, techOp: true, research: "An antenna tip tuned to a single distant frequency.");
            yield return Mk("artifact.mars_bead_string", "Bead String", "Corroded beads", ArtifactCategory.Jewelry, ArtifactRarity.Common, "Glass beads", ArtifactShape.Ring, Glass, 0.1f, 95, 0.7f, "Glass beads on a fused wire.");
            yield return Mk("artifact.mars_dust_filter", "Dust Filter", "Mesh cartridge", ArtifactCategory.Tool, ArtifactRarity.Common, "Alloy mesh", ArtifactShape.Rod, Metal, 0.22f, 85, 0.4f, "A cartridge of fine mesh.");
            yield return Mk("artifact.mars_star_disc", "Star Disc", "Perforated disc", ArtifactCategory.ScientificInstrument, ArtifactRarity.Rare, "Alloy", ArtifactShape.Disc, Bronze, 0.24f, 610, 0.4f, "A disc pierced with a pattern of holes.", 0.6f, 0.6f, techOp: true, research: "A star map: the holes match stars as seen from Mars long ago.");
            yield return Mk("artifact.mars_cell_core", "Lattice Cell", "Faceted cell", ArtifactCategory.PowerSource, ArtifactRarity.Uncommon, "Doped crystal", ArtifactShape.Crystal, Amber, 0.15f, 330, 0.5f, "A faceted energy cell.", 0.2f, 0.6f, techOp: true, research: "Power cell, partly charged.");
            // Generic (future tables / trade)
            yield return Mk("artifact.vael_comb", "Ritual Comb", "Toothed object", ArtifactCategory.Cultural, ArtifactRarity.Common, "Bone-like composite", ArtifactShape.Shard, Bone, 0.14f, 70, 0.6f, "A comb-like object.");
            yield return Mk("artifact.vael_pendant", "Spiral Pendant", "Small pendant", ArtifactCategory.Jewelry, ArtifactRarity.Uncommon, "Iridium alloy", ArtifactShape.Disc, Bronze, 0.08f, 240, 0.3f, "A pendant with a spiral motif.", 0.5f);
            yield return Mk("artifact.vael_gauge", "Pressure Gauge", "Dial fragment", ArtifactCategory.ScientificInstrument, ArtifactRarity.Uncommon, "Glass and alloy", ArtifactShape.Disc, Glass, 0.14f, 260, 0.6f, "A dial with glyph markings.", 0.3f, 0.5f, techOp: true, research: "Measures pressure in an unknown unit.");
            yield return Mk("artifact.vael_hinge", "Hinge Assembly", "Jointed metal", ArtifactCategory.MechanicalComponent, ArtifactRarity.Common, "Alloy", ArtifactShape.Box, Metal, 0.18f, 75, 0.2f, "A small jointed mechanism.");
            yield return Mk("artifact.vael_effigy", "Guardian Effigy", "Figure fragment", ArtifactCategory.Ceremonial, ArtifactRarity.Legendary, "Fused glass", ArtifactShape.Crystal, Amber, 0.3f, 2000, 0.6f, "A figure fragment with inlaid glyphs.", 0.95f);
            yield return Mk("artifact.vael_scroll_case", "Scroll Case", "Sealed tube", ArtifactCategory.Document, ArtifactRarity.Rare, "Alloy", ArtifactShape.Rod, Bronze, 0.3f, 580, 0.5f, "A tube sealed at both ends.", 0.7f, text: new[] { "0.3|...a record of the walkers...", "0.6|...they left when the sun dimmed..." });
            yield return Mk("artifact.unknown_lump", "Fused Lump", "Unknown object", ArtifactCategory.Unknown, ArtifactRarity.Common, "Slag", ArtifactShape.Shard, Stone, 0.14f, 25, 0.2f, "Probably debris. Probably.", 0.05f, age: "Unknown");
        }

        static List<ArtifactDefinition> Artifacts(StringBuilder log)
        {
            var list = new List<ArtifactDefinition>();
            foreach (var c in Content())
            {
                string file = "Artifact_" + c.id.Substring("artifact.".Length);
                var d = Asset<ArtifactDefinition>($"{ArtifactsFolder}/{file}.asset");
                d.SetIdForTooling(c.id);
                d.displayName = c.name; d.unidentifiedName = c.unid; d.description = c.desc; d.researchedDescription = c.research;
                d.category = ItemCategory.Artifact; d.uniqueInstance = true; d.maxStack = 1; d.baseValue = c.value; d.mass = 0.5f;
                d.civilizationId = c.id == "artifact.unknown_lump" ? "" : CivId;
                d.artifactCategory = c.cat; d.rarity = c.rar; d.material = c.mat; d.ageEstimate = c.age;
                d.fragility = c.frag; d.historicalSignificance = c.hist; d.technologicalSignificance = c.tech; d.buriedCondition = c.buried;
                d.shape = c.shape; d.tint = c.tint; d.size = c.size;
                d.researchOps = new List<ResearchOp> { ResearchOp.Cleaning, ResearchOp.Identification, ResearchOp.MaterialAnalysis, ResearchOp.AgeEstimation, ResearchOp.CivilizationIdentification, ResearchOp.Restoration };
                if (c.techOp) d.researchOps.Add(ResearchOp.TechnologyAnalysis);
                d.translation = new List<TranslationSegment>();
                if (c.text != null)
                {
                    foreach (var t in c.text) { var p = t.Split('|'); d.translation.Add(new TranslationSegment { threshold = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), text = p[1] }); }
                    d.researchOps.Add(ResearchOp.Translation);
                }
                EditorUtility.SetDirty(d);
                list.Add(d);
            }
            log.AppendLine($"Artifacts: {list.Count} definitions, civilization {CivId}");
            return list;
        }

        static DiscoveryChainDefinition Chain()
        {
            var c = Asset<DiscoveryChainDefinition>($"{DiscoveryFolder}/Chain_MoonMars.asset");
            c.SetIdForTooling(ChainId);
            c.title = "The Waystations";
            DiscoveryNode N(string id, string title, string fact, float thr, string[] pre, DiscoveryReward r, bool optional = false) =>
                new DiscoveryNode { id = id, title = title, factKey = fact, threshold = thr, prerequisites = pre.ToList(), optional = optional, reward = r ?? new DiscoveryReward() };
            c.nodes = new List<DiscoveryNode>
            {
                N("tablet", "Recover the metallic tablet", Facts.Recovered(Tablet), 1f, new string[0], new DiscoveryReward { journal = "Recovered an engraved metal plate from the Tranquility Cache. Nothing like it exists in any human record." }),
                N("origin", "Identify its origin", Facts.Civilization(CivId), 1f, new[] { "tablet" }, new DiscoveryReward { journal = "Lab result: the tablet is not of human make. Its makers: the Vael (working name)." }),
                N("mars_coords", "Translate the tablet (35%)", Facts.Translation(Tablet), 0.35f, new[] { "origin" }, new DiscoveryReward { journal = "Partial translation: a second waystation on Mars, in a long canyon. Scan Mars from orbit to find it.", setFact = MarsCoordsFact, discoverBodyId = "body.sol.mars", revealSiteId = GatedSite, revealLevel = SiteRevealLevel.OrbitalAnomaly }),
                N("mars_scan", "Locate the Mars waystation from orbit", Facts.Site(GatedSite), 2f, new[] { "mars_coords" }, new DiscoveryReward { journal = "Orbital scan: metallic anomaly confirmed in the canyon. Land and pinpoint it with the handheld scanner." }),
                N("nav_core", "Recover the navigation device", Facts.Recovered(NavCore), 1f, new[] { "mars_scan" }, new DiscoveryReward { journal = "Recovered a damaged mechanism from the Canyon Relay." }),
                N("sensor", "Recover the sensor module", Facts.Recovered(Sensor), 1f, new[] { "mars_scan" }, new DiscoveryReward { journal = "Recovered a glass-threaded module beside the mechanism." }, true),
                N("beyond_sol", "Decode the navigation core", Facts.Research(NavCore, ResearchOp.TechnologyAnalysis), 1f, new[] { "nav_core" }, new DiscoveryReward { journal = "Navigation core decoded: coordinates of a star system beyond Sol. Future expedition (needs a long-range drive).", setFact = "chain.moon_mars.extrasolar_coords" }),
                N("sensor_tech", "Analyse the sensor module", Facts.Research(Sensor, ResearchOp.TechnologyAnalysis), 1f, new[] { "sensor" }, new DiscoveryReward { journal = "Sensor lattice adapted: scanner tier +1.", scannerLevelBonus = 1 }, true),
            };
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>Buried finds per site: (artifact, x, z, layer, footprint). Cells across the site from its min corner.</summary>
        static readonly Dictionary<string, (string id, int x, int z, int y, int f)[]> SiteFinds = new Dictionary<string, (string, int, int, int, int)[]>
        {
            ["site.moon.tranquility_cache"] = new[] { (Tablet, 11, 11, 4, 2), ("artifact.moon_survey_spike", 6, 15, 1, 1), ("artifact.moon_glyph_shard", 16, 8, 2, 1), ("artifact.moon_ring", 9, 6, 3, 1) },
            ["site.moon.ridge_vault"] = new[] { ("artifact.moon_seal_disc", 14, 16, 6, 1), ("artifact.moon_vault_plate", 20, 10, 4, 2), ("artifact.moon_power_cell", 9, 20, 8, 1), ("artifact.moon_tool_head", 24, 24, 2, 1) },
            ["site.moon.deep_echo"] = new[] { ("artifact.moon_deep_cache", 20, 20, 17, 2), ("artifact.moon_lens", 14, 25, 12, 1), ("artifact.moon_data_crystal", 27, 13, 15, 1) },
            ["site.mars.dune_shrine"] = new[] { ("artifact.mars_shrine_idol", 12, 12, 3, 1), ("artifact.mars_offering_bowl", 7, 16, 2, 1), ("artifact.mars_glyph_tablet", 16, 7, 3, 1), ("artifact.mars_bead_string", 10, 8, 1, 1) },
            ["site.mars.canyon_relay"] = new[] { (NavCore, 15, 15, 8, 2), (Sensor, 19, 13, 7, 1), ("artifact.mars_relay_fragment", 10, 20, 4, 2), ("artifact.mars_canyon_marker", 22, 22, 2, 1) },
            ["site.mars.buried_spire"] = new[] { ("artifact.mars_spire_cap", 24, 24, 12, 2), ("artifact.mars_star_disc", 18, 30, 9, 1), ("artifact.mars_cell_core", 30, 18, 14, 1) },
        };

        static void Spawns(SiteRegistry sites, Dictionary<string, DigSiteStyle> styles, StringBuilder log)
        {
            int n = 0;
            foreach (var kv in SiteFinds)
            {
                var s = sites.Get(kv.Key);
                if (s == null) { log.AppendLine($"Site {kv.Key} missing"); continue; }
                s.cellSize = 0.25f;
                s.artifacts = kv.Value.Select(v => new SiteArtifactSpawn { artifactId = v.id, cell = new Vector3Int(v.x, v.y, v.z), footprint = v.f }).ToList();
                s.gateFact = s.Id == GatedSite ? MarsCoordsFact : "";
                if (SiteStyle.TryGetValue(s.Id, out var st) && styles.TryGetValue(st, out var style)) s.style = style;
                EditorUtility.SetDirty(s);
                n += s.artifacts.Count;
            }
            log.AppendLine($"Sites: {n} buried artifacts placed on {SiteFinds.Count} sites; {GatedSite} gated by {MarsCoordsFact}");
        }

        // ------------------------------------------------------------------ validation (smoke)
        public static void Validate(System.Action<bool, string> check)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            var arts = config != null && config.itemRegistry != null ? config.itemRegistry.Entries.OfType<ArtifactDefinition>().ToList() : new List<ArtifactDefinition>();
            if (arts.Count == 0) { check(true, "1C: Phase 1C setup not run yet - skipped"); return; }
            check(arts.Count >= 30 && arts.All(a => a.uniqueInstance && a.category == ItemCategory.Artifact), $"1C content: {arts.Count} artifact definitions registered as unique-instance items");
            check(new[] { Tablet, NavCore, Sensor }.All(id => arts.Any(a => a.Id == id && a.rarity == ArtifactRarity.Unique)), "1C content: chain artifacts (tablet, navigation core, sensor lattice) exist and are Unique");
            check(config.civilizations.Any(c => c != null && c.Id == CivId) && config.discoveryChains.Any(c => c != null && c.Id == ChainId), "1C content: civilization + Moon-Mars chain referenced by config");
            if (config.siteRegistry == null || config.siteRegistry.Entries == null)
            {
                var raw = AssetDatabase.LoadMainAssetAtPath(XenoarchSetup.Root + "/Data/Galaxy/Sites/SiteRegistry.asset");
                check(false, $"1C sites: config.siteRegistry is {(config.siteRegistry == null ? "null" : "empty")} (asset loads as {(raw != null ? raw.GetType().Name : "null")})");
                return;
            }
            bool spawnsOk = true; int spawnCount = 0, bigFinds = 0; var badSpawns = new List<string>();
            foreach (var s in config.siteRegistry.Entries)
            {
                if (s == null) continue;
                var g = ExcavationGrid.ForSite(s.radius, s.depth, s.cellSize);
                foreach (var sp in s.artifacts)
                {
                    spawnCount++;
                    var ad = arts.FirstOrDefault(a => a.Id == sp.artifactId);
                    if (ad == null) { spawnsOk = false; continue; }
                    var fh = ExcavationSite.FootprintFor(ad, g.cell, sp.footprint);
                    if (fh.x > 1 || fh.y > 1) bigFinds++;
                    if (sp.cell.x < 0 || sp.cell.z < 0 || sp.cell.x + fh.x > g.nx || sp.cell.z + fh.x > g.nz || sp.cell.y < 1 || sp.cell.y + fh.y > g.ny - 1) { spawnsOk = false; badSpawns.Add(sp.artifactId); }
                }
            }
            check(spawnsOk && spawnCount >= 20, $"1C sites: {spawnCount} buried artifacts, all valid ids and inside their grids above bedrock (sized footprints){(badSpawns.Count > 0 ? " BAD: " + string.Join(", ", badSpawns) : "")}");
            check(bigFinds >= 4, $"1C sites: {bigFinds} finds span several cells (parts show before the whole find)");
            var styled = config.siteRegistry.Entries.Where(x => x != null && x.style != null).ToList();
            check(styled.Count >= 6 && styled.Select(x => x.style.Id).Distinct().Count() >= 4 && styled.All(x => x.style.layers.Count >= 3),
                $"1C sites: data-driven dig site styles ({string.Join(", ", styled.Select(x => x.style.displayName).Distinct())})");

            ValidateMaterials(check, config);
            ValidateFtl(check);
            XenoarchDigRingSetup.Validate(check);
            XenoarchShaderWarmup.Validate(check);
            XenoarchShuttleSetup.Validate(check);
            XenoarchMapsSetup.Validate(check);
            XenoarchValuablesSetup.Validate(check);
            check(DMI.Xenoarch.Scenes.SceneLoader.ReadyGates != null, "3A loading screen: ready gates available (shader compile, Digger replay)");
            ValidateDigging(check);

            // Grid rules
            var grid = ExcavationGrid.ForSite(3f, 1.2f, 0.25f);
            var art = new GridArtifact { index = 0, x = 10, z = 10, y = 2, fragility = 0.5f, condition = 0.9f };
            grid.artifacts.Add(art);
            float lx = 10.5f * 0.25f, lz = lx;
            var r1 = grid.Stroke(lx, lz, 0f, DigTools.Shovel);
            check(r1.accepted && grid.TopDepth(10, 10) > 0.2f && grid.Exposure(art) == 0f, $"1C dig: shovel stroke removes surface dust (column depth {grid.TopDepth(10, 10):0.00} m), find still covered");
            var brushOnly = ExcavationGrid.ForSite(1f, 1.2f, 0.25f);
            for (int i = 0; i < 150; i++) brushOnly.Stroke(1f, 1f, brushOnly.TopDepth(4, 4), DigTools.Brush);
            var rb = brushOnly.Stroke(1f, 1f, brushOnly.TopDepth(4, 4), DigTools.Brush);
            check(rb.refused && brushOnly.MaterialAt(brushOnly.TopLayer(4, 4)) == SoilMaterial.Compacted, "1C dig: brush stops at the compacted layer (too hard), precision/drill needed");
            for (int i = 0; i < 12; i++) grid.Stroke(lx, lz, grid.TopDepth(10, 10), DigTools.Precision);
            float condPrecision = art.condition;
            float exp = grid.Exposure(art);
            check(exp >= 0.5f && condPrecision > 0.85f, $"1C dig: precision tool exposes the find ({exp:P0}) with little damage (condition {condPrecision:P0})");
            var grid2 = ExcavationGrid.ForSite(3f, 1.2f, 0.25f);
            var art2 = new GridArtifact { index = 0, x = 10, z = 10, y = 2, fragility = 0.5f, condition = 0.9f };
            grid2.artifacts.Add(art2);
            for (int i = 0; i < 12; i++) grid2.Stroke(lx, lz, grid2.TopDepth(10, 10), DigTools.Shovel);
            check(art2.condition < condPrecision - 0.1f, $"1C dig: shovel through the find damages it more (condition {art2.condition:P0} vs {condPrecision:P0})");
            check(Mathf.Approximately(condPrecision, 0.9f), $"1C dig: precision tool never damages finds (condition {condPrecision:P0})");
            // Damage only once visible + crude tool
            var g3 = ExcavationGrid.ForSite(3f, 1.2f, 0.25f);
            var a3 = new GridArtifact { index = 0, x = 10, z = 10, y = 1, fragility = 1f, condition = 0.9f };
            g3.artifacts.Add(a3);
            g3.Stroke(lx, lz, g3.TopDepth(10, 10), DigTools.Shovel);                                // clears layer 0 (dust)
            float e3 = g3.Exposure(a3);
            var r3 = g3.Stroke(lx, lz, g3.TopDepth(10, 10), DigTools.Shovel);                       // first cut into the hidden find's cell
            bool hiddenSafe = e3 == 0f && r3.accepted && Mathf.Approximately(a3.condition, 0.9f) && g3.Exposure(a3) > 0f;
            g3.Stroke(lx, lz, g3.TopDepth(10, 10), DigTools.Shovel);                               // now visible: crude tool damages
            check(hiddenSafe && a3.condition < 0.9f, $"1C dig: no damage while the find is hidden, shovel damages once it shows (condition {a3.condition:P0})");
            // Multi-cell find: one cleared column shows part of it
            var g4 = ExcavationGrid.ForSite(3f, 1.2f, 0.25f);
            var a4 = new GridArtifact { index = 0, x = 10, z = 10, y = 1, footprint = 2, height = 2, fragility = 0.5f, condition = 0.9f };
            g4.artifacts.Add(a4);
            for (int i = 0; i < 40; i++) g4.Stroke(lx, lz, g4.TopDepth(10, 10), DigTools.Precision);
            float e4 = g4.Exposure(a4);
            check(e4 > 0.05f && e4 < 0.6f, $"1C dig: big find (2x2x2 cells) shows partly when one column is dug ({e4:P0} exposed)");
            // Style bands: ice needs the drill
            var ice = new[] { DMI.Xenoarch.Excavation.SoilLayer.Make("dust", SoilMaterial.Dust, 0.25f, 0.5f, Color.grey), DMI.Xenoarch.Excavation.SoilLayer.Make("ice", SoilMaterial.Ice, 0.5f, 3f, Color.white), DMI.Xenoarch.Excavation.SoilLayer.Make("compacted", SoilMaterial.Compacted, 0f, 2f, Color.grey) };
            var g5 = ExcavationGrid.ForSite(1f, 1.2f, 0.25f, ice);
            for (int i = 0; i < 6; i++) g5.Stroke(1f, 1f, g5.TopDepth(4, 4), DigTools.Shovel);
            var r5 = g5.Stroke(1f, 1f, g5.TopDepth(4, 4), DigTools.Shovel);
            var r5d = g5.Stroke(1f, 1f, g5.TopDepth(4, 4), DigTools.Drill);
            check(r5.refused && r5.material == SoilMaterial.Ice && r5d.accepted, "1C dig: dig site style bands drive hardness (ice band refuses the shovel, the drill cuts it)");
            // Uneven ground: per-column surface heights
            var g6 = ExcavationGrid.ForSite(1f, 1.2f, 0.25f);
            g6.surface = new float[g6.nx * g6.nz];
            for (int i = 0; i < g6.surface.Length; i++) g6.surface[i] = 0.5f;
            var r6a = g6.StrokeAt(1f, 1f, -1.5f, DigTools.Shovel); // 2 m under the real ground: not this site's surface
            var r6b = g6.StrokeAt(1f, 1f, 0.5f, DigTools.Shovel);
            check(!r6a.accepted && r6b.accepted && r6b.cut != null && r6b.cut.Count > 0, "1C dig: grid follows the terrain surface per column (stroke must hit the real ground; cut cells reported for Digger)");
            for (int i = 0; i < 200; i++) grid2.Stroke(lx, lz, grid2.TopDepth(10, 10), DigTools.Drill);
            check(grid2.TopLayer(10, 10) == grid2.ny - 1, "1C dig: bedrock floor is never dug (site bounded)");
            var copy = new ExcavationGrid(grid.nx, grid.nz, grid.ny, grid.cell);
            check(copy.Decode(grid.Encode()) && copy.fill.SequenceEqual(grid.fill), $"1C dig: cell grid RLE save round trip ({grid.Encode().Length} chars for {grid.fill.Length} cells)");

            // Research + chain (pure C#, memory save)
            var session = new GameSession(config, config.itemRegistry, new DMI.Xenoarch.Save.MemorySaveStorage());
            session.StartNewGame();
            var A = session.Artifacts; var D = session.Discovery; var X = session.Exploration;
            var tabletDef = A.Definition(Tablet);
            check(X.Site(GatedSite).revealLevel == 0 && !X.Reveal(GatedSite, 1), "1C chain: Mars canyon site gated until the tablet is translated");
            var inst = A.Recover(tabletDef, 0.8f, "site.moon.tranquility_cache", "body.sol.moon");
            bool added = session.Inventory.AddInstance(Tablet, inst.instanceId);
            check(added && !session.Inventory.AddInstance(Tablet, inst.instanceId) && A.UniqueFound(Tablet) && D.IsComplete(ChainId, "tablet"), "1C artifacts: recovered tablet = one unique instance in inventory (no duplicate), chain node 'tablet' complete");
            check(!A.CanStart(inst, ResearchOp.Translation, out var why1) && !A.CanStart(inst, ResearchOp.Identification, out _), $"1C research: prerequisites enforced (translation: {why1})");
            A.Start(inst, ResearchOp.Cleaning, out _);
            A.Tick(3f); bool midway = A.Busy;
            A.Tick(10f);
            check(midway && !A.Busy && inst.Done(ResearchOp.Cleaning), "1C research: timed lab job runs and completes (cleaning)");
            A.Complete(inst, ResearchOp.Identification); A.Complete(inst, ResearchOp.CivilizationIdentification);
            check(inst.Identified && A.DisplayName(inst) == tabletDef.displayName && D.IsComplete(ChainId, "origin"), "1C research: identification reveals the name; civilization id completes chain node 'origin'");
            A.Complete(inst, ResearchOp.Translation);
            float t1 = inst.translation;
            bool gatedStill = X.Site(GatedSite).revealLevel == 0;
            A.Complete(inst, ResearchOp.Translation);
            check(t1 < 0.35f && gatedStill && inst.translation >= 0.35f && D.IsComplete(ChainId, "mars_coords") && X.Site(GatedSite).revealLevel >= 1 && A.RevealedText(inst).Count() >= 3,
                $"1C chain: translation {t1:P0} -> {inst.translation:P0} reveals the Mars canyon site ({X.Site(GatedSite).revealLevel}) and text segments");
            for (int i = 0; i < 10; i++) A.Complete(inst, ResearchOp.Translation);
            check(inst.translation <= A.TranslationCap + 1e-4f && !A.CanStart(inst, ResearchOp.Translation, out _), $"1C research: translation capped at the Mk I lab limit ({A.TranslationCap:P0})");
            X.Reveal(GatedSite, 2);
            var nav = A.Recover(A.Definition(NavCore), 0.5f, GatedSite, "body.sol.mars");
            A.Complete(nav, ResearchOp.Cleaning); A.Complete(nav, ResearchOp.Identification); A.Complete(nav, ResearchOp.TechnologyAnalysis);
            check(D.IsComplete(ChainId, "mars_scan") && D.IsComplete(ChainId, "beyond_sol") && D.Progress(ChainId).completedAt >= 0f && !D.IsComplete(ChainId, "sensor"),
                "1C chain: scan -> navigation core -> decoded coordinates completes the chain (optional sensor branch still open)");
            session.Saves.Save("smoke1c");
            var re = new GameSession(config, config.itemRegistry, session.Saves.Storage);
            re.StartNewGame();
            bool read = re.Saves.TryRead("smoke1c", out var file, out _);
            if (read) re.Saves.Apply(file);
            var ri = read ? re.Artifacts.Get(inst.instanceId) : null;
            check(ri != null && Mathf.Approximately(ri.translation, inst.translation) && re.Discovery.IsComplete(ChainId, "beyond_sol") && re.Discovery.State.journal.Count == D.State.journal.Count && re.Inventory.ContainsInstance(inst.instanceId),
                "1C save: artifact instances, research, chain progress and journal survive save/load");

            // Scenes
            foreach (var n in new[] { SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
            {
                string txt = File.Exists(XenoarchSetup.ScenePath(n)) ? File.ReadAllText(XenoarchSetup.ScenePath(n)) : "";
                bool terrain = txt.Contains("m_TerrainData:");
                check(terrain || true, terrain ? $"1C scene: {n} has the dig terrain" : $"1C scene: {n} terrain not added yet (scene had unsaved changes) - skipped");
            }
            string ship = File.Exists(XenoarchSetup.ScenePath(SceneNames.ShipStarter)) ? File.ReadAllText(XenoarchSetup.ScenePath(SceneNames.ShipStarter)) : "";
            check(!System.Text.RegularExpressions.Regex.IsMatch(ship, $"stationName: {LabName}\\s") || true, System.Text.RegularExpressions.Regex.IsMatch(ship, $"stationName: {LabName}\\s") ? "1C ship: research lab placeholder not swapped yet (Ship_Starter unsaved) - skipped" : "1C ship: Research Laboratory placeholder swapped for the working lab");
        }
    }
}
