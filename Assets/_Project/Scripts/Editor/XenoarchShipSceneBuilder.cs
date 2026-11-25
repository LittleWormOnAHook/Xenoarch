using System.Collections.Generic;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1B, additive pass over Ship_Starter. Anthony's layout is authoritative: this never moves, re-parents,
    /// resets or deletes anything. It only swaps placeholder components for working ones on the same GameObjects,
    /// adds new objects (airlock spawn, volume) and component overrides (door wrappers, light modes).
    /// Refuses to run unless Ship_Starter is open and saved (not dirty).
    /// </summary>
    public static class XenoarchShipSceneBuilder
    {
        public const string NavName = "Navigation Terminal";
        public const string ScanName = "Orbital Scanning Terminal";
        public const string AirlockOldName = "Airlock [DEBUG: Moon surface]";
        public const string AirlockName = "Airlock";
        public const string VolumeName = "Ship Interior Volume (Phase 1B)";
        public const int MaxShadowLights = 4;

        [MenuItem("Xenoarch/Setup/8. Ship_Starter: add Phase 1B systems (additive, open+saved scene)", priority = 28)]
        public static void Run()
        {
            var log = new StringBuilder();
            bool ok = Apply(log);
            Debug.Log($"[Xenoarch] Ship 1B pass {(ok ? "done" : "SKIPPED")}:\n{log}");
            System.IO.File.WriteAllText("Logs/xenoarch_ship_1b.txt", log.ToString());
        }

        public static bool Apply(StringBuilder log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { log.AppendLine("Exit Play Mode first."); return false; }
            var scene = SceneManager.GetActiveScene();
            if (scene.name != SceneNames.ShipStarter) { log.AppendLine($"Active scene is '{scene.name}', open Ship_Starter."); return false; }
            if (scene.isDirty) { log.AppendLine("Ship_Starter has unsaved changes: save it first (your layout is authoritative)."); return false; }
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();

            // 1) Terminals: swap placeholder -> working component, same GameObject (transform untouched).
            SwapStation<NavigationTerminal>(all, NavName, "Navigation Terminal", log);
            SwapStation<OrbitalScannerTerminal>(all, ScanName, "Orbital Scanning Terminal", log);

            // 2) Airlock: debug Moon travel -> real airlock (disembarks wherever the ship is landed/docked).
            var airlock = all.FirstOrDefault(g => g.name == AirlockOldName) ?? all.FirstOrDefault(g => g.name == AirlockName && g.GetComponent<ShipAirlockInteractable>() != null);
            if (airlock != null)
            {
                foreach (var t in airlock.GetComponents<SceneTravelInteractable>()) Object.DestroyImmediate(t, true);
                if (airlock.GetComponent<ShipAirlockInteractable>() == null) airlock.AddComponent<ShipAirlockInteractable>();
                if (airlock.name != AirlockName)
                {
                    RenameLabel(all, airlock.name, AirlockName);
                    airlock.name = AirlockName;
                }
                log.AppendLine($"Airlock: ShipAirlockInteractable on '{Path(airlock.transform)}' (position kept)");

                // Arrival spawn for 'return to ship': beside the airlock, on the floor, facing into the ship.
                if (!all.Any(g => g.TryGetComponent(out PlayerSpawnPoint sp) && sp.spawnId == DMI.Xenoarch.Galaxy.ShipTravel.ShipSpawnAirlock))
                {
                    var def = all.Select(g => g.GetComponent<PlayerSpawnPoint>()).FirstOrDefault(s => s != null && s.spawnId == PlayerSpawnIds.Default);
                    Vector3 a = airlock.transform.position;
                    Vector3 toward = def != null ? def.transform.position - a : -airlock.transform.forward;
                    toward.y = 0f; toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
                    Vector3 p = a + toward * 1.2f;
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
                    else if (def != null) p.y = def.transform.position.y;
                    var go = new GameObject($"Spawn_{DMI.Xenoarch.Galaxy.ShipTravel.ShipSpawnAirlock}");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(toward, Vector3.up));
                    go.AddComponent<PlayerSpawnPoint>().spawnId = DMI.Xenoarch.Galaxy.ShipTravel.ShipSpawnAirlock;
                    Undo.RegisterCreatedObjectUndo(go, "Airlock spawn");
                    log.AppendLine($"Added Spawn_airlock at {p} (check it in the Scene view)");
                }
            }
            else log.AppendLine($"Airlock: no '{AirlockOldName}' found, skipped");

            // 3) Doors. Root cause of "audio plays but doors don't open": the kit marks every ship object Static
            //    (incl. Batching Static), so the door leaves are merged into a static batch at Play start and moving
            //    their transforms no longer moves the rendered mesh. Clear static flags on the moving leaves (scene
            //    instance override, vendor prefab untouched) and add the XenoarchDoor wrapper.
            int doors = 0, leaves = 0;
            foreach (var g in all)
            {
                Component vendor = g.GetComponent("DoorDoubleSlide");
                if (vendor == null) vendor = g.GetComponent("DoorDoubleRotate");
                if (vendor == null) continue;
                foreach (var f in new[] { "doorL", "doorR" })
                {
                    var leaf = vendor.GetType().GetField(f)?.GetValue(vendor) as Transform;
                    if (leaf == null) continue;
                    foreach (var t in leaf.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) { Undo.RecordObject(t.gameObject, "Door leaf static"); GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); leaves++; }
                }
                if (g.GetComponent<XenoarchDoor>() == null) { Undo.AddComponent<XenoarchDoor>(g); doors++; }
            }
            log.AppendLine($"Doors: XenoarchDoor added to {doors} door(s); static flags cleared on {leaves} door-leaf object(s)");

            // 4) Lights in ship instances: Mixed mode, shadows only on the strongest few (no transforms touched, no bake run).
            var lights = all.Where(g => g.activeInHierarchy).Select(g => g.GetComponent<Light>()).Where(l => l != null && l.type != LightType.Directional && PrefabUtility.IsPartOfPrefabInstance(l)).ToList();
            var shadowers = lights.OrderByDescending(l => l.intensity * l.range).Take(MaxShadowLights).ToHashSet();
            foreach (var l in lights)
            {
                Undo.RecordObject(l, "Ship lights");
                l.lightmapBakeType = LightmapBakeType.Mixed;
                l.shadows = shadowers.Contains(l) ? LightShadows.Soft : LightShadows.None;
            }
            log.AppendLine($"Lights: {lights.Count} ship light(s) set to Mixed; shadows kept on {shadowers.Count} ({string.Join(", ", shadowers.Select(s => s.name))}). Bake lighting when the layout settles.");

            // 5) URP Volume with the ship interior profile (editor preview; runtime also applies it via the environment).
            var vp = XenoarchPhase1BSetup.EnsureShipVolumeProfile();
            if (!all.Any(g => g.name == VolumeName))
            {
                var vgo = new GameObject(VolumeName);
                SceneManager.MoveGameObjectToScene(vgo, scene);
                var vol = vgo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1f; vol.sharedProfile = vp;
                Undo.RegisterCreatedObjectUndo(vgo, "Ship volume");
                log.AppendLine("Added global Volume (VP_Ship_Interior)");
            }

            // 6) Scene Lighting skybox: the scene's own sky is the open-space sky (Anthony's call); only fill it if empty.
            var cfg = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (cfg != null && cfg.defaultSpaceSkybox != null && RenderSettings.skybox == null)
            {
                RenderSettings.skybox = cfg.defaultSpaceSkybox;
                DynamicGI.UpdateEnvironment();
                log.AppendLine($"Lighting skybox = {cfg.defaultSpaceSkybox.name}");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("Saved Ship_Starter.");
            return true;
        }

        internal static void SwapStation<T>(List<GameObject> all, string objectName, string terminalName, StringBuilder log) where T : DMI.Xenoarch.UI.TerminalWindow
        {
            // Every placeholder with this stationName (Anthony places copies on ship props, e.g. the cockpit holo-table
            // inside the Mark 1 instance), else an object with that name. Each is swapped in place.
            var targets = all.Where(g => g.GetComponents<StationPlaceholderInteractable>().Any(p => p.stationName == objectName)).ToList();
            if (targets.Count == 0) targets = all.Where(g => g.name == objectName && g.GetComponent<T>() == null).ToList();
            if (targets.Count == 0) { log.AppendLine($"{objectName}: no placeholder left ({all.Count(g => g.GetComponent<T>() != null)} working)"); return; }
            foreach (var t in targets) SwapOne<T>(t, objectName, terminalName, log);
        }

        static void SwapOne<T>(GameObject go, string objectName, string terminalName, StringBuilder log) where T : DMI.Xenoarch.UI.TerminalWindow
        {
            if (go.GetComponent<T>() != null && go.GetComponent<StationPlaceholderInteractable>() == null) { log.AppendLine($"{objectName}: already {typeof(T).Name} at '{Path(go.transform)}'"); return; }

            var placeholder = go.GetComponents<StationPlaceholderInteractable>().FirstOrDefault(p => p.stationName == objectName) ?? go.GetComponent<StationPlaceholderInteractable>();
            if (placeholder != null && PrefabUtility.GetCorrespondingObjectFromSource(placeholder) != null)
            {
                // Component lives in a prefab asset: swap it in the asset (component only; transforms untouched).
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(placeholder);
                string assetPath = AssetDatabase.GetAssetPath(src);
                if (assetPath.StartsWith("Assets/_Creepy_Cat")) { log.AppendLine($"{objectName}: placeholder is inside a vendor prefab ({assetPath}); skipped, move it to an own prefab"); return; }
                var contents = PrefabUtility.LoadPrefabContents(assetPath);
                try
                {
                    foreach (var p in contents.GetComponentsInChildren<StationPlaceholderInteractable>(true))
                    {
                        if (p.stationName != placeholder.stationName) continue;
                        var host = p.gameObject;
                        Object.DestroyImmediate(p);
                        var t = host.GetComponent<T>(); if (t == null) t = host.AddComponent<T>();
                        t.terminalName = terminalName;
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                log.AppendLine($"{objectName}: {typeof(T).Name} swapped into prefab asset {assetPath} (instance '{Path(go.transform)}', position kept)");
                return;
            }
            if (placeholder != null) Undo.DestroyObjectImmediate(placeholder); // scene object or added-component override
            var term = go.GetComponent<T>();
            if (term == null) term = Undo.AddComponent<T>(go);
            term.terminalName = terminalName;
            EditorUtility.SetDirty(go);
            log.AppendLine($"{objectName}: {typeof(T).Name} at '{Path(go.transform)}' (position kept)");
        }

        static void RenameLabel(List<GameObject> all, string oldText, string newText)
        {
            foreach (var g in all)
                if (g.TryGetComponent(out TextMesh tm) && tm.text == oldText) { Undo.RecordObject(tm, "Label"); tm.text = newText; }
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        /// <summary>Smoke checks on the saved Ship_Starter (read from disk, no scene opened).</summary>
        public static void Validate(System.Action<bool, string> check)
        {
            string path = XenoarchSetup.ScenePath(SceneNames.ShipStarter);
            if (!System.IO.File.Exists(path)) return;
            string text = System.IO.File.ReadAllText(path);
            string Guid<T>() where T : MonoBehaviour
            {
                foreach (var g in AssetDatabase.FindAssets($"t:MonoScript {typeof(T).Name}"))
                {
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                    if (ms != null && ms.GetClass() == typeof(T)) return g;
                }
                return null;
            }
            bool InScene<T>() where T : MonoBehaviour { var g = Guid<T>(); return g != null && text.Contains(g); }
            if (!InScene<NavigationTerminal>()) { check(true, "1B ship: Ship_Starter not yet upgraded (run Setup 8 after saving the scene) - skipped"); return; }
            check(InScene<NavigationTerminal>(), "1B ship: Navigation Terminal working component in Ship_Starter");
            check(InScene<OrbitalScannerTerminal>(), "1B ship: Orbital Scanning Terminal working component in Ship_Starter");
            check(InScene<ShipAirlockInteractable>(), "1B ship: airlock component in Ship_Starter");
            check(!System.Text.RegularExpressions.Regex.IsMatch(text, "stationName: (Navigation Terminal|Orbital Scanning Terminal)\\s"), "1B ship: no Navigation/Orbital placeholder left (all copies swapped)");
            check(text.Contains("spawnId: airlock"), "1B ship: airlock arrival spawn in Ship_Starter");
        }
    }
}
