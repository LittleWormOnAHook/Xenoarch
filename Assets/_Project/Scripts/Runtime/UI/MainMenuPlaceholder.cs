using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>PLACEHOLDER main menu (IMGUI): New Game, Continue (quick save), Quit. Real UI comes with the UI pass.</summary>
    public sealed class MainMenuPlaceholder : MonoBehaviour
    {
        void OnEnable() => UIState.SetOpen(Panels.MainMenu, true);
        void OnDisable() => UIState.SetOpen(Panels.MainMenu, false);

        void OnGUI()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || XenoarchRuntime.Scenes.IsLoading) return;
            float w = 260f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.4f;
            GUI.Label(new Rect(x, y - 70, w, 30), "XENOARCH");
            GUI.Label(new Rect(x, y - 45, w + 200, 30), "Discover the past. Unlock the future.");
            if (GUI.Button(new Rect(x, y, w, 36), "New Game")) rt.NewGame();
            var slot = XenoarchRuntime.Session.Config.quickSaveSlot;
            GUI.enabled = XenoarchRuntime.Session.Saves.HasSave(slot);
            if (GUI.Button(new Rect(x, y + 44, w, 36), "Continue (quick save)")) rt.LoadGame(slot);
            GUI.enabled = true;
            if (GUI.Button(new Rect(x, y + 88, w, 36), "Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
