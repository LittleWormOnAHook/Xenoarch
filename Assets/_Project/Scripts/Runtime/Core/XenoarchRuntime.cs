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
            gameObject.AddComponent<DMI.Xenoarch.Exploration.SiteMarkerDirector>();
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
            }
        }

        void Update()
        {
            if (_sessionActive && !_loader.IsLoading) _session.PlaySeconds += Time.unscaledDeltaTime;
            if (!_session.Config.enableDebugHotkeys || _loader.IsLoading) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) SaveGame(_session.Config.quickSaveSlot);
            if (kb.f9Key.wasPressedThisFrame) LoadGame(_session.Config.quickSaveSlot);
        }

        // ------------------------------------------------------------------ game flow

        public void NewGame()
        {
            _session.StartNewGame();
            _sessionActive = true;
            _loader.Load(_session.Location.sceneName, SceneLoader.Arrival.AtSpawn(_session.Location.spawnId));
        }

        public void ReturnToMainMenu()
        {
            _sessionActive = false;
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
            _session.Saves.Apply(file); // state first, so scene objects read restored state in Awake/Start
            _session.PlaySeconds = file.playSeconds;
            _sessionActive = true;
            var loc = _session.Location;
            string scene = string.IsNullOrEmpty(loc.sceneName) ? _session.Config.firstGameplayScene : loc.sceneName;
            var arrival = loc.hasPose
                ? new SceneLoader.Arrival { usePose = true, position = loc.position, yaw = loc.yaw }
                : SceneLoader.Arrival.AtSpawn(loc.spawnId);
            Debug.Log($"[Xenoarch] Loading slot '{slot}' (save v{file.version}) -> {scene}");
            return _loader.Load(scene, arrival);
        }

        static PlayerLocationState NewLocation(string scene, string spawn) => new PlayerLocationState { sceneName = scene, spawnId = spawn };
    }
}
