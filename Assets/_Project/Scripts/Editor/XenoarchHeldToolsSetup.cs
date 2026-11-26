using System.IO;
using System.Text;
using DMI.Xenoarch.Player;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Builds the six held-tool prefabs, URP materials and the Resources catalog (ZEN-23).
    /// Menu: Tools/Xenoarch/Player/Build Held Tool Prefabs. Trigger: Temp/xenoarch_heldtools.trigger
    /// -> Logs/xenoarch_heldtools.txt. Never enters Play mode and never saves a scene.
    /// </summary>
    public static class XenoarchHeldToolsSetup
    {
        public const string Trigger = "Temp/xenoarch_heldtools.trigger";
        public const string LogPath = "Logs/xenoarch_heldtools.txt";
        public const string Root = "Assets/Xenoarch/Tools/Held";
        public const string MatFolder = Root + "/Materials";
        public const string PrefabFolder = Root + "/Prefabs";
        public const string MeshFolder = Root + "/Meshes";
        public const string CatalogPath = "Assets/_Project/Resources/Tools/HeldToolCatalog.asset";

        [MenuItem("Tools/Xenoarch/Player/Build Held Tool Prefabs")]
        public static void Menu() => RunFromTrigger();

        public static void RunFromTrigger()
        {
            var log = new StringBuilder();
            try { Run(log); }
            catch (System.Exception e) { log.AppendLine("ERROR " + e); }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, log.ToString());
            Debug.Log("[Xenoarch] Held tools: " + log.ToString().TrimEnd());
        }

        static void Run(StringBuilder log)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { log.AppendLine("URP Lit shader not found - skipped"); return; }

            Directory.CreateDirectory(MatFolder);
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));

            var mats = new HeldToolMeshFactory.Materials
            {
                metal = Mat(shader, "Held_Metal", new Color(0.55f, 0.57f, 0.60f), 0.85f, 0.55f, null),
                metalDark = Mat(shader, "Held_MetalDark", new Color(0.12f, 0.13f, 0.14f), 0.85f, 0.40f, null),
                rubber = Mat(shader, "Held_Rubber", new Color(0.16f, 0.16f, 0.17f), 0f, 0.22f, null),
                emit = Mat(shader, "Held_EmitCyan", new Color(0.15f, 0.55f, 0.65f), 0.2f, 0.5f, new Color(0.2f, 0.95f, 1f) * 2.4f),
                screen = Mat(shader, "Held_Screen", new Color(0.05f, 0.25f, 0.32f), 0.15f, 0.7f, new Color(0.2f, 0.95f, 1f) * 1.8f),
                bristle = Mat(shader, "Held_Bristle", new Color(0.62f, 0.48f, 0.28f), 0f, 0.2f, null),
                plastic = Mat(shader, "Held_Plastic", new Color(0.38f, 0.40f, 0.43f), 0.15f, 0.35f, null)
            };

            var catalog = AssetDatabase.LoadAssetAtPath<HeldToolCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<HeldToolCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            foreach (var id in HeldToolMeshFactory.AllIds)
            {
                var go = HeldToolMeshFactory.Build(id, mats);
                if (go == null) { log.AppendLine("skip " + id); continue; }
                // Persist each unique mesh so the prefab does not hold un-saved instances.
                int meshIndex = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    string meshPath = $"{MeshFolder}/{go.name}_{mf.gameObject.name}_{meshIndex++}.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
                    var copy = Object.Instantiate(mf.sharedMesh);
                    copy.name = Path.GetFileNameWithoutExtension(meshPath);
                    AssetDatabase.CreateAsset(copy, meshPath);
                    mf.sharedMesh = copy;
                }
                string prefabPath = $"{PrefabFolder}/{go.name}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);
                if (prefab == null) { log.AppendLine("FAILED prefab " + prefabPath); continue; }
                Assign(catalog, id, prefab);
                log.AppendLine("prefab " + prefabPath);
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            log.AppendLine("catalog " + CatalogPath);
            log.AppendLine("done");
        }

        static void Assign(HeldToolCatalog catalog, string id, GameObject prefab)
        {
            switch (id)
            {
                case HeldToolMeshFactory.ProbeId: catalog.probeWand = prefab; break;
                case HeldToolMeshFactory.ScannerId: catalog.scannerPistol = prefab; break;
                case HeldToolMeshFactory.ShovelId: catalog.entrenchingShovel = prefab; break;
                case HeldToolMeshFactory.PrecisionId: catalog.precisionDual = prefab; break;
                case HeldToolMeshFactory.BrushId: catalog.brushStubby = prefab; break;
                case HeldToolMeshFactory.DrillId: catalog.rockDrill = prefab; break;
            }
        }

        static Material Mat(Shader shader, string name, Color color, float metallic, float smooth, Color? emit)
        {
            string path = $"{MatFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color); else mat.color = color;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (emit.HasValue && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emit.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
