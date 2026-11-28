using DMI.Xenoarch.Audio;
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
                else if (DMI.Xenoarch.Lighting.FloodLightPlacer.TryHandleEscape()) { }
                else if (DMI.Xenoarch.Ship.CockpitFtlDisplays.TryHandleEscape()) { }
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
            UISound.Play(UISoundEvent.WindowOpen);
        }

        public void Close()
        {
            _veil?.RemoveFromHierarchy();
            _veil = null;
            UIState.SetOpen(Panels.Pause, false);
            Time.timeScale = 1f;
            UISound.Play(UISoundEvent.WindowClose);
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Swap(VisualElement screen) { _veil.Clear(); _veil.Add(screen); }

        void ShowMain()
        {
            // t246u Morpheus layout: PAUSED + location on the left with the button column, session card on the right.
            var s = XenoarchRuntime.Session;
            string where = s != null ? DMI.Xenoarch.Galaxy.ShipTravel.LocationText(s.Exploration) : SaveSlots.Pretty(SceneManager.GetActiveScene().name);
            var root = MenuScreens.MenuLayout(false, "Paused", where, out var col, out var side);
            MenuScreens.ColumnButton(col, "Resume", "arrowRight", Close).AddToClassList("xa-m-sel");
            MenuScreens.ColumnButton(col, "Save game", "save", () => Swap(MenuScreens.Slots(true, ShowMain, null)));
            MenuScreens.ColumnButton(col, "Load game", "import", () => Swap(MenuScreens.Slots(false, ShowMain, Close)));
            MenuScreens.ColumnButton(col, "Settings", "ui-settings", () => Swap(MenuScreens.Settings(ShowMain)));
            MenuScreens.ColumnButton(col, "Main menu", "home", () => MenuScreens.Confirm(root, "Return to the main menu? Unsaved progress since the last save is lost.", () => { Close(); XenoarchRuntime.Instance?.ReturnToMainMenu(); }));
            MenuScreens.ColumnButton(col, "Quit", "exit", () => MenuScreens.Confirm(root, "Quit the game? Unsaved progress since the last save is lost.", MenuScreens.Quit));
            if (s != null) side.Add(SessionCard(s));
            var lg = MUI.Legend(root, ("Esc", "Resume")); lg.style.position = Position.Absolute; lg.style.right = 48; lg.style.bottom = 30;
            Swap(root);
        }

        static VisualElement SessionCard(GameSession s)
        {
            var card = new VisualElement(); card.AddToClassList("xa-m-panel"); card.style.width = 520;
            card.style.paddingLeft = 26; card.style.paddingRight = 26; card.style.paddingTop = 20; card.style.paddingBottom = 22;
            MUI.L(card, "SESSION", "xa-m-head__title");
            var o = s.Objectives;
            var f = o?.Focused() ?? o?.Main();
            if (f != null)
            {
                var row = new VisualElement(); row.AddToClassList("xa-m-card__row"); row.style.marginTop = 14;
                row.Add(XenoarchIcons.Glyph("star", "xa-m-star"));
                MUI.L(row, f.title, "xa-m-card__title");
                card.Add(row);
                float p = MUI.Progress(f, out int d, out int t);
                MUI.BarRow(card, p, $"{d} / {t}", f.kind != DMI.Xenoarch.Objectives.ObjectiveKind.Main);
            }
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; grid.style.marginTop = 12;
            void Cell(string k, string v)
            {
                var c = new VisualElement(); c.style.width = Length.Percent(50); c.style.marginTop = 8;
                MUI.L(c, k, "xa-m-dim"); MUI.L(c, v, "xa-m-text"); grid.Add(c);
            }
            Cell("Played", SaveSlots.PlayTime(s.PlaySeconds));
            Cell("Credits", $"{s.Economy.Credits:N0} cr");
            Cell("Artifacts found", s.Artifacts.State.instances.Count.ToString());
            Cell("Log entries", (s.Log?.State.entries?.Count ?? 0).ToString());
            card.Add(grid);
            return card;
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
