using DMI.Xenoarch.Excavation;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Writes the built-in dig tool tiers (DigTools.Defaults) as DigToolDefinition assets the first time the folder is
    /// empty, so the tiers are tunable data. Never overwrites existing assets; skipped while playing.
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchDigToolAssets
    {
        const string Folder = "Assets/_Project/Resources/Excavation/DigTools";

        static XenoarchDigToolAssets() => EditorApplication.delayCall += Ensure;

        [MenuItem("Xenoarch/Excavation/Create Default Dig Tool Assets")]
        static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (AssetDatabase.IsValidFolder(Folder) && AssetDatabase.FindAssets("t:DigToolDefinition", new[] { Folder }).Length > 0) return;
            EnsureFolder("Assets/_Project/Resources", "Excavation");
            EnsureFolder("Assets/_Project/Resources/Excavation", "DigTools");
            var defs = DigTools.Defaults;
            for (int i = 0; i < defs.Length; i++)
            {
                var a = ScriptableObject.CreateInstance<DigToolDefinition>();
                a.spec = defs[i];
                a.slotOrder = i;
                string key = string.IsNullOrEmpty(defs[i].shortName) ? $"tool{i}" : defs[i].shortName;
                a.SetIdForTooling("digtool." + key);
                AssetDatabase.CreateAsset(a, $"{Folder}/DigTool_{key}.asset");
            }
            AssetDatabase.SaveAssets();
            DigTools.Reload();
            Debug.Log($"[Xenoarch] Created {defs.Length} dig tool assets in {Folder}");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
