using System.IO;
using DMI.Xenoarch.Weather;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Creates the weather assets once (and again after edits that add bodies): a WeatherProfile per landable body
    /// (Moon, Mars, the 22 generated planets) plus one default per climate, the particle sprites and materials, and the
    /// WeatherCatalog in Resources. Never overwrites an existing asset (your Inspector edits stay); "Reset Profiles"
    /// rewrites the profiles from the defaults on request. Skips while the Editor is in Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchWeatherSetup
    {
        const string DataDir = "Assets/_Project/Data/Weather";
        const string ArtDir = "Assets/_Project/Art/Weather";
        const string AudioDir = "Assets/_Project/Audio/Weather";
        const string CatalogPath = "Assets/_Project/Resources/Weather/WeatherCatalog.asset";

        static double _startAt;
        static XenoarchWeatherSetup() { _startAt = EditorApplication.timeSinceStartup + 3.0; EditorApplication.update += WaitAndRun; }

        // Retries until the Editor is idle (not compiling / importing / playing), then runs once per domain load.
        static void WaitAndRun()
        {
            if (EditorApplication.timeSinceStartup < _startAt) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= WaitAndRun;
            Ensure(false);
        }

        [MenuItem("Xenoarch/Weather/Create Weather Assets")]
        public static void CreateMenu() { Ensure(true); }

        [MenuItem("Xenoarch/Weather/Reset Profiles To Defaults (overwrites)")]
        public static void ResetMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorUtility.DisplayDialog("Reset weather profiles", "Rewrite every WeatherProfile asset from the built-in defaults? Your edits to those assets are lost.", "Reset", "Cancel")) return;
            Ensure(true, overwrite: true);
        }

        [MenuItem("Xenoarch/Weather/Next Weather (Play mode)")]
        public static void NextMenu() { var d = WeatherDirector.Instance; if (d != null) { d.NextWeather(); Debug.Log("[Xenoarch] Weather: " + d.Describe()); } }

        [MenuItem("Xenoarch/Weather/Toggle Extreme Preset")]
        public static void ExtremeMenu() { WeatherSettings.Extreme = !WeatherSettings.Extreme; Debug.Log("[Xenoarch] Weather extreme preset: " + WeatherSettings.Extreme); }

        [MenuItem("Xenoarch/Weather/Print Current Weather")]
        public static void PrintMenu() { var d = WeatherDirector.Instance; Debug.Log(d != null ? "[Xenoarch] " + d.Describe() : "[Xenoarch] Weather director not running (enter Play mode)."); }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void Ensure(bool verbose, bool overwrite = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            Folder(DataDir); Folder(ArtDir); Folder(AudioDir); Folder("Assets/_Project/Resources/Weather");

            var cat = AssetDatabase.LoadAssetAtPath<WeatherCatalog>(CatalogPath);
            bool dirty = false;
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<WeatherCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
                dirty = true;
            }

            // sprites + materials
            var dot = SavePng(WeatherVisuals.SoftDot(), ArtDir + "/Weather_SoftDot.png");
            var streak = SavePng(WeatherVisuals.Streak(), ArtDir + "/Weather_Streak.png");
            if (cat.alphaMaterial == null) { cat.alphaMaterial = SaveMat("Weather_Alpha", dot, false); dirty = true; }
            if (cat.additiveMaterial == null) { cat.additiveMaterial = SaveMat("Weather_Additive", dot, true); dirty = true; }
            if (cat.streakMaterial == null) { cat.streakMaterial = SaveMat("Weather_Streak", streak, false); dirty = true; }

            // thunder (CC0, copied from ASH)
            if (cat.thunder == null || cat.thunder.Length == 0)
            {
                var a = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/ASH_Thunder_01.ogg");
                var b = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/ASH_Thunder_02.ogg");
                if (a != null && b != null) { cat.thunder = new[] { a, b }; dirty = true; }
            }

            int created = 0;
            foreach (var row in WeatherDefaults.Table)
            {
                string path = DataDir + "/Weather_" + row.bodyId.Replace("body.", "").Replace('.', '_') + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<WeatherProfile>(path);
                if (existing != null && !overwrite) { Register(cat, row.bodyId, existing, ref dirty); continue; }
                var built = WeatherDefaults.Build(row);
                if (existing != null) { EditorUtility.CopySerialized(built, existing); Object.DestroyImmediate(built); EditorUtility.SetDirty(existing); Register(cat, row.bodyId, existing, ref dirty); }
                else { AssetDatabase.CreateAsset(built, path); Register(cat, row.bodyId, built, ref dirty); created++; }
            }
            foreach (WeatherArchetype t in System.Enum.GetValues(typeof(WeatherArchetype)))
            {
                if (t == WeatherArchetype.None) continue;
                string path = DataDir + "/Weather_Default_" + t + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<WeatherProfile>(path);
                if (existing == null || overwrite)
                {
                    var built = WeatherDefaults.BuildGeneric("default." + t, "Default " + t, t, 9.81f);
                    built.notes = "Used for a body that has no profile of its own.";
                    if (existing != null) { EditorUtility.CopySerialized(built, existing); Object.DestroyImmediate(built); EditorUtility.SetDirty(existing); }
                    else { AssetDatabase.CreateAsset(built, path); existing = built; created++; }
                }
                if (!cat.archetypeDefaults.Contains(existing)) { cat.archetypeDefaults.Add(existing); dirty = true; }
            }

            if (dirty || created > 0 || overwrite)
            {
                EditorUtility.SetDirty(cat);
                AssetDatabase.SaveAssets();
                WeatherCatalog.Invalidate();
                Debug.Log($"[Xenoarch] Weather assets ready ({created} new profile(s), {cat.bodies.Count} bodies in the catalog).");
            }
            else if (verbose) Debug.Log("[Xenoarch] Weather assets already up to date.");
        }

        static void Register(WeatherCatalog cat, string bodyId, WeatherProfile p, ref bool dirty)
        {
            foreach (var e in cat.bodies)
                if (e != null && e.bodyId == bodyId) { if (e.profile != p) { e.profile = p; dirty = true; } return; }
            cat.bodies.Add(new WeatherCatalog.Entry { bodyId = bodyId, profile = p });
            dirty = true;
        }

        static Texture2D SavePng(Texture2D src, string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, src.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null)
                {
                    imp.textureType = TextureImporterType.Default; imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
                    imp.wrapMode = TextureWrapMode.Clamp; imp.sRGBTexture = true;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material SaveMat(string name, Texture2D tex, bool additive)
        {
            string path = ArtDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = WeatherVisuals.MakeMaterial(name, tex, additive);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}

