using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Scenes;
using DMI.Xenoarch.UI;
using DMI.Xenoarch.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 1A setup: foundation assets (config, item registry, starter items, placeholder materials) and the six
    /// placeholder scenes + Build Settings. Idempotent: existing assets/scenes are kept unless you choose Rebuild.
    /// Scenes are created additively and closed again, so the scene you have open is not touched.
    /// </summary>
    public static class XenoarchSetup
    {
        public const string Root = "Assets/_Project";
        public const string ScenesFolder = Root + "/Scenes";
        public const string ItemsFolder = Root + "/Data/Items";
        public const string MaterialsFolder = Root + "/Art/Materials";
        public const string ResourcesFolder = Root + "/Resources";
        public const string ConfigPath = ResourcesFolder + "/" + XenoarchConfig.ResourcePath + ".asset";
        public const string RegistryPath = Root + "/Data/ItemRegistry.asset";

        public static string ScenePath(string name) => $"{ScenesFolder}/{name}.unity";

        [MenuItem("Xenoarch/Setup/Run Full Phase 1A Setup", priority = 0)]
        public static void RunAll()
        {
            CreateFoundationAssets();
            BuildPlaceholderScenes(false);
            UpdateBuildSettings();
            Debug.Log("[Xenoarch] Setup: Phase 1A setup complete");
        }

        [MenuItem("Xenoarch/Setup/1. Create Foundation Assets", priority = 20)]
        public static void CreateFoundationAssets()
        {
            foreach (var f in new[] { ScenesFolder, ItemsFolder, MaterialsFolder, ResourcesFolder, Root + "/Prefabs" }) EnsureFolder(f);

            var items = new List<ItemDefinition>
            {
                Item("item.tool.handheld_scanner", "Handheld Surface Scanner Mk I", ItemCategory.Tool, 1, false, 2.5f, 400, "Basic handheld scanner for locating buried objects in a small area."),
                Item("item.tool.excavation_shovel", "Sonic Excavator", ItemCategory.Tool, 1, false, 2f, 60, "Sonic head that shakes loose material fast. Switch to Fine focus near fragile objects."),
                Item("item.tool.precision_tool", "Excavator Fine Focus", ItemCategory.Tool, 1, false, 0.4f, 150, "Narrow sonic point (the excavator's Fine focus) for working close to finds."),
                Item("item.tool.brush", "Ultrasonic Brush", ItemCategory.Tool, 1, false, 0.1f, 20, "Removes the last layer of dust without damaging surfaces."),
                Item("item.tool.powered_drill", "Sonic Burster", ItemCategory.Tool, 1, false, 6f, 900, "Breaks compacted or frozen material. High risk near artifacts."),
                Item("item.consumable.sample_container", "Sample Container", ItemCategory.Consumable, 20, false, 0.1f, 5, "Sealed container for soil and material samples."),
                Item("item.test.unique_fragment", "Unidentified Fragment (test)", ItemCategory.Artifact, 1, true, 0.3f, 0, "Foundation test item: unique-instance handling. Real artifacts arrive in Phase 1C."),
            };

            AssignPlaceholderIcons(items, false);
            EnsurePanelSettings();

            var registry = LoadOrCreate<ItemRegistry>(RegistryPath);
            registry.SetEntries(items);
            EditorUtility.SetDirty(registry);

            var config = LoadOrCreate<XenoarchConfig>(ConfigPath);
            config.itemRegistry = registry;
            if (config.startingItems == null || config.startingItems.Count == 0)
            {
                config.startingItems = new List<StartingItem>
                {
                    new StartingItem { item = items[0], count = 1 },
                    new StartingItem { item = items[1], count = 1 },
                    new StartingItem { item = items[2], count = 1 },
                    new StartingItem { item = items[5], count = 5 },
                };
            }
            EditorUtility.SetDirty(config);

            Mat("Placeholder_Graphite", new Color(0.16f, 0.17f, 0.19f));
            Mat("Placeholder_Metal", new Color(0.42f, 0.44f, 0.47f));
            Mat("Placeholder_Station", new Color(0.26f, 0.30f, 0.36f));
            Mat("Placeholder_Amber", new Color(0.95f, 0.66f, 0.22f));
            Mat("Placeholder_PaleBlue", new Color(0.55f, 0.75f, 0.95f));
            Mat("Placeholder_Regolith", new Color(0.52f, 0.52f, 0.50f));
            Mat("Placeholder_MarsSoil", new Color(0.62f, 0.33f, 0.19f));

            AssetDatabase.SaveAssets();
            Debug.Log($"[Xenoarch] Setup: foundation assets ready ({items.Count} items, config at {ConfigPath})");
        }

        /// <summary>Item id -> placeholder icon copied from Genesis/ASH (see Art/Placeholder/SOURCES.md).</summary>
        public static readonly Dictionary<string, string> PlaceholderIcons = new Dictionary<string, string>
        {
            { "item.tool.handheld_scanner", PlaceholderArt + "/Genesis/Items/Icon_HandheldScanner.png" },
            { "item.tool.excavation_shovel", PlaceholderArt + "/Genesis/Items/Icon_ExcavationShovel.png" },
            { "item.tool.precision_tool", PlaceholderArt + "/Genesis/Items/Icon_PrecisionTool.png" },
            { "item.tool.brush", PlaceholderArt + "/Genesis/Items/Icon_Brush.png" },
            { "item.tool.powered_drill", PlaceholderArt + "/Genesis/Items/Icon_PoweredDrill.png" },
            { "item.consumable.sample_container", PlaceholderArt + "/Genesis/Items/Icon_SampleContainer.png" },
            { "item.test.unique_fragment", PlaceholderArt + "/ASH/Items/Icon_UnidentifiedFragment.png" },
        };
        public const string PlaceholderArt = Root + "/Art/Placeholder";

        public const string PanelSettingsPath = ResourcesFolder + "/UI/XenoarchPanelSettings.asset";
        public const string ThemePath = ResourcesFolder + "/UI/XenoarchTheme.tss";

        /// <summary>UI Toolkit PanelSettings used by every Xenoarch UIDocument (1920x1080 reference, scale with screen).</summary>
        public static UnityEngine.UIElements.PanelSettings EnsurePanelSettings()
        {
            EnsureFolder(ResourcesFolder + "/UI");
            var ps = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(PanelSettingsPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            }
            ps.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            var theme = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(ThemePath);
            if (theme != null) ps.themeStyleSheet = theme;
            else Debug.LogWarning($"[Xenoarch] Setup: theme {ThemePath} not found");
            EditorUtility.SetDirty(ps);
            return ps;
        }

        [MenuItem("Xenoarch/Setup/4. Assign Placeholder Item Icons (overwrite)", priority = 24)]
        public static void AssignPlaceholderIconsMenu()
        {
            var items = new List<ItemDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemsFolder }))
                items.Add(AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
            AssignPlaceholderIcons(items, true);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Sets ItemDefinition.icon from <see cref="PlaceholderIcons"/>. Keeps an existing icon unless overwrite.</summary>
        public static int AssignPlaceholderIcons(IEnumerable<ItemDefinition> items, bool overwrite)
        {
            int n = 0;
            foreach (var def in items)
            {
                if (def == null || !PlaceholderIcons.TryGetValue(def.Id, out var path)) continue;
                if (def.icon != null && !overwrite) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) { Debug.LogWarning($"[Xenoarch] Setup: icon sprite missing for {def.Id} at {path}"); continue; }
                def.icon = sprite;
                EditorUtility.SetDirty(def);
                n++;
            }
            if (n > 0) Debug.Log($"[Xenoarch] Setup: assigned {n} placeholder item icons");
            return n;
        }

        [MenuItem("Xenoarch/Setup/2. Build Placeholder Scenes (keep existing)", priority = 21)]
        public static void BuildScenesKeep() { BuildPlaceholderScenes(false); UpdateBuildSettings(); }

        [MenuItem("Xenoarch/Setup/2b. Rebuild Placeholder Scenes (overwrite)", priority = 22)]
        public static void BuildScenesOverwrite()
        {
            if (!EditorUtility.DisplayDialog("Rebuild placeholder scenes", "Overwrite all six Xenoarch placeholder scenes? Manual edits in them are lost.", "Overwrite", "Cancel")) return;
            BuildPlaceholderScenes(true);
            UpdateBuildSettings();
        }

        [MenuItem("Xenoarch/Setup/3. Update Build Settings", priority = 23)]
        public static void UpdateBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var n in SceneNames.All)
            {
                var p = ScenePath(n);
                if (File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true));
                else Debug.LogWarning($"[Xenoarch] Setup: missing scene {p}");
            }
            foreach (var n in SceneNames.Extra) { var p = ScenePath(n); if (File.Exists(p)) list.Add(new EditorBuildSettingsScene(p, true)); }
            string gen = ScenesFolder + "/Generated";
            if (Directory.Exists(gen))
                foreach (var file in Directory.GetFiles(gen, "Surface_*.unity"))
                    if (!list.Any(s => s.path == file.Replace("\\", "/"))) list.Add(new EditorBuildSettingsScene(file.Replace("\\", "/"), true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[Xenoarch] Setup: Build Settings = {string.Join(", ", SceneNames.All)} ({list.Count} scenes, Bootstrap first)");
        }

        public static void BuildPlaceholderScenes(bool overwrite)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Xenoarch] Setup: exit Play Mode first"); return; }
            EnsureFolder(ScenesFolder);
            Build(SceneNames.Bootstrap, overwrite, BuildBootstrap);
            Build(SceneNames.MainMenu, overwrite, BuildMainMenu);
            Build(SceneNames.ShipStarter, overwrite, BuildShip);
            Build(SceneNames.TradeHubSol, overwrite, BuildTradeHub);
            Build(SceneNames.MoonExcavation01, overwrite, () => BuildSurface("Moon", "Placeholder_Regolith", new Color(1f, 0.98f, 0.95f), 1.6f, new Color(0.02f, 0.02f, 0.03f)));
            Build(SceneNames.MarsExcavation01, overwrite, () => BuildSurface("Mars", "Placeholder_MarsSoil", new Color(1f, 0.85f, 0.7f), 1.1f, new Color(0.55f, 0.38f, 0.28f)));
        }

        // ------------------------------------------------------------------ scenes

        static string _buildingScene;

        static void Build(string name, bool overwrite, Action populate)
        {
            string path = ScenePath(name);
            if (File.Exists(path) && !overwrite) { Debug.Log($"[Xenoarch] Setup: {path} exists, kept"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == path) { Debug.LogWarning($"[Xenoarch] Setup: {path} is open, skipped"); return; }

            _buildingScene = name;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                populate();
                EditorSceneManager.SaveScene(scene, path);
                Debug.Log($"[Xenoarch] Setup: created {path}");
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void BuildBootstrap()
        {
            new GameObject("Bootstrap").AddComponent<BootstrapEntry>();
            Cam("Camera", new Vector3(0, 1, -10), Quaternion.identity, Color.black);
        }

        static void BuildMainMenu()
        {
            new GameObject("MainMenu").AddComponent<MainMenuPlaceholder>();
            Cam("MenuCamera", new Vector3(0, 1, -10), Quaternion.identity, new Color(0.08f, 0.09f, 0.11f));
            Sun(new Color(0.8f, 0.85f, 1f), 0.8f);
        }

        static void BuildShip()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.3f);
            var env = new GameObject("Environment").transform;
            Box("Floor", new Vector3(0, -0.1f, 0), new Vector3(10, 0.2f, 16), "Placeholder_Graphite", env);
            Box("Ceiling", new Vector3(0, 3.1f, 0), new Vector3(10, 0.2f, 16), "Placeholder_Metal", env);
            Box("Wall_L", new Vector3(-5.1f, 1.5f, 0), new Vector3(0.2f, 3f, 16), "Placeholder_Metal", env);
            Box("Wall_R", new Vector3(5.1f, 1.5f, 0), new Vector3(0.2f, 3f, 16), "Placeholder_Metal", env);
            Box("Wall_Fwd", new Vector3(0, 1.5f, 8.1f), new Vector3(10, 3f, 0.2f), "Placeholder_Metal", env);
            Box("Wall_Aft", new Vector3(0, 1.5f, -8.1f), new Vector3(10, 3f, 0.2f), "Placeholder_Metal", env);
            Box("Cockpit_Console", new Vector3(0, 0.5f, 7.3f), new Vector3(4, 1f, 1f), "Placeholder_Station", env);
            Box("ArtifactStorage_Racks", new Vector3(-4.4f, 1f, -4f), new Vector3(1f, 2f, 4f), "Placeholder_Station", env);
            Box("EquipmentStorage_Lockers", new Vector3(4.4f, 1f, -4f), new Vector3(1f, 2f, 4f), "Placeholder_Station", env);
            PointLight("CabinLight_Fwd", new Vector3(0, 2.8f, 4), 8f);
            PointLight("CabinLight_Aft", new Vector3(0, 2.8f, -4), 8f);

            Spawn(PlayerSpawnIds.Default, Vector3.zero, 0f);
            Rig(Vector3.zero, 0f);

            // Interaction ring around the spawn (placeholder rig only turns): 45 degree steps, 1.8 m out.
            var ring = new GameObject("InteractionTestRing (Phase 1A placeholder)").transform;
            Pickup(Ring(0), ring, "Excavation Brush pickup", "item.tool.brush", "wo.ship_starter.pickup_brush");
            Station(Ring(45), ring, "Navigation Terminal", "Phase 1B");
            Station(Ring(90), ring, "Orbital Scanning Terminal", "Phase 1B");
            Station(Ring(135), ring, "Research Laboratory", "Phase 1C");
            Travel(Ring(180), ring, "Airlock [DEBUG: Moon surface]", SceneNames.MoonExcavation01);
            Travel(Ring(225), ring, "[DEBUG] Dock at TradeHub Sol", SceneNames.TradeHubSol);
            Travel(Ring(270), ring, "[DEBUG] Travel to Mars surface", SceneNames.MarsExcavation01);
            Station(Ring(315), ring, "Engineering / Upgrade Terminal", "Phase 1D");
        }

        static void BuildTradeHub()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.33f);
            var env = new GameObject("Environment").transform;
            Box("Concourse_Floor", new Vector3(0, -0.1f, 0), new Vector3(30, 0.2f, 30), "Placeholder_Graphite", env);
            Box("Vendor_Kiosk_Block", new Vector3(-8, 1.5f, 8), new Vector3(6, 3, 3), "Placeholder_Station", env);
            Box("Docking_Ring", new Vector3(0, 2f, -12), new Vector3(8, 4, 1), "Placeholder_Metal", env);
            PointLight("HubLight", new Vector3(0, 6, 0), 25f);
            Sun(new Color(0.9f, 0.95f, 1f), 0.6f);
            Spawn(PlayerSpawnIds.Default, Vector3.zero, 0f);
            Rig(Vector3.zero, 0f);
            var ring = new GameObject("InteractionTestRing (Phase 1A placeholder)").transform;
            Travel(Ring(0), ring, "Return to ship", SceneNames.ShipStarter);
            Station(Ring(90), ring, "Artifact Buyer (vendor)", "Phase 1D");
            Station(Ring(180), ring, "Equipment Supplier (vendor)", "Phase 1D");
            Station(Ring(270), ring, "Refuelling Service", "Phase 1D");
        }

        static void BuildSurface(string body, string groundMat, Color sunColor, float sunIntensity, Color ambient)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            var env = new GameObject("Environment").transform;
            Box($"{body}_Ground", new Vector3(0, -0.5f, 0), new Vector3(200, 1f, 200), groundMat, env);
            Box("Lander_Placeholder", new Vector3(-6, 1.5f, -6), new Vector3(4, 3, 4), "Placeholder_Metal", env);
            Box("Excavation_Area_Marker (Phase 1C diggable volume goes here)", new Vector3(0, 0.02f, 10), new Vector3(6, 0.04f, 6), "Placeholder_Amber", env);
            Sun(sunColor, sunIntensity);
            Spawn(PlayerSpawnIds.Default, Vector3.zero, 0f);
            Rig(Vector3.zero, 0f);
            var ring = new GameObject("InteractionTestRing (Phase 1A placeholder)").transform;
            Travel(Ring(180), ring, "Board lander: return to ship", SceneNames.ShipStarter);
            Station(Ring(0), ring, $"{body} Survey Beacon", "Phase 1B/1C");
        }

        // ------------------------------------------------------------------ helpers

        static (Vector3 pos, float yaw) Ring(float yaw)
        {
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            return (dir * 1.8f, yaw);
        }

        static GameObject Rig(Vector3 pos, float yaw)
        {
            return XenoarchPlayerBuilder.InstantiatePlayer(pos, yaw, XenoarchPlayerBuilder.GravityFor(_buildingScene));
        }

        static void Spawn(string id, Vector3 pos, float yaw)
        {
            var go = new GameObject($"Spawn_{id}");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.AddComponent<PlayerSpawnPoint>().spawnId = id;
        }

        static GameObject Pillar((Vector3 pos, float yaw) at, Transform parent, string name, string mat)
        {
            var go = Box(name, at.pos + Vector3.up * 1f, new Vector3(0.6f, 2f, 0.3f), mat, parent);
            go.transform.rotation = Quaternion.Euler(0, at.yaw, 0);
            Label(parent, name, at.pos + Vector3.up * 2.3f, at.yaw);
            return go;
        }

        static void Station((Vector3 pos, float yaw) at, Transform parent, string name, string phase)
        {
            var s = Pillar(at, parent, name, "Placeholder_Station").AddComponent<StationPlaceholderInteractable>();
            s.stationName = name; s.plannedPhase = phase;
        }

        static void Travel((Vector3 pos, float yaw) at, Transform parent, string name, string scene)
        {
            var t = Pillar(at, parent, name, "Placeholder_Amber").AddComponent<SceneTravelInteractable>();
            t.targetScene = scene; t.targetSpawnId = PlayerSpawnIds.Default; t.prompt = name;
        }

        static void Pickup((Vector3 pos, float yaw) at, Transform parent, string name, string itemId, string worldId)
        {
            Box(name + " pedestal", at.pos + Vector3.up * 0.7f, new Vector3(0.4f, 1.4f, 0.4f), "Placeholder_Station", parent);
            var go = Box(name, at.pos + Vector3.up * 1.62f, new Vector3(0.35f, 0.35f, 0.35f), "Placeholder_PaleBlue", parent);
            go.AddComponent<WorldObjectId>().SetIdForTooling(worldId);
            var p = go.AddComponent<ItemPickupInteractable>();
            p.item = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryPath)?.Get(itemId);
            p.count = 1;
            if (p.item == null) Debug.LogWarning($"[Xenoarch] Setup: pickup item '{itemId}' not found (run Create Foundation Assets first)");
            Label(parent, name, at.pos + Vector3.up * 2.3f, at.yaw);
        }

        static void Label(Transform parent, string text, Vector3 worldPos, float yaw)
        {
            // Floating world labels were removed in the 1B polish pass (Anthony: only the HUD "E" popup). Kept as a no-op
            // so re-running scene setup never brings them back.
            return;
#pragma warning disable CS0162
            var go = new GameObject("Label", typeof(TextMesh));
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one;
            var tm = go.GetComponent<TextMesh>();
            tm.text = text; tm.fontSize = 48; tm.characterSize = 0.025f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.85f, 0.92f, 1f);
