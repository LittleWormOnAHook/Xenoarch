using DMI.Xenoarch.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Adapted from ASH "Play From Main Menu" (ASH/Editor/Systems/ASHPlayFromMainMenu.cs).
    /// Editor Play normally starts whatever scene is open. Menu Xenoarch/Play Mode:
    ///  - "Full Flow (from Bootstrap)" (default since t175u): Play always starts Bootstrap -> MainMenu (New Game / Continue),
    ///    like a build, whatever scene is open.
    ///  - "Quick Play (open scene)": dev bypass, Play starts the open scene directly, e.g. Ship_Starter, for fast tests.
    /// Uses EditorSceneManager.playModeStartScene; saved per machine in EditorPrefs. Changes no scene or build list.
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchPlayFromBootstrap
    {
        const string Pref = "DMI.Xenoarch.PlayFromBootstrap";
        const string MenuFull = "Xenoarch/Play Mode/Full Flow (from Bootstrap)";
        const string MenuQuick = "Xenoarch/Play Mode/Quick Play (open scene)";

        static XenoarchPlayFromBootstrap() => EditorApplication.delayCall += Apply;

        public static bool FromBootstrap => EditorPrefs.GetBool(Pref, true);

        [MenuItem(MenuFull, false, 1)] static void SetFull() { EditorPrefs.SetBool(Pref, true); Apply(); }
        [MenuItem(MenuFull, true)] static bool ValidateFull() { Menu.SetChecked(MenuFull, FromBootstrap); return true; }
        [MenuItem(MenuQuick, false, 2)] static void SetQuick() { EditorPrefs.SetBool(Pref, false); Apply(); }
        [MenuItem(MenuQuick, true)] static bool ValidateQuick() { Menu.SetChecked(MenuQuick, !FromBootstrap); return true; }

        static void Apply()
        {
            if (!FromBootstrap) { EditorSceneManager.playModeStartScene = null; return; }
            string path = XenoarchSetup.ScenePath(SceneNames.Bootstrap);
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            EditorSceneManager.playModeStartScene = scene;
            if (!scene) Debug.LogWarning($"[Xenoarch] Play Mode: Full Flow is on, but {path} was not found.");
        }
    }
}
