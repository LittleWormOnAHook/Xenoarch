using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Main menu (MainMenu scene, Resources/UI/MainMenu.uxml for the backdrop), following ASH's ASHMainMenu flow:
    /// Continue (newest save, shown only when one exists, with its place and time) above New Game, then Load Game,
    /// Settings, Quit. Screens are built in code (<see cref="MenuScreens"/>) so the scene needs no changes.
    /// (Class name kept: the MainMenu scene references it.)
    /// </summary>
    public sealed class MainMenuPlaceholder : MonoBehaviour
    {
        public const string Uxml = "UI/MainMenu";
        VisualElement _host;

        void OnEnable()
        {
            UIState.SetOpen(Panels.MainMenu, true);
            Time.timeScale = 1f;
            GameSettings.Apply();
            var doc = XenoarchUI.CreateDocument(gameObject, Uxml, 20);
            if (doc == null) return;
            var r = doc.rootVisualElement;
            _host = r.Q<VisualElement>("menu-root") ?? r;
            ShowMain();
        }

        void OnDisable() => UIState.SetOpen(Panels.MainMenu, false);

        static bool Busy => XenoarchRuntime.Instance == null || XenoarchRuntime.Scenes.IsLoading;

        void Swap(VisualElement screen) { _host.Clear(); _host.Add(screen); }

        void ShowMain()
        {
            var p = MenuScreens.Panel("XENOARCH", "Discover the past. Unlock the future.");
            string newest = SaveSlots.Newest();
            if (newest != null)
            {
                var info = SaveSlots.Read(newest);
                MenuScreens.Button(p, "Continue", () => { if (!Busy) XenoarchRuntime.Instance.LoadGame(newest); });
                MenuScreens.Note(p, $"{info.place}  -  {(newest == SaveSlots.Auto ? "autosave" : newest)}, {SaveSlots.When(info.ticks)}, played {SaveSlots.PlayTime(info.played)}");
            }
            MenuScreens.Button(p, "New Game", () =>
            {
                if (Busy) return;
                if (newest == null) XenoarchRuntime.Instance.NewGame();
                else MenuScreens.Confirm(p, "Start a new game? Your saves are kept (the autosave will be replaced as you play).", () => XenoarchRuntime.Instance.NewGame());
            });
            MenuScreens.Button(p, "Load Game", () => Swap(MenuScreens.Slots(false, ShowMain, null)), newest != null);
            MenuScreens.Button(p, "Settings", () => Swap(MenuScreens.Settings(ShowMain)));
            MenuScreens.Button(p, "Quit", MenuScreens.Quit);
            Swap(p);
        }
    }
}
