using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// In-game pause menu (Esc / gamepad Start), adapted from ASH's pause flow: Resume, Save, Load, Settings, Main menu,
    /// Quit. Pauses time while open. Only opens in gameplay (session active, not loading, no other panel open last frame,
    /// so Esc that closes a terminal does not also open this).
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        UIDocument _doc;
        VisualElement _veil;
        bool _blockedLastFrame;
        public bool IsOpen => _veil != null;

        void Update()
        {
            var kb = Keyboard.current; var gp = Gamepad.current;
            bool pressed = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);
            if (pressed)
            {
                if (IsOpen) Close();
                else if (CanOpen()) Open();
            }
            _blockedLastFrame = UIState.BlocksGameplay;
        }

        bool CanOpen()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null || !rt.SessionActive || XenoarchRuntime.Scenes == null || XenoarchRuntime.Scenes.IsLoading) return false;
            if (_blockedLastFrame || UIState.BlocksGameplay) return false;
            string s = SceneManager.GetActiveScene().name;
            return s != SceneNames.MainMenu && s != SceneNames.Bootstrap;
        }

        public void Open()
        {
            if (_doc == null)
            {
                var host = new GameObject("PauseMenu");
                host.transform.SetParent(transform, false);
                _doc = host.AddComponent<UIDocument>();
                _doc.panelSettings = XenoarchUI.LoadPanelSettings();
                _doc.sortingOrder = 60;
            }
            var root = _doc.rootVisualElement;
            XenoarchUI.FillRoot(root);
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
            _veil = new VisualElement();
            XenoarchUI.FillRoot(_veil);
            _veil.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDeep, 0.75f);
            _veil.style.alignItems = Align.Center; _veil.style.justifyContent = Justify.Center;
            root.Add(_veil);
            ShowMain();
            UIState.SetOpen(Panels.Pause, true);
            Time.timeScale = 0f;
        }

        public void Close()
        {
            _veil?.RemoveFromHierarchy();
            _veil = null;
            UIState.SetOpen(Panels.Pause, false);
            Time.timeScale = 1f;
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Swap(VisualElement screen) { _veil.Clear(); _veil.Add(screen); }

        void ShowMain()
        {
            var p = MenuScreens.Panel("PAUSED", SaveSlots.Pretty(SceneManager.GetActiveScene().name));
            MenuScreens.GlyphButton(p, "Resume", "arrowRight", Close);
            MenuScreens.GlyphButton(p, "Save game", "save", () => Swap(MenuScreens.Slots(true, ShowMain, null)));
            MenuScreens.GlyphButton(p, "Load game", "import", () => Swap(MenuScreens.Slots(false, ShowMain, Close)));
            MenuScreens.GlyphButton(p, "Settings", "ui-settings", () => Swap(MenuScreens.Settings(ShowMain)));
            MenuScreens.GlyphButton(p, "Main menu", "home", () => MenuScreens.Confirm(p, "Return to the main menu? Unsaved progress since the last save is lost.", () => { Close(); XenoarchRuntime.Instance?.ReturnToMainMenu(); }));
            MenuScreens.GlyphButton(p, "Quit", "exit", () => MenuScreens.Confirm(p, "Quit the game? Unsaved progress since the last save is lost.", MenuScreens.Quit));
            Swap(p);
        }
    }

    /// <summary>
    /// Autosave into the "auto" slot: every N minutes of play, a few seconds after landing / launching (any gameplay
    /// scene load), and after a discovery (journal entry). Settings in <see cref="GameSettings"/>. Never while loading,
    /// paused or with a panel open (retries shortly after).
    /// </summary>
    public sealed class AutosaveDirector : MonoBehaviour
    {
        float _nextTimed = -1f, _pendingAt = -1f;
        bool _hooked;

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            if (_hooked && XenoarchRuntime.Session != null) { XenoarchRuntime.Session.Discovery.JournalAdded -= OnJournal; XenoarchRuntime.Session.Exploration.SiteRevealed -= OnSiteRevealed; }
        }

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (m != LoadSceneMode.Single || !GameSettings.AutosaveOnTravel) return;
            Request(4f);
        }

        void OnJournal(DMI.Xenoarch.Discovery.JournalEntry e) { if (GameSettings.AutosaveOnDiscovery) Request(2f); }
        // t182u: an orbital pass or handheld scan that reveals a site is saved without waiting for the next travel.
        void OnSiteRevealed(string siteId, int level) { if (GameSettings.AutosaveOnDiscovery) Request(2f); }

        /// <summary>Ask for an autosave after <paramref name="delay"/> seconds (coalesces requests).</summary>
        public void Request(float delay)
        {
            float t = Time.unscaledTime + delay;
            _pendingAt = _pendingAt < 0f ? t : Mathf.Min(_pendingAt, t);
        }

        void Update()
        {
            var rt = XenoarchRuntime.Instance;
            if (!_hooked && XenoarchRuntime.Session != null) { XenoarchRuntime.Session.Discovery.JournalAdded += OnJournal; XenoarchRuntime.Session.Exploration.SiteRevealed += OnSiteRevealed; _hooked = true; }
            if (rt == null || !rt.SessionActive) { _nextTimed = -1f; return; }
            int minutes = GameSettings.AutosaveMinutes;
            if (minutes > 0)
            {
                if (_nextTimed < 0f) _nextTimed = Time.unscaledTime + minutes * 60f;
                else if (Time.unscaledTime >= _nextTimed) { Request(0f); _nextTimed = Time.unscaledTime + minutes * 60f; }
            }
            if (_pendingAt < 0f || Time.unscaledTime < _pendingAt) return;
            if (XenoarchRuntime.Scenes == null || XenoarchRuntime.Scenes.IsLoading || UIState.BlocksGameplay || Time.timeScale == 0f) { _pendingAt = Time.unscaledTime + 2f; return; }
            _pendingAt = -1f;
            if (rt.SaveGame(SaveSlots.Auto)) Toast.Show("Autosaved.", 1.5f);
        }
    }
}
