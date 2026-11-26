using System;
using System.IO;
using System.Linq;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// t186u (Phase 1 close-out): creates the shared landing scene of generated planets (Surface_Procedural: one
    /// ProceduralSurface object, a sun and a player rig; the terrain is built at runtime) additively, so the open scene is
    /// untouched; adds it to Build Settings; and turns the Mars navigation core into the ship's navigation drive
    /// (installTarget NavigationDrive). Idempotent. Trigger: create Temp/xeno_phase1e.trig (log: Logs/xeno_phase1e.txt).
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchPhase1ESetup
    {
        const string Trigger = "Temp/xeno_phase1e.trig";
        const string RoverTrigger = "Temp/xeno_rover.trig";
        const string RoverBakeTrigger = "Temp/xeno_rover_bake.trig";
        const string RoverItemPath = "Assets/_Project/Data/Items/Item_vehicle_grasshopper.asset";
        const string GrasshopperGuid = "73a3d6dceb2a6f747b43ee02c5144d36"; // Assets/_Project/Prefabs/Vehicles and Ships/Grasshoper .prefab
        const string LogFile = "Logs/xeno_phase1e.txt";
        const string NavCorePath = "Assets/_Project/Data/Artifacts/Artifact_mars_navigation_core.asset";
        const string TerrainLitPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";

        static XenoarchPhase1ESetup() => EditorApplication.update += Poll;

        static double _next;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (File.Exists(Trigger)) { File.Delete(Trigger); Run(); }
            if (File.Exists(RoverBakeTrigger)) { File.Delete(RoverBakeTrigger); BakeSceneRover(); }
            if (File.Exists(RoverTrigger)) { File.Delete(RoverTrigger); try { RoverItem(); AssetDatabase.SaveAssets(); Log("rover done"); } catch (Exception e) { Log("FAILED " + e); } }
        }

        static void Log(string s)
        {
            Debug.Log("[Xenoarch] 1E setup: " + s);
            try { Directory.CreateDirectory("Logs"); File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {s}\n"); } catch { }
        }

        [MenuItem("Xenoarch/Setup/Phase 1E: Procedural Surface + Navigation Drive", priority = 60)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("exit Play Mode first"); return; }
            try
            {
                Scene();
                BuildSettings();
                NavDrive();
                RoverItem();
                AssetDatabase.SaveAssets();
                Log("done");
            }
            catch (Exception e) { Log("FAILED " + e); }
        }

        static void Scene()
        {
            string path = XenoarchSetup.ScenePath(SceneNames.ProceduralSurface);
            if (File.Exists(path)) { Log($"{path} exists, kept"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == path) { Log($"{path} is open, skipped"); return; }

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.16f, 0.15f, 0.15f);

                var root = new GameObject("[Procedural Surface]");
                var ps = root.AddComponent<ProceduralSurface>();
                ps.terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainLitPath);

                var sun = new GameObject("Sun", typeof(Light));
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                var l = sun.GetComponent<Light>();
                l.type = LightType.Directional; l.color = new Color(1f, 0.95f, 0.88f); l.intensity = 1.3f; l.shadows = LightShadows.Soft;
                RenderSettings.sun = l;

                // Player rig (every landing scene has one; the shuttle landing moves it next to the shuttle).
                XenoarchPlayerBuilder.InstantiatePlayer(new Vector3(0f, 80f, -40f), 0f, 9.81f);

                EditorSceneManager.SaveScene(scene, path);
                Log($"created {path} (terrain material {(ps.terrainMaterial != null ? ps.terrainMaterial.name : "runtime Shader.Find")})");
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void BuildSettings()
        {
            string path = XenoarchSetup.ScenePath(SceneNames.ProceduralSurface);
            if (!File.Exists(path)) return;
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) { Log("Build Settings already has " + path); return; }
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            Log($"Build Settings: added {path} ({list.Count} scenes)");
        }

        /// <summary>t187u: the Grasshopper rover as an inventory item (Deploy on planet / moon surfaces). The prefab is
        /// referenced, never modified.</summary>
        static void RoverItem()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(GrasshopperGuid));
            var item = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Vehicles.VehicleItemDefinition>(RoverItemPath);
            bool created = item == null;
            if (created) { item = ScriptableObject.CreateInstance<DMI.Xenoarch.Vehicles.VehicleItemDefinition>(); AssetDatabase.CreateAsset(item, RoverItemPath); item.baseValue = 18000; }
            item.SetIdForTooling("item.vehicle.grasshopper");
            item.displayName = "Grasshopper Rover";
            item.description = "Surface rover, packed for transport. Deploy it on a planet or moon surface (not aboard the ship or a station). Enter through the rear door.";
            item.category = DMI.Xenoarch.Items.ItemCategory.Equipment; item.maxStack = 1; item.uniqueInstance = false; item.mass = 0f;
            if (prefab != null) item.vehiclePrefab = prefab;
            EditorUtility.SetDirty(item);
            var cfg = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            var reg = cfg != null ? cfg.itemRegistry : AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Items.ItemRegistry>(XenoarchSetup.RegistryPath);
            if (reg != null)
            {
                var entries = reg.Entries.Where(e => e != null).ToList();
                if (!entries.Any(e => e.Id == item.Id)) { entries.Add(item); reg.SetEntries(entries); EditorUtility.SetDirty(reg); }
            }
            Log($"rover item {(created ? "created" : "updated")} ({RoverItemPath}), prefab {(prefab != null ? AssetDatabase.GetAssetPath(prefab) : "NOT FOUND (guid " + GrasshopperGuid + ")")}, registry {(reg != null ? reg.name : "missing")}");
        }

        /// <summary>t195u: set up the Grasshopper instance in the open scene (his half-size edit kept) and apply it to the
        /// prefab: rover component, rigidbody, wheel colliders, chassis, entry zone, seat / door / COM markers. The scene
        /// itself is not saved.</summary>
        [MenuItem("Xenoarch/Setup/Rover: Set Up Scene Grasshopper + Apply To Prefab", priority = 61)]
        public static void BakeSceneRover()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("rover bake: exit Play Mode first"); return; }
            try
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(GrasshopperGuid);
                GameObject inst = null; int count = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var sc = SceneManager.GetSceneAt(i);
                    if (!sc.isLoaded) continue;
                    foreach (var root in sc.GetRootGameObjects())
                        foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        {
                            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) != prefabPath) continue;
                            count++; if (inst == null) inst = t.gameObject;
                        }
                }
                if (inst == null) { Log($"rover bake: no instance of {prefabPath} in the open scene(s)"); return; }
                var tr = inst.transform;
                Log($"rover bake: instance '{inst.name}' in {inst.scene.name} ({count} found) pos {tr.position} rot {tr.eulerAngles} scale {tr.localScale} lossy {tr.lossyScale}; overrides {PrefabUtility.GetObjectOverrides(inst).Count}, added objects {PrefabUtility.GetAddedGameObjects(inst).Count}, scene dirty {inst.scene.isDirty}");
                var rover = inst.GetComponent<DMI.Xenoarch.Vehicles.RoverVehicle>();
                if (rover == null) rover = Undo.AddComponent<DMI.Xenoarch.Vehicles.RoverVehicle>(inst);
                Undo.RegisterFullObjectHierarchyUndo(inst, "Rover setup");
                rover.Bake();
                EditorUtility.SetDirty(rover);
                var w0 = rover.wheels.Count > 0 && rover.wheels[0].collider != null ? rover.wheels[0].collider.radius : 0f;
                Log($"rover bake: door {(rover.door ? rover.door.name : "MISSING")} open {rover.doorOpen.eulerAngles}, chair {(rover.driverChair ? rover.driverChair.name + " @" + rover.driverChair.position : "MISSING")}, console {(rover.console ? rover.console.name : "MISSING")}, wheels {rover.wheels.Count} (r {w0:0.###} m, front {rover.wheels.FindAll(w => w.front).Count}), seat {rover.seat.position} eye {rover.seatedEyeHeight:0.##}, chassis {rover.chassis.size}");
                PrefabUtility.ApplyPrefabInstance(inst, InteractionMode.AutomatedAction);
                AssetDatabase.SaveAssets();
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Log($"rover bake: applied to {prefabPath} (prefab root scale {asset.transform.localScale}, RoverVehicle {(asset.GetComponent<DMI.Xenoarch.Vehicles.RoverVehicle>() != null)}); scene NOT saved");
            }
            catch (Exception e) { Log("rover bake FAILED " + e); }
        }

        static void NavDrive()
        {
            var a = AssetDatabase.LoadAssetAtPath<ArtifactDefinition>(NavCorePath);
            if (a == null) { Log("navigation core asset not found at " + NavCorePath); return; }
            if (a.installTarget == TechInstallTarget.NavigationDrive && a.installBonus == 1) { Log("navigation core already a navigation drive"); return; }
            a.installTarget = TechInstallTarget.NavigationDrive; a.installBonus = 1;
            EditorUtility.SetDirty(a);
            Log("navigation core: installTarget NavigationDrive, +1 tier");
        }
    }
}
