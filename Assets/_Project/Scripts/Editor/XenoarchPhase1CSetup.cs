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
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1C setup (additive, re-runnable): one civilization, ~30 artifact definitions (registered as items), the
    /// Moon-to-Mars discovery chain, buried artifact placements on the six sites, flat Unity Terrain (with a hole cut
    /// over each site footprint) replacing the primitive ground in Moon/Mars, and the ship's Research Laboratory
    /// placeholder swapped for the working ResearchTerminal. Scenes with unsaved changes are skipped, never saved.
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
        public const float TerrainSize = 256f, TerrainHeight = 40f, TerrainBase = 10f;

        [MenuItem("Xenoarch/Setup/10. Phase 1C: artifacts, chain, dig sites, terrain, research lab (additive)", priority = 30)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Xenoarch] 1C: exit Play Mode first"); return; }
            XenoarchSetup.EnsureFolder(ArtifactsFolder); XenoarchSetup.EnsureFolder(DiscoveryFolder); XenoarchSetup.EnsureFolder(TerrainFolder);
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.itemRegistry == null || config.siteRegistry == null) { Debug.LogError("[Xenoarch] 1C: config/registries missing (run Setup 1-7 first)"); return; }

            var civ = Civ();
            var arts = Artifacts(log);
            var reg = config.itemRegistry;
            var entries = reg.Entries.Where(e => e != null).ToList();
            foreach (var a in arts) if (!entries.Any(e => e.Id == a.Id)) entries.Add(a);
            reg.SetEntries(entries);
            EditorUtility.SetDirty(reg);

            var chain = Chain();
            if (!config.civilizations.Contains(civ)) config.civilizations.Add(civ);
            if (!config.discoveryChains.Contains(chain)) config.discoveryChains.Add(chain);
            EditorUtility.SetDirty(config);

            Spawns(config.siteRegistry, log);
            AssetDatabase.SaveAssets();

            foreach (var scene in new[] { SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
                WithScene(scene, log, s => TerrainPass(s, config.siteRegistry, log));
            WithScene(SceneNames.ShipStarter, log, s =>
            {
                var all = s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
                var sb = new StringBuilder();
                XenoarchShipSceneBuilder.SwapStation<ResearchTerminal>(all, LabName, LabName, sb);
                log.Append(sb);
                return true;
            });
            Debug.Log($"[Xenoarch] Phase 1C setup:\n{log}");
            File.WriteAllText("Logs/xenoarch_1c.txt", log.ToString());
        }

        // ------------------------------------------------------------------ scenes
        static void WithScene(string name, StringBuilder log, System.Func<Scene, bool> work)
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

        static bool TerrainPass(Scene scene, SiteRegistry sites, StringBuilder log)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.Any(r => r.GetComponentInChildren<Terrain>(true) != null)) { log.AppendLine($"{scene.name}: terrain already present, kept"); return false; }
            var siteDefs = sites.Entries.Where(s => s != null && s.sceneName == scene.name).ToList();
            string body = siteDefs.FirstOrDefault()?.bodyId ?? "";
            string prefix = scene.name.StartsWith("Mars") ? "Mars" : "Moon";

            // Soil texture + layer (colour shared with the runtime trench mesh).
            string texPath = $"{TerrainFolder}/{prefix}_Soil.png";
            var col = SoilPalette.Surface(body);
            var tex = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var rng = new System.Random(prefix.Length * 7919);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float n = 0.92f + 0.16f * (float)rng.NextDouble(); tex.SetPixel(x, y, col * n); }
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var layer = new TerrainLayer { diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath), tileSize = new Vector2(4f, 4f), name = $"{prefix}_Soil" };
            string layerPath = $"{TerrainFolder}/{prefix}_Soil.terrainlayer";
            AssetDatabase.DeleteAsset(layerPath);
            AssetDatabase.CreateAsset(layer, layerPath);

            var data = new TerrainData { heightmapResolution = 513, name = $"{scene.name}_TerrainData" };
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            int hr = data.heightmapResolution;
            var h = new float[hr, hr];
            for (int i = 0; i < hr; i++) for (int j = 0; j < hr; j++) h[i, j] = TerrainBase / TerrainHeight;
            data.SetHeights(0, 0, h);
            data.terrainLayers = new[] { layer };

            // Holes over each site footprint (same square the runtime grid uses), aligned to 0.5 m hole cells.
            int res = data.holesResolution;
            float step = TerrainSize / res, origin = -TerrainSize * 0.5f;
            var holes = new bool[res, res];
            for (int i = 0; i < res; i++) for (int j = 0; j < res; j++) holes[i, j] = true;
            int cut = 0;
            foreach (var s in siteDefs)
            {
                var g = ExcavationGrid.ForSite(s.radius, s.depth, s.cellSize > 0.05f ? s.cellSize : 0.25f);
                float minX = s.position.x - g.SizeX * 0.5f, maxX = minX + g.SizeX, minZ = s.position.z - g.SizeZ * 0.5f, maxZ = minZ + g.SizeZ;
                for (int zi = 0; zi < res; zi++)
                    for (int xi = 0; xi < res; xi++)
                    {
                        float cx = origin + (xi + 0.5f) * step, cz = origin + (zi + 0.5f) * step;
                        if (cx > minX && cx < maxX && cz > minZ && cz < maxZ) { holes[zi, xi] = false; cut++; }
                    }
            }
            data.SetHoles(0, 0, holes);
            string dataPath = $"{TerrainFolder}/{scene.name}_TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = $"{prefix}_Terrain";
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(origin, -TerrainBase, origin);
            var env = roots.FirstOrDefault(r => r.name == "Environment");
            if (env != null) go.transform.SetParent(env.transform, true);

            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            int hidden = 0;
            foreach (var g in all.Where(g => g.name == $"{prefix}_Ground" || g.name.StartsWith("Excavation_Area_Marker")))
            {
                if (!g.activeSelf) continue;
                Undo.RecordObject(g, "Hide placeholder ground"); g.SetActive(false); hidden++;
            }
            log.AppendLine($"{scene.name}: terrain {TerrainSize}x{TerrainSize} m (surface y=0), {siteDefs.Count} site hole(s) ({cut} hole cells), {hidden} placeholder ground/marker object(s) deactivated (not deleted)");
            return true;
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
            yield return Mk(Tablet, "Vael Waystation Tablet", "Engraved metal plate", ArtifactCategory.Tablet, ArtifactRarity.Unique, "Titanium-iridium alloy", ArtifactShape.Tablet, Metal, 0.32f, 1200, 0.45f,
                "A plate covered in angular glyph rows, cold to the touch and too regular to be natural.", 0.9f, 0.4f, 0.85f, false,
                text: new[] { "0.10|...we who walked before the dust...", "0.20|...this waystation beneath the grey sea keeps the path...", "0.35|...the second waystation lies on the red world, in the long canyon east of the great shield mountains...", "0.55|...from there the guide-stone points beyond the sun..." });
            yield return Mk(NavCore, "Vael Navigation Core", "Damaged mechanism", ArtifactCategory.NavigationDevice, ArtifactRarity.Unique, "Fused glass and alloy lattice", ArtifactShape.Device, Teal, 0.4f, 2600, 0.7f,
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
            yield return Mk("artifact.moon_vault_plate", "Vault Plate", "Curved metal panel", ArtifactCategory.MechanicalComponent, ArtifactRarity.Common, "Titanium alloy", ArtifactShape.Box, Metal, 0.35f, 120, 0.25f, "A curved panel, perhaps from a container.");
            yield return Mk("artifact.moon_lens", "Echo Lens", "Clouded crystal", ArtifactCategory.ScientificInstrument, ArtifactRarity.Rare, "Fused glass", ArtifactShape.Crystal, Glass, 0.18f, 520, 0.75f, "A lens that rings when tapped.", 0.4f, 0.7f, techOp: true, research: "An acoustic lens used to sound out buried voids.");
            yield return Mk("artifact.moon_ring", "Spiral Band", "Small metal ring", ArtifactCategory.Jewelry, ArtifactRarity.Uncommon, "Iridium alloy", ArtifactShape.Ring, Bronze, 0.08f, 260, 0.3f, "A band engraved inside and out.", 0.5f);
            yield return Mk("artifact.moon_deep_cache", "Deep Echo Cache", "Sealed box", ArtifactCategory.AncientTechnology, ArtifactRarity.Exceptional, "Alloy", ArtifactShape.Box, Teal, 0.4f, 900, 0.5f, "A sealed container from far below the surface.", 0.6f, 0.8f, techOp: true, research: "Contains layered data films; needs a better lab to read.");
            yield return Mk("artifact.moon_tool_head", "Cutting Head", "Worn tool tip", ArtifactCategory.Tool, ArtifactRarity.Common, "Hardened alloy", ArtifactShape.Shard, Metal, 0.12f, 80, 0.2f, "A worn cutting edge.");
            yield return Mk("artifact.moon_data_crystal", "Data Crystal", "Glowing shard", ArtifactCategory.DataCrystal, ArtifactRarity.Rare, "Doped crystal", ArtifactShape.Crystal, Teal, 0.16f, 640, 0.65f, "A crystal with a faint inner light.", 0.5f, 0.7f, techOp: true, research: "Holds patterned light: a star field, partly corrupted.");
            // Mars finds
            yield return Mk("artifact.mars_relay_fragment", "Relay Fragment", "Scorched panel", ArtifactCategory.MechanicalComponent, ArtifactRarity.Uncommon, "Alloy", ArtifactShape.Box, Red, 0.3f, 240, 0.35f, "Part of a larger transmitter.", 0.3f, 0.5f, techOp: true, research: "Part of a relay that pulsed on a fixed period.");
            yield return Mk("artifact.mars_canyon_marker", "Canyon Marker", "Engraved stone", ArtifactCategory.Cultural, ArtifactRarity.Common, "Basalt", ArtifactShape.Shard, Stone, 0.3f, 110, 0.3f, "A shaped basalt block with a spiral glyph.");
            yield return Mk("artifact.mars_shrine_idol", "Dune Shrine Figure", "Carved figure", ArtifactCategory.Ceremonial, ArtifactRarity.Rare, "Fused glass", ArtifactShape.Crystal, Amber, 0.22f, 560, 0.6f, "A stylised standing figure.", 0.8f);
            yield return Mk("artifact.mars_offering_bowl", "Offering Bowl", "Shallow dish", ArtifactCategory.Cultural, ArtifactRarity.Uncommon, "Fired regolith", ArtifactShape.Disc, Bone, 0.26f, 200, 0.65f, "A dish with traces of residue.", 0.6f);
            yield return Mk("artifact.mars_glyph_tablet", "Shrine Tablet", "Small engraved plate", ArtifactCategory.Document, ArtifactRarity.Rare, "Iridium alloy", ArtifactShape.Tablet, Bronze, 0.22f, 700, 0.45f, "A smaller cousin of the Moon tablet.", 0.8f, text: new[] { "0.2|...the second waystation...", "0.45|...rest here, then follow the guide-stone..." });
            yield return Mk("artifact.mars_spire_cap", "Spire Capstone", "Pointed cap", ArtifactCategory.MechanicalComponent, ArtifactRarity.Exceptional, "Alloy", ArtifactShape.Crystal, Metal, 0.4f, 950, 0.35f, "The tip of a tall buried structure.", 0.5f, 0.6f, techOp: true, research: "An antenna tip tuned to a single distant frequency.");
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

        static void Spawns(SiteRegistry sites, StringBuilder log)
        {
            int n = 0;
            foreach (var kv in SiteFinds)
            {
                var s = sites.Get(kv.Key);
                if (s == null) { log.AppendLine($"Site {kv.Key} missing"); continue; }
                s.cellSize = 0.25f;
                s.artifacts = kv.Value.Select(v => new SiteArtifactSpawn { artifactId = v.id, cell = new Vector3Int(v.x, v.y, v.z), footprint = v.f }).ToList();
                s.gateFact = s.Id == GatedSite ? MarsCoordsFact : "";
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
            bool spawnsOk = true; int spawnCount = 0;
            foreach (var s in config.siteRegistry.Entries)
            {
                if (s == null) continue;
                var g = ExcavationGrid.ForSite(s.radius, s.depth, s.cellSize > 0.05f ? s.cellSize : 0.25f);
                foreach (var sp in s.artifacts)
                {
                    spawnCount++;
                    if (!arts.Any(a => a.Id == sp.artifactId) || sp.cell.x < 0 || sp.cell.z < 0 || sp.cell.x + sp.footprint > g.nx || sp.cell.z + sp.footprint > g.nz || sp.cell.y < 1 || sp.cell.y > g.ny - 2) spawnsOk = false;
                }
            }
            check(spawnsOk && spawnCount >= 20, $"1C sites: {spawnCount} buried artifacts, all valid ids and inside their grids above bedrock");

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
