using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Main menu (MainMenu scene, Resources/UI/MainMenu.uxml for the backdrop), following ASH's ASHMainMenu flow:
    /// Continue (newest save, shown only when one exists, with its place and time) above New Game, then New Test All,
    /// Load Game, Settings, Quit. Screens are built in code (<see cref="MenuScreens"/>) so the scene needs no changes.
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
            // t246u Morpheus layout: logo + button column on the left, Continue card (newest save) on the right.
            var root = MenuScreens.MenuLayout(true, "Xenoarch", "Discover the past. Unlock the future.", out var col, out var side);
            string newest = SaveSlots.Newest();
            var btns = new VisualElement(); btns.style.width = 440; col.Add(btns);
            if (newest != null)
            {
                MenuScreens.ColumnButton(btns, "Continue", "arrowRight", () => { if (!Busy) XenoarchRuntime.Instance.LoadGame(newest); }).AddToClassList("xa-m-sel");
                side.Add(MenuScreens.SaveCard(newest, newest == SaveSlots.Auto ? "Autosave" : newest == SaveSlots.Quick ? "Quick save" : newest, "Continue"));
            }
            MenuScreens.ColumnButton(btns, "New Game", "ui-level", () =>
            {
                if (Busy) return;
                if (newest == null) XenoarchRuntime.Instance.NewGame();
                else MenuScreens.Confirm(root, "Start a new game? This deletes ALL saves (autosave, quick save and slots 1-6).", () => XenoarchRuntime.Instance.NewGame());
            });
            MenuScreens.ColumnButton(btns, "New Test All", "ui-upgrade", () =>
            {
                if (Busy) return;
                if (newest == null) XenoarchRuntime.Instance.NewTestAll();
                else MenuScreens.Confirm(root, "Start a New Test All game? This deletes ALL saves (autosave, quick save and slots 1-6).", () => XenoarchRuntime.Instance.NewTestAll());
            }, true, "Test");
            MenuScreens.ColumnButton(btns, "Load Game", "import", () => Swap(MenuScreens.Slots(false, ShowMain, null)), newest != null);
            MenuScreens.ColumnButton(btns, "Settings", "ui-settings", () => Swap(MenuScreens.Settings(ShowMain)));
            MenuScreens.ColumnButton(btns, "Quit", "exit", MenuScreens.Quit);
            var note = MUI.L(btns, "New Test All starts with everything unlocked, all tools and full resource stacks.", "xa-m-menu__note");
            MUI.L(root, "XENOARCH  ·  DARK MATTER INTERACTIVE", "xa-m-menu__version");
            var lg = MUI.Legend(root, ("Click", "Select"), ("Esc", "Back")); lg.style.position = Position.Absolute; lg.style.right = 48; lg.style.bottom = 30;
            Swap(root);
        }
    }
}
