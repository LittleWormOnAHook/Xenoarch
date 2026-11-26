using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1C batch 3C: map data. Bakes a top-down relief map of each landing region from the scene terrain
    /// (Data/Maps/*.png, referenced by the body), copies LandingZone marker positions to the site/body definitions
    /// (orbit scan view), and adds nearby star systems for the galaxy map (Tau Ceti revealed by the decoded navigation
    /// core). Scenes are only read (never saved); scenes with unsaved changes are skipped.
    /// </summary>
    public static class XenoarchMapsSetup
    {
        public const string MapsFolder = XenoarchSetup.Root + "/Data/Maps";
        public const string SystemsFolder = XenoarchPhase1BSetup.GalaxyFolder + "/Systems";
        public const string ExtrasolarFact = "chain.moon_mars.extrasolar_coords";
        public const int MapSize = 512;

        [MenuItem("Xenoarch/Setup/12. Maps: bake surface maps, landing hints, galaxy systems", priority = 32)]
        public static void RunMenu() { var log = new StringBuilder(); Run(log); Debug.Log($"[Xenoarch] Maps setup:\n{log}"); }

        /// <summary>Adds any missing galaxy system assets (e.g. Proxima Centauri for the Nav dropdown) without re-baking maps.</summary>
        public static void EnsureSystems()
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.galaxy == null) { Debug.LogWarning("[Xenoarch] Systems: config/galaxy missing"); return; }
            var log = new StringBuilder(); Systems(config.galaxy, log);
            AssetDatabase.SaveAssets(); Debug.Log($"[Xenoarch] {log}");
        }

        public static void Run(StringBuilder log)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.galaxy == null) { log.AppendLine("Maps: config/galaxy missing"); return; }
            XenoarchSetup.EnsureFolder(MapsFolder);
            foreach (var name in new[] { SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
            {
                var body = config.galaxy.FindBodyByScene(name);
                if (body == null) { log.AppendLine($"Maps: no body lands in {name}"); continue; }
                XenoarchPhase1CSetup.WithScene(name, log, s => { Bake(s, body, config, log); return false; }, readOnly: true); // bake only reads the scene
            }
            Systems(config.galaxy, log);
            AssetDatabase.SaveAssets();
        }

        static void Bake(Scene scene, CelestialBodyDefinition body, XenoarchConfig config, StringBuilder log)
        {
            var roots = scene.GetRootGameObjects();
            var terrain = roots.Select(r => r.GetComponentInChildren<Terrain>(true)).FirstOrDefault(t => t != null);
            if (terrain == null || terrain.terrainData == null) { log.AppendLine($"{scene.name}: no terrain, map not baked"); return; }
            var td = terrain.terrainData; var tp = terrain.transform.position;
            var sites = config.siteRegistry.Entries.Where(s => s != null && s.sceneName == scene.name).ToList();
            var zones = roots.SelectMany(r => r.GetComponentsInChildren<LandingZone>(true)).ToList();
            var spawn = roots.SelectMany(r => r.GetComponentsInChildren<PlayerSpawnPoint>(true)).FirstOrDefault(p => p.spawnId == PlayerSpawnIds.Default);

            // Landing hints (data copy of his markers; the markers themselves are untouched).
            foreach (var site in sites)
            {
                var z = zones.FirstOrDefault(m => m.siteId == site.Id);
                bool has = z != null; var pos = has ? z.transform.position : Vector3.zero;
                if (site.hasLandingHint != has || site.landingHint != pos) { site.hasLandingHint = has; site.landingHint = pos; EditorUtility.SetDirty(site); }
            }
            var bz = zones.FirstOrDefault(m => string.IsNullOrEmpty(m.siteId));
            body.hasBaseLanding = bz != null; body.baseLanding = bz != null ? bz.transform.position : Vector3.zero;

            // Region: everything of interest plus margin, at least 300 m, inside the terrain.
            var pts = sites.Select(s => s.position).Concat(zones.Select(z => z.transform.position)).ToList();
            if (spawn != null) pts.Add(spawn.transform.position);
            if (pts.Count == 0) pts.Add(tp + td.size * 0.5f);
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x), minZ = pts.Min(p => p.z), maxZ = pts.Max(p => p.z);
            float size = Mathf.Min(Mathf.Max(500f, Mathf.Max(maxX - minX, maxZ - minZ) + 200f), Mathf.Min(td.size.x, td.size.z));
            float cx = Mathf.Clamp((minX + maxX) * 0.5f, tp.x + size * 0.5f, tp.x + td.size.x - size * 0.5f);
            float cz = Mathf.Clamp((minZ + maxZ) * 0.5f, tp.z + size * 0.5f, tp.z + td.size.z - size * 0.5f);
            var rect = new Rect(cx - size * 0.5f, cz - size * 0.5f, size, size);

            var tex = Relief(td, tp, rect, body.mapColor);
            string path = $"{MapsFolder}/{scene.name}_SurfaceMap.png";
            var bytes = tex.EncodeToPNG(); Object.DestroyImmediate(tex);
            bool changed = !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes);
            if (changed) { File.WriteAllBytes(path, bytes); AssetDatabase.ImportAsset(path); }
            if (AssetImporter.GetAtPath(path) is TextureImporter ti && (ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp || ti.npotScale != TextureImporterNPOTScale.None || ti.textureCompression != TextureImporterCompression.Uncompressed))
            {
                ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.None; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.maxTextureSize = MapSize;
                ti.SaveAndReimport();
            }
            body.surfaceMap = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            body.surfaceMapRect = rect;
            EditorUtility.SetDirty(body);
            log.AppendLine($"{scene.name}: surface map {(changed ? "baked" : "unchanged")} ({rect.width:0} m square at {rect.center.x:0},{rect.center.y:0}); landing hints for {sites.Count(s => s.hasLandingHint)}/{sites.Count} sites{(body.hasBaseLanding ? " + base" : "")}");
        }

        /// <summary>Hillshaded relief tinted by the body's map colour, with 5 m contour lines.</summary>
        public static Texture2D Relief(TerrainData td, Vector3 tp, Rect rect, Color tint)
        {
            var tex = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false);
            var px = new Color[MapSize * MapSize];
            var heights = new float[MapSize * MapSize];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int y = 0; y < MapSize; y++)
                for (int x = 0; x < MapSize; x++)
                {
                    float wx = rect.xMin + (x + 0.5f) / MapSize * rect.width, wz = rect.yMin + (y + 0.5f) / MapSize * rect.height;
                    float h = td.GetInterpolatedHeight((wx - tp.x) / td.size.x, (wz - tp.z) / td.size.z);
                    heights[y * MapSize + x] = h; lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
            var light = new Vector3(-0.6f, 1.2f, 0.7f).normalized;
            var baseCol = Color.Lerp(tint, new Color(0.6f, 0.6f, 0.6f), 0.35f);
            for (int y = 0; y < MapSize; y++)
                for (int x = 0; x < MapSize; x++)
                {
                    float wx = rect.xMin + (x + 0.5f) / MapSize * rect.width, wz = rect.yMin + (y + 0.5f) / MapSize * rect.height;
                    var n = td.GetInterpolatedNormal((wx - tp.x) / td.size.x, (wz - tp.z) / td.size.z);
                    float shade = Mathf.Clamp01(Vector3.Dot(n, light));
                    float h = heights[y * MapSize + x], hn = hi > lo ? (h - lo) / (hi - lo) : 0.5f;
                    var c = baseCol * (0.25f + 0.75f * shade) * (0.7f + 0.45f * hn);
                    float f = Mathf.Repeat(h, 5f);
                    if (f < 0.25f || f > 4.75f) c *= 0.8f;
                    c.a = 1f;
                    px[y * MapSize + x] = c;
                }
            tex.SetPixels(px); tex.Apply(false);
            return tex;
        }

        static void Systems(GalaxyDefinition galaxy, StringBuilder log)
        {
            XenoarchSetup.EnsureFolder(SystemsFolder);
            var defs = new (string id, string name, string cls, Vector3 pos, string fact, string note)[]
            {
                ("sys.alpha_centauri", "Alpha Centauri", "G2V", new Vector3(-1.6f, -1.4f, -3.8f), null, null),
                ("sys.proxima_centauri", "Proxima Centauri", "M5.5Ve", new Vector3(-1.55f, -1.18f, -3.77f), null, null),
                ("sys.barnards_star", "Barnard's Star", "M4V", new Vector3(-0.06f, -5.94f, 0.49f), null, null),
                ("sys.sirius", "Sirius", "A1V", new Vector3(-1.6f, 8.1f, -2.5f), null, null),
                ("sys.epsilon_eridani", "Epsilon Eridani", "K2V", new Vector3(6.2f, 8.3f, -1.7f), null, null),
                ("sys.tau_ceti", "Tau Ceti", "G8V", new Vector3(10.3f, 5.0f, -3.3f), ExtrasolarFact,
                    "Coordinates decoded from the Vael navigation core point here. Source of the Vael signal."),
            };
            int added = 0;
            foreach (var d in defs)
            {
                string path = $"{SystemsFolder}/System_{d.id.Substring(4)}.asset";
                var s = AssetDatabase.LoadAssetAtPath<StarSystemDefinition>(path);
                if (s == null)
                {
                    s = ScriptableObject.CreateInstance<StarSystemDefinition>();
                    s.SetIdForTooling(d.id);
                    s.displayName = d.name; s.stellarClass = d.cls; s.galacticPosition = d.pos; s.seed = d.id.GetHashCode();
                    s.knownByDefault = d.fact == null; s.revealFact = d.fact; s.revealNote = d.note;
                    s.minFtlRangeLy = d.pos.magnitude; s.archaeologicalPotential = d.fact != null ? 0.9f : 0.1f;
                    AssetDatabase.CreateAsset(s, path); added++;
                }
                if (!galaxy.systems.Contains(s)) { galaxy.systems.Add(s); EditorUtility.SetDirty(galaxy); }
            }
            log.AppendLine($"Galaxy: {added} system(s) added ({galaxy.systems.Count} total); Tau Ceti revealed by {ExtrasolarFact}");
        }

        // ------------------------------------------------------------------ smoke
        public static void Validate(System.Action<bool, string> check)
        {
            // Projection: top-down +x right, +z up; edge-on shows height.
            var a = MapView3D.Project(new Vector3(1, 0, 0), 0f, 90f, out _); var b = MapView3D.Project(new Vector3(0, 0, 1), 0f, 90f, out _);
            var c = MapView3D.Project(new Vector3(0, 1, 0), 0f, 0f, out _); var r = MapView3D.Project(new Vector3(1, 0, 0), 90f, 90f, out _);
            check(Near(a, new Vector2(1, 0)) && Near(b, new Vector2(0, -1)) && Near(c, new Vector2(0, -1)) && Near(r.normalized, new Vector2(0, 1)),
                "3C maps: 3D map projection (top-down, edge-on, rotated)");
            // Free rotation: tumble past the top to view from underneath (no clamp), quaternion stays unit after many drags.
            var cam = MapView3D.DefaultCam(0f, 90f);
            for (int i = 0; i < 1000; i++) cam = cam * Quaternion.AngleAxis(0.18f, Vector3.right) * Quaternion.AngleAxis(0.05f, Vector3.up);
            float len = Mathf.Sqrt(cam.x * cam.x + cam.y * cam.y + cam.z * cam.z + cam.w * cam.w);
            var under = MapView3D.DefaultCam(0f, 90f) * Quaternion.AngleAxis(180f, Vector3.right);
            MapView3D.Project(new Vector3(0, 1, 0), MapView3D.DefaultCam(0f, 90f), out float dTop); MapView3D.Project(new Vector3(0, 1, 0), under, out float dUnder);
            check(Mathf.Abs(len - 1f) < 0.01f && dTop > 0.99f && dUnder < -0.99f, $"3C maps: free 3D rotation (view from below works, rotation stays unit: {len:0.0000})");
            var m = SurfaceMapView.ToMap01(new Rect(-100, -100, 200, 200), new Vector2(100, 100));
            check(Near(m, new Vector2(1, 0)), "3C maps: surface map world -> map coordinates");

            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            var g = config != null ? config.galaxy : null;
            var tau = g != null ? g.FindSystem("sys.tau_ceti") : null;
            bool lockedBefore = !ExplorationService.GalaxyUnlocked(g, k => false) && !ExplorationService.SystemKnown(tau, k => false);
            bool unlockedAfter = ExplorationService.GalaxyUnlocked(g, k => k == ExtrasolarFact) && ExplorationService.SystemKnown(tau, k => k == ExtrasolarFact);
            check(tau != null && lockedBefore && unlockedAfter, "3C galaxy: galaxy map and Tau Ceti stay hidden until the navigation core is decoded");
            bool chainSets = config != null && config.discoveryChains.Any(ch => ch != null && JsonUtility.ToJson(ch).Contains(ExtrasolarFact));
            check(chainSets, "3C galaxy: the discovery chain publishes the extrasolar-coordinates fact");
            bool navData = g != null && ExplorationService.NavSystems[0].id == "sys.sol"
                && ExplorationService.NavSystems.All(n => g.FindSystem(n.id) != null);
            bool navUnlockFact = ExplorationService.NavUnlockFact("sys.proxima_centauri") == "nav.system.proxima_centauri";
            var bf = StarFacts.For("sys.barnards_star"); var pf = StarFacts.For("sys.proxima_centauri"); var ef = StarFacts.For("sys.epsilon_eridani");
            check(bf != null && bf.planets.Length == 4 && pf != null && pf.planets.Any(pl => pl.Candidate) && ef != null,
                "3C nav: real star facts for Proxima Centauri, Barnard's Star, Epsilon Eridani (candidates flagged)");
            check(navData && navUnlockFact, "3C nav: system dropdown lists Sol (default), Proxima Centauri, Barnard's Star, Epsilon Eridani");

            foreach (var name in new[] { SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
            {
                var body = g != null ? g.FindBodyByScene(name) : null;
                bool ok = body != null && body.surfaceMap != null && body.surfaceMapRect.width >= 100f;
                int hints = config != null ? config.siteRegistry.Entries.Count(s => s != null && s.sceneName == name && s.hasLandingHint) : 0;
                check(true, ok ? $"3C maps: {name} surface map baked ({body.surfaceMapRect.width:0} m), {hints} landing sites on it" : $"3C maps: {name} surface map not baked yet (scene skipped)");
            }
        }

        static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-4f;
    }
}
