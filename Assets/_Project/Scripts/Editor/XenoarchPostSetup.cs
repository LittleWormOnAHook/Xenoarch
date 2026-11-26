using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Creates the editable base post profile (Data/Rendering/VP_Xenoarch_Base.asset) once if it does not exist and
    /// assigns it to XenoarchConfig.basePostProfile when that is empty. Never overwrites an existing profile or a
    /// profile already assigned. Re-run: Tools/Xenoarch/Rendering/Create Base Post Profile.
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchPostSetup
    {
        const string Path = "Assets/_Project/Data/Rendering/VP_Xenoarch_Base.asset";
        static int _tries;
        static XenoarchPostSetup() { _tries = 0; EditorApplication.update += Tick; }

        // Retry on editor ticks until the editor is idle (right after a domain reload it can still be compiling/importing).
        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.update -= Tick; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { if (++_tries > 2000) EditorApplication.update -= Tick; return; }
            EditorApplication.update -= Tick;
            Ensure(false);
        }

        [MenuItem("Tools/Xenoarch/Rendering/Create Base Post Profile")]
        static void Menu() => Ensure(true);

        static void Ensure(bool verbose)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Path);
            if (profile == null)
            {
                profile = XenoarchPostStack.BuildDefault();
                AssetDatabase.CreateAsset(profile, Path);
                foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Xenoarch] Base post profile created: {Path}");
            }
            else if (verbose) Debug.Log($"[Xenoarch] Base post profile already exists: {Path} (kept)");
            foreach (var guid in AssetDatabase.FindAssets("t:XenoarchConfig"))
            {
                var cfg = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (cfg == null || cfg.basePostProfile != null) continue;
                cfg.basePostProfile = profile; EditorUtility.SetDirty(cfg); AssetDatabase.SaveAssets();
                Debug.Log($"[Xenoarch] {cfg.name}: basePostProfile = VP_Xenoarch_Base");
            }
        }
    }
}
