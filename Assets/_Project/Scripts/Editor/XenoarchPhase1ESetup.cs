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
        const string RoverMeasureTrigger = "Temp/xeno_rover_measure.trig";
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
            if (File.Exists(RoverMeasureTrigger)) { File.Delete(RoverMeasureTrigger); MeasureSceneRover(); }
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

        static GameObject FindSceneRover(out int count)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(GrasshopperGuid);
            GameObject inst = null; count = 0;
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
            return inst;
        }

        /// <summary>t196u: read-only fit check of the player against the scene Grasshopper (nothing is changed).</summary>
        [MenuItem("Xenoarch/Setup/Rover: Measure Player Fit (read-only)", priority = 62)]
        public static void MeasureSceneRover()
        {
            try
            {
                var inst = FindSceneRover(out _);
                if (inst == null) { Log("rover measure: no scene instance"); return; }
                var rover = inst.GetComponent<DMI.Xenoarch.Vehicles.RoverVehicle>();
                if (rover == null || rover.seat == null) { Log("rover measure: not baked"); return; }
                var tr = inst.transform;

                // Player capsule from the XA_Player prefab.
                float ph = 1.8f, pr = 0.3f, eye = 1.6f;
                var guid = AssetDatabase.FindAssets("XA_Player t:Prefab").FirstOrDefault();
                if (guid != null)
                {
                    var pp = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    var cc = pp.GetComponent<CharacterController>();
                    if (cc != null) { ph = cc.height * pp.transform.localScale.y; pr = cc.radius * pp.transform.localScale.x; }
                    var cam = pp.GetComponentInChildren<Camera>(true);
                    if (cam != null) eye = pp.transform.InverseTransformPoint(cam.transform.position).y;
                }
                Log($"rover measure: player capsule height {ph:0.00} radius {pr:0.00} standing eye {eye:0.00}; rover seated eye {rover.seatedEyeHeight:0.00}; rover scale {tr.lossyScale.x:0.###}");

                // Body bounds and what sets the top.
                var rends = inst.GetComponentsInChildren<Renderer>(true);
                var body = rends[0].bounds; foreach (var r in rends) body.Encapsulate(r.bounds);
                Log($"rover measure: body bounds size {body.size} min.y {body.min.y:0.00} max.y {body.max.y:0.00}");
                foreach (var r in rends.OrderByDescending(r => r.bounds.max.y).Take(5))
                    Log($"rover measure:   top renderer {PathOf(r.transform, tr)} max.y {r.bounds.max.y:0.00} size {r.bounds.size}");

                Physics.SyncTransforms();
                var rig = rover.rig;
                bool Mine(Collider c) => c != null && !c.isTrigger && c.transform.IsChildOf(tr) && (rig == null || !c.transform.IsChildOf(rig));
                float Cast(Vector3 o, Vector3 d, float max, out string what)
                {
                    what = "none"; float best = max;
                    foreach (var h in Physics.RaycastAll(o, d, max, ~0, QueryTriggerInteraction.Ignore))
                        if (Mine(h.collider) && h.distance < best) { best = h.distance; what = PathOf(h.collider.transform, tr); }
                    return best;
                }
                int mine = inst.GetComponentsInChildren<Collider>(true).Count(c => Mine(c) && c.enabled && c.gameObject.activeInHierarchy);
                Log($"rover measure: {mine} active solid rover colliders");

                Vector3 fwd = rig != null ? rig.forward : tr.forward; fwd.y = 0; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);

                // Seat: floor, roof, side clearance.
                var s = rover.seat.position;
                float down = Cast(s + Vector3.up * 0.5f, Vector3.down, 3f, out var fl);
                float floorY = s.y + 0.5f - down;
                float up = Cast(new Vector3(s.x, floorY + 0.05f, s.z), Vector3.up, 10f, out var roof);
                var chairB = rover.driverChair != null ? BoundsOf(rover.driverChair) : new Bounds(s, Vector3.zero);
                float seatTop = chairB.min.y + Mathf.Min(0.5f, chairB.size.y);
                float l = Cast(new Vector3(s.x, floorY + 1.0f, s.z), -right, 5f, out var lw), rr = Cast(new Vector3(s.x, floorY + 1.0f, s.z), right, 5f, out var rw);
                Log($"rover measure: seat floor {floorY:0.00} (hit {fl}, seat marker y {s.y:0.00}); floor->roof {up + 0.05f:0.00} m (hit {roof}); chair size {chairB.size} (across {Mathf.Abs(Vector3.Dot(chairB.size, right)):0.00}); seated head top ~{rover.seatedEyeHeight + 0.15f:0.00} above floor; side clearance L {l:0.00} ({lw}) R {rr:0.00} ({rw})");

                // Rear doorway: height and width just inside the door.
                if (rover.door != null && rover.doorInside != null)
                {
                    var di = rover.doorInside.position;
                    foreach (float off in new[] { -0.6f, -0.3f, 0f, 0.4f })
                    {
                        var p = di + fwd * off;
                        float d2 = Cast(p + Vector3.up * 0.9f, Vector3.down, 3f, out var f2);
                        float fy = p.y + 0.9f - d2;
                        float h2 = Cast(new Vector3(p.x, fy + 0.05f, p.z), Vector3.up, 10f, out var r2);
                        var mid = new Vector3(p.x, fy + 0.9f, p.z);
                        float a = Cast(mid, -right, 5f, out var aw), b = Cast(mid, right, 5f, out var bw);
                        Log($"rover measure: doorway {off:+0.0;-0.0} m from DoorInside: floor {fy:0.00} ({f2}), height {h2 + 0.05f:0.00} ({r2}), width {a + b:0.00} (L {aw} / R {bw})");
                    }
                }

                // Path door -> seat: capsule overlap every 0.25 m (standing player), stopping 0.6 m short of the seat.
                if (rover.doorInside != null)
                {
                    var a0 = rover.doorInside.position; var b0 = s;
                    float len = Vector3.Distance(new Vector3(a0.x, 0, a0.z), new Vector3(b0.x, 0, b0.z));
                    int blocked = 0, n = 0; var names = new System.Collections.Generic.HashSet<string>();
                    float minHead = 99f;
                    for (float t = 0; t <= Mathf.Max(0f, len - 0.6f); t += 0.25f, n++)
                    {
                        var p = Vector3.Lerp(a0, b0, len > 0 ? t / len : 0);
                        float d2 = Cast(p + Vector3.up * 0.9f, Vector3.down, 3f, out _);
                        float fy = p.y + 0.9f - d2;
                        float h2 = Cast(new Vector3(p.x, fy + 0.05f, p.z), Vector3.up, 10f, out _) + 0.05f;
                        minHead = Mathf.Min(minHead, h2);
                        var c1 = new Vector3(p.x, fy + pr + 0.05f, p.z); var c2 = new Vector3(p.x, fy + ph - pr, p.z);
                        bool hit = false;
                        foreach (var c in Physics.OverlapCapsule(c1, c2, pr, ~0, QueryTriggerInteraction.Ignore))
                            if (Mine(c)) { hit = true; names.Add(PathOf(c.transform, tr)); }
                        if (hit) blocked++;
                    }
                    Log($"rover measure: path door->seat {len:0.00} m, {n} samples, {blocked} blocked by a standing capsule; lowest floor->roof on path {minHead:0.00} m; blockers: {string.Join(", ", names)}");
                }
            }
            catch (Exception e) { Log("rover measure FAILED " + e); }
        }

        static Bounds BoundsOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static string PathOf(Transform t, Transform root)
        {
            var s = t.name;
            while (t.parent != null && t != root && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
            return s;
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
