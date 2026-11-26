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
        const string LogFile = "Logs/xeno_phase1e.txt";
        const string NavCorePath = "Assets/_Project/Data/Artifacts/Artifact_mars_navigation_core.asset";
        const string TerrainLitPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";

        static XenoarchPhase1ESetup() => EditorApplication.update += Poll;

        static double _next;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(Trigger) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            File.Delete(Trigger);
            Run();
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
