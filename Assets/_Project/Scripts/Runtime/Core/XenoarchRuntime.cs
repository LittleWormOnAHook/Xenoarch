using System.IO;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Save;
using DMI.Xenoarch.Scenes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Core
{
    /// <summary>
    /// Composition root. Created automatically before the first scene loads (so any scene can be played directly in
    /// the editor), persists across scenes, owns the <see cref="GameSession"/> and the <see cref="SceneLoader"/>.
    /// This is the single static entry point; systems should take the session/services as parameters where practical.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class XenoarchRuntime : MonoBehaviour
    {
        public static XenoarchRuntime Instance { get; private set; }
        public static GameSession Session => Instance != null ? Instance._session : null;
        public static SceneLoader Scenes => Instance != null ? Instance._loader : null;

        GameSession _session;
        SceneLoader _loader;
        bool _sessionActive;
        /// <summary>True while a game is running (not on the main menu).</summary>
        public bool SessionActive => _sessionActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("[XenoarchRuntime]");
            DontDestroyOnLoad(go);
            go.AddComponent<XenoarchRuntime>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var config = Resources.Load<XenoarchConfig>(XenoarchConfig.ResourcePath);
            if (config == null)
            {
                Debug.LogWarning("[Xenoarch] Resources/XenoarchConfig missing: using defaults (run Xenoarch > Setup > Create Foundation Assets)");
                config = ScriptableObject.CreateInstance<XenoarchConfig>();
            }
            IItemLookup items = config.itemRegistry != null ? config.itemRegistry : ScriptableObject.CreateInstance<ItemRegistry>();
            var storage = new FileSaveStorage(Path.Combine(Application.persistentDataPath, config.saveFolderName));
            _session = new GameSession(config, items, storage);

            _loader = gameObject.AddComponent<SceneLoader>();
            _loader.fadeDuration = config.fadeDuration;
            gameObject.AddComponent<DMI.Xenoarch.Galaxy.EnvironmentDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Galaxy.SunDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Galaxy.OrbitViewDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Exploration.SiteMarkerDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Excavation.ExcavationDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Exploration.ShuttleDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Valuables.ValuableSiteDirector>();
            gameObject.AddComponent<DMI.Xenoarch.UI.MinimapHud>();
            gameObject.AddComponent<DMI.Xenoarch.UI.HelmetHud>();
            gameObject.AddComponent<DMI.Xenoarch.UI.DetectorSweepHud>();
            gameObject.AddComponent<DMI.Xenoarch.UI.InventoryWindow>();
            gameObject.AddComponent<DMI.Xenoarch.Materials.MaterialsDirector>();
            gameObject.AddComponent<DMI.Xenoarch.UI.PauseMenu>();
            gameObject.AddComponent<DMI.Xenoarch.Excavation.DigSiteFlags>();
            gameObject.AddComponent<DMI.Xenoarch.UI.DigSiteMapWindow>();
            gameObject.AddComponent<DMI.Xenoarch.UI.JournalReader>();
            gameObject.AddComponent<DMI.Xenoarch.Journals.JournalDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Vehicles.RoverDirector>();   // t187u
            gameObject.AddComponent<DMI.Xenoarch.UI.RoverHud>();              // t206u
            gameObject.AddComponent<DMI.Xenoarch.UI.AutosaveDirector>();
            gameObject.AddComponent<DMI.Xenoarch.Economy.VendorDirector>();
            gameObject.AddComponent<DMI.Xenoarch.UI.DiscoveryJournalWindow>();
            gameObject.AddComponent<DMI.Xenoarch.Excavation.ChamberDirector>();
            gameObject.AddComponent<DMI.Xenoarch.World.RockScatterDirector>();
            _session.Artifacts.ResearchCompleted += (a, op, result) =>
                DMI.Xenoarch.UI.Toast.Show($"Lab: {DMI.Xenoarch.Artifacts.ResearchRules.Label(op)} done. {result}");
            _session.Discovery.JournalAdded += e => DMI.Xenoarch.UI.Toast.Show($"Journal: {e.text}", 8f);
            _session.Recovery.Notice += msg => DMI.Xenoarch.UI.Toast.Show(msg, 6f);
            Debug.Log($"[Xenoarch] Runtime ready (save folder: {storage.Folder})");
        }

        void Start()
        {
            // Playing a gameplay scene directly in the editor: start an implicit new game so systems have state.
            var active = SceneManager.GetActiveScene().name;
            if (active != SceneNames.Bootstrap && active != SceneNames.MainMenu && PlayerRigLocator.Find() != null)
            {
                _session.StartNewGame();
                _session.Location.sceneName = active;
                _sessionActive = true;
                Debug.Log($"[Xenoarch] Direct play of '{active}': implicit new game started");
                _loader.CoverCurrentScene(); // loading screen until shaders/terrain are ready
            }
        }

        void Update()
        {
            if (_sessionActive && !_loader.IsLoading) { _session.PlaySeconds += Time.unscaledDeltaTime; _session.Artifacts.Tick(Time.deltaTime); _session.Materials.Tick(Time.deltaTime); _session.Crafting.Tick(Time.deltaTime); }
            // Phase 2 M1: cheap, idempotent recovery check for mandatory items (acts only when something went missing).
            if (_sessionActive && !_loader.IsLoading && Time.unscaledTime >= _nextRecoveryCheck) { _nextRecoveryCheck = Time.unscaledTime + 5f; _session.Recovery.Ensure("periodic"); }
            if (!_session.Config.enableDebugHotkeys || _loader.IsLoading) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) SaveGame(_session.Config.quickSaveSlot);
            if (kb.f9Key.wasPressedThisFrame) LoadGame(_session.Config.quickSaveSlot);
        }

        // ------------------------------------------------------------------ game flow

        float _nextRecoveryCheck;

        /// <summary>
        /// Phase 2 M1: New Game clears the development save slots (autosave, quick save, manual slots 1-6, with their
        /// .bak/.tmp files) before the new expedition starts. Only those slot files are deleted; nothing else in the save
        /// folder or the project is touched. (Before M1, New Game kept every save; the menu said so.)
        /// </summary>
        public int ClearDevSaves()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var (slot, _) in DMI.Xenoarch.UI.SaveSlots.All()) names.Add(slot);
            int n = _session.Saves.DeleteSlots(names);
            Debug.Log($"[Xenoarch] New Game: cleared {n} development save slot(s) ({string.Join(", ", names)})");
            return n;
        }

        public void NewGame()
        {
            ClearDevSaves();
            _session.StartNewGame();
            _sessionActive = true;
            SceneLoader.NextTitle = "New expedition";
            SceneLoader.NextSubtitle = DMI.Xenoarch.Galaxy.ShipTravel.LocationText(_session.Exploration);
            _loader.Load(_session.Location.sceneName, SceneLoader.Arrival.AtSpawn(_session.Location.spawnId));
        }

        public void ReturnToMainMenu()
        {
            _sessionActive = false;
            Time.timeScale = 1f;
            SceneLoader.NextTitle = "Main menu";
            _loader.Load(_session.Config.mainMenuScene, default);
        }

        /// <summary>Travel inside the current session (keeps state; records the destination as the player location).</summary>
        public bool Travel(string sceneName, string spawnId)
        {
            if (!_loader.Load(sceneName, SceneLoader.Arrival.AtSpawn(spawnId))) return false;
            _session.Location = NewLocation(sceneName, spawnId);
            return true;
        }

        public bool SaveGame(string slot)
        {
            var rig = PlayerRigLocator.Find();
            if (!_sessionActive || rig == null) { Debug.LogWarning("[Xenoarch] Save ignored: no active gameplay session/player"); return false; }
            var loc = _session.Location;
            loc.sceneName = SceneManager.GetActiveScene().name;
            loc.hasPose = true;
            loc.position = rig is IPlayerSeat seat && seat.Seated ? seat.SeatExitPosition : rig.Root.position; // never save inside a chair
            loc.yaw = rig.Root.eulerAngles.y;
            bool ok = _session.Saves.Save(slot, _session.PlaySeconds);
            if (ok) Debug.Log($"[Xenoarch] Saved slot '{slot}' in {loc.sceneName}");
            return ok;
        }

        public bool LoadGame(string slot)
        {
            if (!_session.Saves.TryRead(slot, out var file, out var error)) { Debug.LogWarning($"[Xenoarch] Load failed: {error}"); return false; }
            Time.timeScale = 1f;
            _session.Saves.Apply(file); // state first, so scene objects read restored state in Awake/Start
            _session.PlaySeconds = file.playSeconds;
            _session.ApplyDevLoadout();
            _session.GrantStarterVehicles();
            _sessionActive = true;
            var loc = _session.Location;
            string scene = string.IsNullOrEmpty(loc.sceneName) ? _session.Config.firstGameplayScene : loc.sceneName;
            var arrival = loc.hasPose
                ? new SceneLoader.Arrival { usePose = true, position = loc.position, yaw = loc.yaw }
                : SceneLoader.Arrival.AtSpawn(loc.spawnId);
            Debug.Log($"[Xenoarch] Loading slot '{slot}' (save v{file.version}) -> {scene}");
            SceneLoader.NextTitle = "Loading saved game";
            SceneLoader.NextSubtitle = DMI.Xenoarch.Galaxy.ShipTravel.LocationText(_session.Exploration);
            return _loader.Load(scene, arrival);
        }

        static PlayerLocationState NewLocation(string scene, string spawn) => new PlayerLocationState { sceneName = scene, spawnId = spawn };
    }
}