#pragma warning restore CS0162
        }

        static GameObject Box(string name, Vector3 pos, Vector3 scale, string mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = LoadMat(mat);
            return go;
        }

        static void Cam(string name, Vector3 pos, Quaternion rot, Color bg)
        {
            var go = new GameObject(name, typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(pos, rot);
            var c = go.GetComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = bg;
        }

        static void Sun(Color color, float intensity)
        {
            var go = new GameObject("Sun", typeof(Light));
            go.transform.rotation = Quaternion.Euler(50, -30, 0);
            var l = go.GetComponent<Light>();
            l.type = LightType.Directional; l.color = color; l.intensity = intensity; l.shadows = LightShadows.Soft;
        }

        static void PointLight(string name, Vector3 pos, float range)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.position = pos;
            var l = go.GetComponent<Light>();
            l.type = LightType.Point; l.range = range; l.intensity = 2f; l.color = new Color(0.85f, 0.9f, 1f);
        }

        // ------------------------------------------------------------------ assets

        static ItemDefinition Item(string id, string display, ItemCategory cat, int maxStack, bool unique, float mass, int value, string desc)
        {
            string file = $"{ItemsFolder}/{id.Replace("item.", "Item_").Replace('.', '_')}.asset";
            var def = LoadOrCreate<ItemDefinition>(file);
            def.SetIdForTooling(id);
            def.displayName = display; def.category = cat; def.maxStack = maxStack; def.uniqueInstance = unique;
            def.mass = mass; def.baseValue = value; def.description = desc;
            EditorUtility.SetDirty(def);
            return def;
        }

        static void Mat(string name, Color color)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) { Debug.LogError("[Xenoarch] Setup: URP Lit shader not found"); return; }
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            m.color = color;
            EditorUtility.SetDirty(m);
        }

        static Material LoadMat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
