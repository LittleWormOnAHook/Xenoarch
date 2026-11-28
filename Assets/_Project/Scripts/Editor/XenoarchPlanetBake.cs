using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// t262u: bake each generated planet once into its own landing scene (terrain asset, lighting, landing zones,
    /// spawn, Digger when it is safe). Never overwrites a baked scene (use Rebake Terrain Only to refresh terrain
    /// without touching placed objects). Never saves or closes the currently open scene. Trigger:
    /// Temp/xenoarch_planet_bake.trig containing "all", "digger", "rebake", "repair", "versions", "health", or a body id (log: Logs/xenoarch_planet_bake.txt).
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchPlanetBake
    {
        public const string Trigger = "Temp/xenoarch_planet_bake.trig";
        public const string LogFile = "Logs/xenoarch_planet_bake.txt";
        public const string ScenesFolder = XenoarchSetup.ScenesFolder + "/Generated";
        public const string TerrainFolder = XenoarchSetup.Root + "/Data/Terrain/Generated";
        public const string RegistryPath = "Assets/_Project/Resources/Galaxy/BakedPlanetRegistry.asset";
        public const string CatalogPath = "Assets/_Project/Resources/World/PlanetSurfaceCatalog.asset";

        static XenoarchPlanetBake() => EditorApplication.update += Poll;
        static double _next;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (!File.Exists(Trigger)) return;
            string cmd = File.ReadAllText(Trigger).Trim();
            File.Delete(Trigger);
            if (cmd.Equals("digger", StringComparison.OrdinalIgnoreCase)) AddDiggerToBaked();
            else if (cmd.Equals("rebake", StringComparison.OrdinalIgnoreCase)) RebakeAllTerrain();
            else if (cmd.Equals("repair", StringComparison.OrdinalIgnoreCase)) RepairBakedDigger();
            else if (cmd.Equals("versions", StringComparison.OrdinalIgnoreCase)) RepairDiggerVersions();
            else if (cmd.Equals("health", StringComparison.OrdinalIgnoreCase)) CheckDiggerHealth();
            else if (string.IsNullOrEmpty(cmd) || cmd.Equals("all", StringComparison.OrdinalIgnoreCase)) BakeAll();
            else BakeBody(cmd);
        }

        static void Log(string s)
        {
            Debug.Log("[Xenoarch] Planet bake: " + s);
            try { Directory.CreateDirectory("Logs"); File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {s}\n"); } catch { }
        }

        [MenuItem("Xenoarch/Planets/Bake Generated Surfaces", priority = 80)]
        public static void BakeAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var log = new StringBuilder();
            try
            {
                EnsureSupportAssets(log);
                var bodies = LandableGenerated(out var galaxy);
                if (bodies.Count == 0) { Log("no generated landable planets (is the galaxy set up?)"); return; }
                int made = 0, skipped = 0, failed = 0;
                foreach (var b in bodies)
                {
                    string msg = BakeOne(b, galaxy, overwriteTerrain: false, log);
                    if (msg == "made") made++;
                    else if (msg == "skipped") skipped++;
                    else failed++;
                }
                RegisterBuildSettings(log);
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<BakedPlanetRegistry>(RegistryPath));
                Log($"done: {made} baked, {skipped} already existed (left alone), {failed} failed.\n{log}");
            }
            catch (Exception e) { Log("FAILED " + e); }
        }

        [MenuItem("Xenoarch/Planets/Add Digger to Baked Surfaces", priority = 81)]
        public static void AddDiggerToBaked()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var other = OtherTerrainScenes(null);
            if (other.Count > 0)
            {
                Log("Digger setup skipped: close these terrain scenes first (the open scene will not be saved or closed for you): " + string.Join(", ", other));
                return;
            }
            var log = new StringBuilder();
            var reg = AssetDatabase.LoadAssetAtPath<BakedPlanetRegistry>(RegistryPath);
            if (reg == null || reg.entries == null) { Log("no baked planets yet"); return; }
            int n = 0;
            foreach (var e in reg.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.scenePath) || !File.Exists(e.scenePath)) continue;
                if (IsUserOpenScene(e.scenePath)) { log.AppendLine($"{e.sceneName}: open in the Editor, skipped (close it and re-run)"); continue; }
                var scene = EditorSceneManager.OpenScene(e.scenePath, OpenSceneMode.Additive);
                try
                {
                    if (SetupDigger(scene, log)) { EditorSceneManager.SaveScene(scene, e.scenePath); n++; }
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
            Log($"Digger pass finished ({n} scene(s) updated).\n{log}");
        }

        [MenuItem("Xenoarch/Planets/Repair Baked Digger Paths", priority = 83)]
        public static void RepairBakedDigger()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var log = new StringBuilder();
            try
            {
                EnsureSupportAssets(log);
                string[] scenes = Directory.Exists(ScenesFolder)
                    ? Directory.GetFiles(ScenesFolder, "Surface_*.unity")
                    : Array.Empty<string>();
                int moved = 0, skipped = 0;
                foreach (var file in scenes)
                {
                    string assetPath = ToAssetPath(file);
                    if (IsUserOpenScene(assetPath)) { log.AppendLine($"{Path.GetFileNameWithoutExtension(assetPath)}: open in the Editor, skipped"); skipped++; continue; }
                    if (RemapDiggerFolder(assetPath, log)) moved++;
                }
                AssetDatabase.Refresh();

                var bodies = LandableGenerated(out _);
                int iceFixed = 0, diggerFixed = 0;
                foreach (var b in bodies)
                {
                    if (!BakedPlanetRegistry.TryGetScene(b.Id, out _, out var path) || string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    if (IsUserOpenScene(path)) { log.AppendLine($"{b.displayName}: scene is open, skipped"); continue; }
                    var rec = ProceduralGalaxy.Recipe(b.Id);
                    bool ice = rec != null && rec.type == ProceduralPlanetType.Ice;
                    bool missingRuntime = File.ReadAllText(path).IndexOf("Digger Master Runtime", StringComparison.Ordinal) < 0;
                    if (ice && RebakeTerrain(b, path, log)) iceFixed++;
                    if (missingRuntime)
                    {
                        var previous = SceneManager.GetActiveScene();
                        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        try
                        {
                            if (SetupDigger(scene, log)) { EditorSceneManager.SaveScene(scene, path); diggerFixed++; }
                        }
                        finally
                        {
                            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                        }
                    }
                }

                int deleted = DeleteOrphanUntitledFolders(log);
                Log($"repair finished: {moved} Digger folder(s) renamed, {iceFixed} ice terrain(s) restored, {diggerFixed} Digger runtime(s) added, {deleted} leftover Untitled folder(s) removed, {skipped} open scene(s) skipped.\n{log}");
            }
            catch (Exception e) { Log("FAILED " + e); }
        }

        /// <summary>
        /// t293u: Rebake Terrain Only re-ran Digger's forced setup, which bumps DiggerSystem.version and writes
        /// current_version.asset, but the scene was never marked dirty so the .unity kept the old number. At Play,
        /// Digger saw the mismatch, took its editor-only undo path before any chunks existed, threw an NRE and never
        /// initialised, so digging did nothing. This copies the persisted number back into each baked scene. It only
        /// changes the DiggerSystem version field; nothing is moved. Trigger: "versions".
        /// </summary>
        [MenuItem("Xenoarch/Planets/Repair Digger Versions", priority = 84)]
        public static void RepairDiggerVersions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var log = new StringBuilder();
            try
            {
                var systemT = FindType("Digger.Modules.Core.Sources.DiggerSystem");
                if (systemT == null) { Log("Digger not installed"); return; }
                var getPersisted = systemT.GetMethod("GetLastPersistedVersion",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (getPersisted == null) { Log("Digger GetLastPersistedVersion not found"); return; }
                string[] scenes = Directory.Exists(ScenesFolder) ? Directory.GetFiles(ScenesFolder, "Surface_*.unity") : Array.Empty<string>();
                int fixedScenes = 0, ok = 0, skipped = 0;
                foreach (var file in scenes)
                {
                    string path = ToAssetPath(file);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (IsUserOpenScene(path)) { log.AppendLine($"{name}: open in the Editor, skipped (close it and re-run)"); skipped++; continue; }
                    var existing = SceneManager.GetSceneByPath(path);
                    bool wasLoaded = existing.IsValid() && existing.isLoaded;
                    var scene = wasLoaded ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        int changed = 0;
                        foreach (var root in scene.GetRootGameObjects())
                        foreach (var d in root.GetComponentsInChildren(systemT, true))
                        {
                            long persisted = Convert.ToInt64(getPersisted.Invoke(d, null));
                            var so = new SerializedObject(d);
                            var prop = so.FindProperty("version");
                            if (prop == null) { log.AppendLine($"{name}: Digger version field not found"); continue; }
                            if (persisted <= 0 || prop.longValue == persisted) { ok++; continue; }
                            log.AppendLine($"{name}: Digger version {prop.longValue} -> {persisted}");
                            prop.longValue = persisted;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            changed++;
                        }
                        if (changed > 0)
                        {
                            EditorSceneManager.MarkSceneDirty(scene);
                            EditorSceneManager.SaveScene(scene, path);
                            fixedScenes++;
                        }
                    }
                    finally
                    {
                        if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                    }
                }
                Log($"Digger version repair finished: {fixedScenes} scene(s) fixed, {ok} already in sync, {skipped} skipped.\n{log}");
                CheckDiggerHealth();
            }
            catch (Exception e) { Log("Digger version repair FAILED " + e); }
        }

        /// <summary>
        /// t300u: read-only Digger health report for every baked Surface_* scene plus the Moon and Mars references.
        /// Every dig type (fenced major sites, survey caches, hidden caches, landing-zone caches) carves through the
        /// scene's DiggerExcavationSurface -> DiggerMasterRuntime -> DiggerSystem, so one broken link stops them all.
        /// Opens closed scenes additively and closes them without saving. Trigger: "health".
        /// </summary>
        [MenuItem("Xenoarch/Planets/Check Digger Health", priority = 85)]
        public static void CheckDiggerHealth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var systemT = FindType("Digger.Modules.Core.Sources.DiggerSystem");
            var masterT = FindType("Digger.Modules.Core.Sources.DiggerMaster");
            var runtimeT = FindType("Digger.Modules.Runtime.Sources.DiggerMasterRuntime");
            var surfaceT = FindType("DMI.Xenoarch.Integrations.Digger.DiggerExcavationSurface");
            if (systemT == null || masterT == null || runtimeT == null) { Log("health: Digger not installed"); return; }
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var getPersisted = systemT.GetMethod("GetLastPersistedVersion", flags);
            var basePath = systemT.GetProperty("BasePathData", flags);
            var paths = new List<string>();
            if (Directory.Exists(ScenesFolder)) paths.AddRange(Directory.GetFiles(ScenesFolder, "Surface_*.unity").Select(ToAssetPath).OrderBy(p => p));
            foreach (var refName in new[] { "Moon_Excavation_01", "Mars_Excavation_01" })
                paths.AddRange(AssetDatabase.FindAssets(refName + " t:Scene").Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p) == refName));
            var log = new StringBuilder();
            int healthy = 0, broken = 0;
            foreach (var path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var existing = SceneManager.GetSceneByPath(path);
                bool wasLoaded = existing.IsValid() && existing.isLoaded;
                Scene scene;
                try { scene = wasLoaded ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); }
                catch (Exception e) { log.AppendLine($"BROKEN  {name}: could not open ({e.Message})"); broken++; continue; }
                try
                {
                    var issues = new List<string>();
                    var roots = scene.GetRootGameObjects();
                    Component Find(Type t) => roots.Select(r => r.GetComponentInChildren(t, true)).FirstOrDefault(c => c != null);
                    if (Find(masterT) == null) issues.Add("no DiggerMaster");
                    if (Find(runtimeT) == null) issues.Add("no DiggerMasterRuntime");
                    if (surfaceT != null && Find(surfaceT) == null) issues.Add("no DiggerExcavationSurface");
                    var terrain = roots.Select(r => r.GetComponentInChildren<Terrain>(true)).FirstOrDefault(c => c != null);
                    if (terrain == null || terrain.terrainData == null) issues.Add("no terrain");
                    var systems = roots.SelectMany(r => r.GetComponentsInChildren(systemT, true)).ToList();
                    if (systems.Count == 0) issues.Add("no DiggerSystem");
                    string ver = "-";
                    foreach (var d in systems)
                    {
                        var so = new SerializedObject(d);
                        long v = so.FindProperty("version")?.longValue ?? -1;
                        long persisted = getPersisted != null ? Convert.ToInt64(getPersisted.Invoke(d, null)) : -1;
                        ver = $"{v}/{persisted}";
                        if (persisted > 0 && v != persisted) issues.Add($"version mismatch (scene {v}, persisted {persisted}: Digger will throw at Play and never dig)");
                        string bp = basePath?.GetValue(d) as string;
                        if (string.IsNullOrEmpty(bp) || !Directory.Exists(bp)) issues.Add($"data folder missing ({bp})");
                        var dt = so.FindProperty("Terrain")?.objectReferenceValue as Terrain;
                        if (terrain != null && dt != null && dt != terrain) issues.Add("DiggerSystem points at another terrain");
                    }
                    if (terrain != null)
                    {
                        var mat = terrain.materialTemplate;
                        string shader = mat != null && mat.shader != null ? mat.shader.name : "none";
                        if (shader.IndexOf("Digger", StringComparison.OrdinalIgnoreCase) < 0) issues.Add($"terrain material is not a Digger shader ({shader})");
                    }
                    if (issues.Count == 0) { healthy++; log.AppendLine($"OK      {name}: version {ver}"); }
                    else { broken++; log.AppendLine($"BROKEN  {name}: {string.Join("; ", issues)}"); }
                }
                finally
                {
                    if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                }
            }
            Log($"Digger health: {healthy} OK, {broken} broken.\n{log}");
        }

        [MenuItem("Xenoarch/Planets/Rebake Terrain Only (keep placed objects)", priority = 82)]
        public static void RebakeAllTerrain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var log = new StringBuilder();
            try
            {
                EnsureSupportAssets(log);
                var bodies = LandableGenerated(out var galaxy);
                int n = 0;
                foreach (var b in bodies)
                {
                    if (!BakedPlanetRegistry.TryGetScene(b.Id, out _, out var path) || string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    if (IsUserOpenScene(path)) { log.AppendLine($"{b.displayName}: scene is open, skipped (close it and re-run)"); continue; }
                    if (RebakeTerrain(b, path, log)) n++;
                }
                Log($"rebake finished ({n} scene(s)).\n{log}");
            }
            catch (Exception e) { Log("FAILED " + e); }
        }

        public static void BakeBody(string bodyId)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            var log = new StringBuilder();
            try
            {
                EnsureSupportAssets(log);
                var bodies = LandableGenerated(out var galaxy);
                var b = bodies.FirstOrDefault(x => x.Id == bodyId || string.Equals(x.displayName, bodyId, StringComparison.OrdinalIgnoreCase));
                if (b == null) { Log("no generated body matches '" + bodyId + "'"); return; }
                string msg = BakeOne(b, galaxy, overwriteTerrain: false, log);
                RegisterBuildSettings(log);
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<BakedPlanetRegistry>(RegistryPath));
                Log($"{b.displayName}: {msg}\n{log}");
            }
            catch (Exception e) { Log("FAILED " + e); }
        }

        static string BakeOne(CelestialBodyDefinition b, GalaxyDefinition galaxy, bool overwriteTerrain, StringBuilder log)
        {
            var rec = ProceduralGalaxy.Recipe(b.Id);
            if (rec == null) { log.AppendLine($"{b.Id}: no recipe"); return "failed"; }
            string sceneName = SceneNameFor(b);
            string scenePath = $"{ScenesFolder}/{sceneName}.unity";
            string terrainPath = $"{TerrainFolder}/{sceneName}_Terrain.asset";
            if (File.Exists(scenePath) && !overwriteTerrain)
            {
                Remember(b, sceneName, scenePath);
                log.AppendLine($"{b.displayName}: {scenePath} exists, left alone");
                return "skipped";
            }
            if (IsUserOpenScene(scenePath)) { log.AppendLine($"{b.displayName}: destination is the open scene, skipped"); return "failed"; }

            XenoarchSetup.EnsureFolder(ScenesFolder);
            XenoarchSetup.EnsureFolder(TerrainFolder);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var env = b.environment;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = env != null ? env.ambientColor : new Color(0.16f, 0.15f, 0.15f);
                if (env != null && env.fog) { RenderSettings.fog = true; RenderSettings.fogColor = env.fogColor; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = env.fogDensity; }
                if (env != null && env.skybox != null) RenderSettings.skybox = env.skybox;

                var data = ProceduralSurface.CreateTerrainData(rec, cutHoles: false);
                data.name = sceneName + "_Terrain";
                AssetDatabase.CreateAsset(data, terrainPath);
                PersistProceduralLayers(data, terrainPath);

                float half = ProceduralGalaxy.TerrainSize * 0.5f;
                var go = Terrain.CreateTerrainGameObject(data);
                go.name = sceneName + "_Terrain";
                go.transform.position = new Vector3(-half, 0f, -half);
                var terrain = go.GetComponent<Terrain>();
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat");
                if (mat != null) terrain.materialTemplate = mat;
                terrain.drawInstanced = true; terrain.heightmapPixelError = 4f; terrain.basemapDistance = 400f;
                terrain.Flush();

                var sun = new GameObject("Sun", typeof(Light));
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                var light = sun.GetComponent<Light>();
                light.type = LightType.Directional; light.color = new Color(1f, 0.95f, 0.88f); light.intensity = 1.3f; light.shadows = LightShadows.Soft;
                RenderSettings.sun = light;

                float gy = env != null ? env.gravity : 9.81f;
                var spawnPos = new Vector3(0f, rec.Height(0f, -40f) + 0.1f, -40f);
                XenoarchPlayerBuilder.InstantiatePlayer(spawnPos, 0f, gy);
                if (PlayerSpawnPoint.Find(PlayerSpawnIds.Default) == null)
                {
                    var sp = new GameObject("Spawn_Default");
                    sp.transform.position = spawnPos;
                    sp.AddComponent<PlayerSpawnPoint>();
                }

                PlaceZone("", "LandingZone_Base", new Vector3(0f, rec.Height(0f, -40f), -40f), Vector3.forward);
                foreach (var site in ProceduralGalaxy.Sites)
                {
                    if (site == null || site.bodyId != b.Id || !site.hasLandingHint) continue;
                    var p = site.landingHint;
                    var look = site.position - p; look.y = 0f;
                    PlaceZone(site.Id, "LandingZone_" + site.Id.Replace('.', '_'), p, look.sqrMagnitude > 0.01f ? look.normalized : Vector3.forward);
                }

                // Save first so DiggerMaster.CreateDirs uses the scene name, not "Untitled".
                EditorSceneManager.SaveScene(scene, scenePath);
                SceneManager.SetActiveScene(scene);

                bool digger = false;
                var other = OtherTerrainScenes(scene);
                if (other.Count > 0) log.AppendLine($"{b.displayName}: Digger not set up (other terrain scenes open: {string.Join(", ", other)}). Close them and run Xenoarch/Planets/Add Digger to Baked Surfaces.");
                else digger = SetupDigger(scene, log);

                EditorSceneManager.SaveScene(scene, scenePath);
                Remember(b, sceneName, scenePath);
                b.sceneName = sceneName;
                foreach (var s in ProceduralGalaxy.Sites) if (s != null && s.bodyId == b.Id) s.sceneName = sceneName;
                log.AppendLine($"{b.displayName}: wrote {scenePath} (Digger {(digger ? "yes" : "pending")})");
                return "made";
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static bool RebakeTerrain(CelestialBodyDefinition b, string scenePath, StringBuilder log)
        {
            var rec = ProceduralGalaxy.Recipe(b.Id);
            if (rec == null) { log.AppendLine($"{b.Id}: no recipe"); return false; }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                Terrain terrain = null;
                foreach (var r in scene.GetRootGameObjects()) { terrain = r.GetComponentInChildren<Terrain>(true); if (terrain != null) break; }
                if (terrain == null || terrain.terrainData == null) { log.AppendLine($"{b.displayName}: no terrain"); return false; }
                var path = AssetDatabase.GetAssetPath(terrain.terrainData);
                var fresh = ProceduralSurface.CreateTerrainData(rec, cutHoles: false);
                var dst = terrain.terrainData;
                dst.heightmapResolution = fresh.heightmapResolution;
                dst.size = fresh.size;
                dst.SetHeights(0, 0, fresh.GetHeights(0, 0, fresh.heightmapResolution, fresh.heightmapResolution));
                int n = fresh.terrainLayers != null ? fresh.terrainLayers.Length : 0;
                string names = fresh.terrainLayers == null ? "-" : string.Join(", ", fresh.terrainLayers.Select(l => l != null ? l.name : "null"));
                dst.terrainLayers = fresh.terrainLayers;
                dst.alphamapResolution = fresh.alphamapResolution;
                dst.SetAlphamaps(0, 0, fresh.GetAlphamaps(0, 0, fresh.alphamapResolution, fresh.alphamapResolution));
                PersistProceduralLayers(dst, path);
                EditorUtility.SetDirty(dst);
                AssetDatabase.SaveAssets();
                if (terrain != null) terrain.Flush();
                UnityEngine.Object.DestroyImmediate(fresh);
                int snapped = SnapSurfaceObjects(scene, terrain);
                int digger = SyncDiggerInScene(scene, log);
                var profile = PlanetHeightCatalog.For(b.Id);
                string shape = profile != null ? profile.shape.ToString() : "legacy-noise";
                // Do not move or delete anything except a baked rock root if we had written one (runtime scatter uses this name only when present).
                log.AppendLine($"{b.displayName}: terrain refreshed ({dst.heightmapResolution} res, {dst.size.y:0} m relief, {shape}, {n} layers: {names}), snapped {snapped} object(s), Digger sync {digger}");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!IsUserOpenScene(scenePath)) EditorSceneManager.SaveScene(scene, scenePath);
                return true;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded && !IsUserOpenScene(scenePath)) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static int SnapSurfaceObjects(Scene scene, Terrain terrain)
        {
            if (terrain == null) return 0;
            int n = 0;
            float Y(Vector3 p) => terrain.transform.position.y + terrain.SampleHeight(p);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var zone in root.GetComponentsInChildren<LandingZone>(true))
                {
                    var p = zone.transform.position;
                    p.y = Y(p);
                    zone.transform.position = p;
                    if (zone.autoPlaced) zone.autoPosition = p;
                    n++;
                }
                foreach (var spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                {
                    var p = spawn.transform.position;
                    p.y = Y(p) + 0.1f;
                    spawn.transform.position = p;
                    n++;
                }
            }
            return n;
        }

        static int SyncDiggerInScene(Scene scene, StringBuilder log)
        {
            var editorT = FindType("Digger.Modules.Core.Editor.DiggerSystemEditor");
            var systemT = FindType("Digger.Modules.Core.Sources.DiggerSystem");
            if (editorT == null || systemT == null) return 0;
            var init = editorT.GetMethod("Init", new[] { systemT, typeof(bool) });
            if (init == null) { log?.AppendLine($"{scene.name}: Digger Init not found"); return 0; }
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var d in root.GetComponentsInChildren(systemT, true))
                {
                    init.Invoke(null, new object[] { d, true });
                    // Digger's forced Init bumps DiggerSystem.version and writes current_version.asset; dirty it so the
                    // scene keeps the same number, or Play hits Digger's editor undo path and never loads chunks (t293u).
                    EditorUtility.SetDirty(d);
                    n++;
                }
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(scene);
            return n;
        }

        static void PlaceZone(string siteId, string name, Vector3 pos, Vector3 forward)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            if (forward.sqrMagnitude > 0.001f) go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var z = go.AddComponent<LandingZone>();
            z.siteId = siteId ?? "";
            z.footprint = 8f;
            z.autoPlaced = true;
            z.autoPosition = pos;
        }

        static bool SetupDigger(Scene scene, StringBuilder log)
        {
            var masterT = FindType("Digger.Modules.Core.Sources.DiggerMaster");
            var runtimeT = FindType("Digger.Modules.Runtime.Sources.DiggerMasterRuntime");
            var surfaceT = FindType("DMI.Xenoarch.Integrations.Digger.DiggerExcavationSurface");
            if (masterT == null || runtimeT == null || surfaceT == null) { log.AppendLine($"{scene.name}: Digger PRO not installed, skipped"); return false; }
            var other = OtherTerrainScenes(scene);
            if (other.Count > 0) { log.AppendLine($"{scene.name}: Digger skipped, other terrain scenes: {string.Join(", ", other)}"); return false; }
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
                    masterT.GetProperty("ResolutionMult")?.SetValue(master, XenoarchPhase1CSetup.DiggerResolutionMult);
                    masterT.GetMethod("CreateDirs")?.Invoke(master, null);
                }
                if (!had && !EditorApplication.ExecuteMenuItem("Tools/Digger/Setup for runtime"))
                { log.AppendLine($"{scene.name}: Digger menu 'Tools/Digger/Setup for runtime' not found"); return false; }
                var rt = Find(runtimeT);
                if (rt == null) { log.AppendLine($"{scene.name}: Digger runtime object missing after setup"); return false; }
                if (rt.GetComponent(surfaceT) == null) rt.gameObject.AddComponent(surfaceT);
                log.AppendLine($"{scene.name}: Digger ready");
                return true;
            }
            finally { if (prev.IsValid() && prev.isLoaded) SceneManager.SetActiveScene(prev); }
        }

        static System.Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(asm => asm.GetType(fullName)).FirstOrDefault(t => t != null);

        static List<CelestialBodyDefinition> LandableGenerated(out GalaxyDefinition galaxy)
        {
            galaxy = null;
            var list = new List<CelestialBodyDefinition>();
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.galaxy == null) return list;
            galaxy = config.galaxy;
            var pool = new List<string>(DMI.Xenoarch.Galaxy.ProceduralArtifactPools.V1);
            // Ensure must not leak DontSave objects into the open scene; it only creates ScriptableObjects.
            ProceduralGalaxy.Ensure(galaxy, pool);
            foreach (var sys in galaxy.AllSystems)
                foreach (var b in sys.AllBodies)
                    if (b != null && ProceduralGalaxy.IsGenerated(b.Id) && b.HasLanding)
                        list.Add(b);
            return list;
        }

        static void Remember(CelestialBodyDefinition b, string sceneName, string scenePath)
        {
            var reg = AssetDatabase.LoadAssetAtPath<BakedPlanetRegistry>(RegistryPath);
            if (reg == null) return;
            var e = reg.entries.FirstOrDefault(x => x != null && x.bodyId == b.Id);
            if (e == null) { e = new BakedPlanetRegistry.Entry(); reg.entries.Add(e); }
            e.bodyId = b.Id; e.sceneName = sceneName; e.scenePath = scenePath;
            EditorUtility.SetDirty(reg);
            BakedPlanetRegistry.UnloadCache();
        }

        static void RegisterBuildSettings(StringBuilder log)
        {
            var list = EditorBuildSettings.scenes.ToList();
            bool changed = false;
            var reg = AssetDatabase.LoadAssetAtPath<BakedPlanetRegistry>(RegistryPath);
            if (reg != null && reg.entries != null)
                foreach (var e in reg.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.scenePath) || !File.Exists(e.scenePath)) continue;
                    if (list.Any(s => s.path == e.scenePath)) continue;
                    list.Add(new EditorBuildSettingsScene(e.scenePath, true));
                    changed = true;
                    log.AppendLine("Build Settings: added " + e.scenePath);
                }
            if (changed) EditorBuildSettings.scenes = list.ToArray();
        }

        public static void EnsureSupportAssets(StringBuilder log)
        {
            XenoarchSetup.EnsureFolder("Assets/_Project/Resources/Galaxy");
            XenoarchSetup.EnsureFolder("Assets/_Project/Resources/World");
            if (AssetDatabase.LoadAssetAtPath<BakedPlanetRegistry>(RegistryPath) == null)
            {
                var reg = ScriptableObject.CreateInstance<BakedPlanetRegistry>();
                AssetDatabase.CreateAsset(reg, RegistryPath);
                log?.AppendLine("created " + RegistryPath);
            }
            var cat = AssetDatabase.LoadAssetAtPath<PlanetSurfaceCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<PlanetSurfaceCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
                log?.AppendLine("created " + CatalogPath);
            }
            FillCatalog(cat);
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(IceLayerPath);
            AssetDatabase.ImportAsset(CatalogPath);
            PlanetSurfaceCatalog.Invalidate();
            var iceEntry = PlanetSurfaceCatalog.For(ProceduralPlanetType.Ice);
            int iceLayers = iceEntry != null && iceEntry.layers != null ? iceEntry.layers.Count(l => l != null) : 0;
            log?.AppendLine($"ice catalog layers: {iceLayers}");
            XenoarchHeightProfiles.Ensure(log);
        }

        static void FillCatalog(PlanetSurfaceCatalog cat)
        {
            TerrainLayer Layer(string path) => AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            var mars = new[]
            {
                Layer("Assets/_Project/Data/Terrain/Layers/TL_Mars_Regolith.terrainlayer"),
                Layer("Assets/_Project/Data/Terrain/Mars_Soil.terrainlayer"),
                Layer("Assets/_Project/Data/Terrain/Layers/TL_Mars_Rock.terrainlayer"),
                Layer("Assets/_Project/Data/Terrain/Layers/TL_Mars_CrackedCrust.terrainlayer"),
            };
            var moon = new[]
            {
                Layer("Assets/_Project/Data/Terrain/Layers/TL_Moon_Regolith.terrainlayer"),
                Layer("Assets/_Project/Data/Terrain/Moon_Soil.terrainlayer"),
                Layer("Assets/_Project/Data/Terrain/Layers/TL_Moon_Rock.terrainlayer"),
            };
            var canyon = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Excavation.DigSiteStyle>("Assets/_Project/Data/Excavation/DigStyle_canyon_sediment.asset");
            var dune = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Excavation.DigSiteStyle>("Assets/_Project/Data/Excavation/DigStyle_dune_shrine.asset");
            var ice = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Excavation.DigSiteStyle>("Assets/_Project/Data/Excavation/DigStyle_ice_vault.asset");
            var cache = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Excavation.DigSiteStyle>("Assets/_Project/Data/Excavation/DigStyle_regolith_cache.asset");
            var iceSheet = EnsureIceTerrainLayer();
            var moonIce = new[] { moon[0], moon[1], moon[2], iceSheet };
            void Set(ProceduralPlanetType type, TerrainLayer[] layers, bool iceLayer, Color tint, int count, int clusters, DMI.Xenoarch.Excavation.DigSiteStyle major, DMI.Xenoarch.Excavation.DigSiteStyle cstyle)
            {
                var e = cat.entries.FirstOrDefault(x => x != null && x.type == type);
                if (e == null) { e = new PlanetSurfaceCatalog.Entry { type = type }; cat.entries.Add(e); }
                if (type == ProceduralPlanetType.Ice) { e.layers = layers; e.generateIceLayer = false; e.iceColor = new Color(0.78f, 0.88f, 0.95f); }
                else
                {
                    if (e.layers == null || e.layers.Length == 0) e.layers = layers;
                    e.generateIceLayer = iceLayer;
                }
                e.scatterTint = tint; e.scatterCount = count; e.slopeClusters = clusters;
                if (e.majorStyle == null) e.majorStyle = major;
                if (e.cacheStyle == null) e.cacheStyle = cstyle;
            }
            Set(ProceduralPlanetType.Desert, mars, false, new Color(1f, 0.62f, 0.43f), 440, 40, canyon, dune);
            Set(ProceduralPlanetType.Volcanic, mars, false, new Color(0.45f, 0.28f, 0.24f), 400, 50, canyon, dune);
            Set(ProceduralPlanetType.Ice, moonIce, false, new Color(0.86f, 0.9f, 0.95f), 360, 35, ice, cache);
            Set(ProceduralPlanetType.Barren, moon, false, new Color(0.86f, 0.86f, 0.88f), 480, 45, cache, cache);
            Set(ProceduralPlanetType.Rocky, moon, false, new Color(0.7f, 0.64f, 0.56f), 400, 40, cache, cache);
        }


        const string IceLayerPath = "Assets/_Project/Data/Terrain/Layers/TL_Ice_Sheet.terrainlayer";
        const string IceTexPath = "Assets/_Project/Data/Terrain/Layers/TL_Ice_Sheet.png";

        static string ToAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string p = path.Replace("\\", "/");
            int i = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return p.Substring(i + 1);
            if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return p;
            return p;
        }

        static TerrainLayer EnsureIceTerrainLayer()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(IceLayerPath);
            if (existing != null) return existing;
            XenoarchSetup.EnsureFolder("Assets/_Project/Data/Terrain/Layers");
            if (!File.Exists(IceTexPath))
            {
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "TL_Ice_Sheet", wrapMode = TextureWrapMode.Repeat };
                var px = new Color[64 * 64];
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float n = 0.92f + 0.16f * Mathf.PerlinNoise(x * 0.17f, y * 0.17f);
                        var c = new Color(0.78f * n, 0.88f * n, 0.95f * n, 1f);
                        px[x + y * 64] = c;
                    }
                tex.SetPixels(px);
                File.WriteAllBytes(IceTexPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(IceTexPath);
                if (AssetImporter.GetAtPath(IceTexPath) is TextureImporter imp)
                {
                    imp.sRGBTexture = true;
                    imp.alphaSource = TextureImporterAlphaSource.None;
                    imp.wrapMode = TextureWrapMode.Repeat;
                    imp.SaveAndReimport();
                }
            }
            var layer = new TerrainLayer
            {
                name = "TL_Ice_Sheet",
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(IceTexPath),
                tileSize = new Vector2(14f, 14f),
                metallic = 0f,
                smoothness = 0f,
                specular = Color.black,
                smoothnessSource = TerrainLayerSmoothnessSource.Constant,
            };
            AssetDatabase.CreateAsset(layer, IceLayerPath);
            return AssetDatabase.LoadAssetAtPath<TerrainLayer>(IceLayerPath);
        }

        static void PersistProceduralLayers(TerrainData data, string terrainPath)
        {
            if (data == null) return;
            var layers = data.terrainLayers;
            if (layers == null || layers.Length == 0) return;
            var ice = EnsureIceTerrainLayer();
            bool changed = false;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null)
                {
                    layers[i] = ice;
                    changed = true;
                    continue;
                }
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(layers[i]))) continue;
                if (layers[i].name != null && layers[i].name.IndexOf("Ice", StringComparison.OrdinalIgnoreCase) >= 0)
                { layers[i] = ice; changed = true; }
            }
            if (!changed) return;
            data.terrainLayers = layers;
            EditorUtility.SetDirty(data);
            if (!string.IsNullOrEmpty(terrainPath)) AssetDatabase.SaveAssetIfDirty(data);
        }

        static bool RemapDiggerFolder(string scenePath, StringBuilder log)
        {
            string text = File.ReadAllText(scenePath);
            const string marker = "basePathData: ";
            int i = text.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) { log.AppendLine($"{Path.GetFileNameWithoutExtension(scenePath)}: no Digger path"); return false; }
            int start = i + marker.Length;
            int end = text.IndexOfAny(new[] { '\r', '\n' }, start);
            if (end < 0) end = text.Length;
            string current = text.Substring(start, end - start).Trim();
            if (current.IndexOf("Untitled", StringComparison.OrdinalIgnoreCase) < 0) return false;

            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string norm = current.Replace("\\", "/");
            const string prefix = "Assets/DiggerData/Scenes/";
            int rel = norm.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (rel < 0) { log.AppendLine($"{sceneName}: unexpected Digger path {current}"); return false; }
            string rest = norm.Substring(rel + prefix.Length);
            string oldFolder = rest.Split('/')[0];
            if (string.IsNullOrEmpty(oldFolder) || oldFolder == sceneName) return false;

            string src = prefix + oldFolder;
            string dest = prefix + sceneName;
            if (AssetDatabase.IsValidFolder(src))
            {
                if (AssetDatabase.IsValidFolder(dest))
                    log.AppendLine($"{sceneName}: {dest} already exists, left {src} in place and rewired the scene");
                else
                {
                    string err = AssetDatabase.MoveAsset(src, dest);
                    if (!string.IsNullOrEmpty(err)) { log.AppendLine($"{sceneName}: move failed: {err}"); return false; }
                }
            }
            else log.AppendLine($"{sceneName}: source folder missing ({src}), rewiring path only");

            text = text.Replace("sceneDataFolder: " + oldFolder, "sceneDataFolder: " + sceneName);
            text = text.Replace("DiggerData\\Scenes\\" + oldFolder + "\\", "DiggerData\\Scenes\\" + sceneName + "\\");
            text = text.Replace("DiggerData/Scenes/" + oldFolder + "/", "DiggerData/Scenes/" + sceneName + "/");
            File.WriteAllText(scenePath, text);
            AssetDatabase.ImportAsset(scenePath);
            log.AppendLine($"{sceneName}: Digger data {oldFolder} -> {sceneName}");
            return true;
        }

        static int DeleteOrphanUntitledFolders(StringBuilder log)
        {
            const string scenesBase = "Assets/DiggerData/Scenes";
            if (!AssetDatabase.IsValidFolder(scenesBase)) return 0;
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories))
            {
                string txt = File.ReadAllText(scene);
                int idx = 0;
                while ((idx = txt.IndexOf("DiggerData", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int lineEnd = txt.IndexOfAny(new[] { '\r', '\n' }, idx);
                    if (lineEnd < 0) lineEnd = txt.Length;
                    referenced.Add(txt.Substring(idx, lineEnd - idx).Replace("\\", "/"));
                    idx = lineEnd;
                }
            }
            int deleted = 0;
            foreach (var folder in AssetDatabase.GetSubFolders(scenesBase))
            {
                string name = Path.GetFileName(folder);
                if (name == null || !name.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase)) continue;
                if (name == "Moon_Excavation_01" || name == "Mars_Excavation_01") continue;
                bool used = referenced.Any(r => r.IndexOf("/" + name + "/", StringComparison.OrdinalIgnoreCase) >= 0 || r.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
                if (used) { log.AppendLine($"kept {folder} (still referenced)"); continue; }
                if (AssetDatabase.DeleteAsset(folder)) { log.AppendLine("deleted leftover " + folder); deleted++; }
            }
            return deleted;
        }

        static string SceneNameFor(CelestialBodyDefinition b)
        {
            string slug = (b.Id ?? "body").StartsWith("body.") ? b.Id.Substring(5) : b.Id;
            return "Surface_" + slug.Replace('.', '_').Replace(' ', '_');
        }

        static bool IsUserOpenScene(string path)
        {
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && string.Equals(active.path, path, StringComparison.OrdinalIgnoreCase)) return true;
            // If it is the only loaded scene and already open, treat it as the user's scene.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && string.Equals(s.path, path, StringComparison.OrdinalIgnoreCase) && s == active) return true;
            }
            return false;
        }

        static List<string> OtherTerrainScenes(Scene? except)
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded || (except.HasValue && s == except.Value)) continue;
                if (s.GetRootGameObjects().Any(r => r.GetComponentInChildren<Terrain>(true) != null)) names.Add(s.name);
            }
            return names;
        }
    }
}