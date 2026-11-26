using System;
using System.Collections;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Scenes
{
    /// <summary>
    /// Single-scene transitions behind a landing/loading screen (destination title, status, spinning ring) that stays up
    /// until the scene, shaders and terrain are ready (<see cref="ReadyGates"/>), then fades into the scene. Pattern taken from ASH ASHLoadingScreen/ASHSceneFlow
    /// (async load behind an overlay), rewritten without ASH dependencies. After the load it places the
    /// <see cref="IPlayerRig"/> at a saved pose or a named <see cref="PlayerSpawnPoint"/>.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour
    {
        public float fadeDuration = 0.35f;
        [Tooltip("Longest the loading screen waits for shaders / terrain after the scene loads (s).")] public float maxHoldSeconds = 30f;
        [Tooltip("Frames to keep the loading screen up after everything reports ready (lets the first frames render).")] public int settleFrames = 6;

        /// <summary>Loading-screen gates: each returns a status line while its system is still busy, or null when ready
        /// (shader compile, Digger replay, landing-zone pick...). The loading screen stays up until all return null.</summary>
        public static readonly System.Collections.Generic.List<Func<string>> ReadyGates = new System.Collections.Generic.List<Func<string>>();

        /// <summary>Optional title for the next load (else derived: "Returning to the ship" / "Landing: body").</summary>
        public static string NextTitle;
        LoadingScreenView _view;
        static ShaderVariantCollection _warm;
        static bool _warmed;

        public bool IsLoading { get; private set; }
        public event Action<string> LoadStarted;
        public event Action<string> LoadCompleted;

        public const string FadeUxml = "UI/Fade";
        VisualElement _fade;
        float _alpha;

        /// <summary>Arrival request: a spawn id, or an exact pose (loading a save).</summary>
        public struct Arrival
        {
            public string spawnId;
            public bool usePose;
            public Vector3 position;
            public float yaw;
            public static Arrival AtSpawn(string id) => new Arrival { spawnId = id };
        }

        private void Awake() => BuildOverlay();

        public bool Load(string sceneName, Arrival arrival, Action onLoaded = null)
        {
            if (IsLoading) { Debug.LogWarning($"[Xenoarch] SceneLoader busy; ignored request for '{sceneName}'"); return false; }
            if (!Application.CanStreamedLevelBeLoaded(sceneName)) { Debug.LogError($"[Xenoarch] Scene '{sceneName}' is not in Build Settings"); return false; }
            StartCoroutine(LoadRoutine(sceneName, arrival, onLoaded));
            return true;
        }

        /// <summary>Show the loading screen over the already-open scene (direct play in the Editor) until it is ready.</summary>
        public void CoverCurrentScene()
        {
            if (IsLoading) return;
            StartCoroutine(CoverRoutine());
        }

        IEnumerator CoverRoutine()
        {
            IsLoading = true;
            if (_fade == null) BuildOverlay();
            if (_fade != null) { _alpha = 1f; _fade.style.opacity = 1f; _fade.pickingMode = PickingMode.Position; }
            _view?.SetTitle(TitleFor(SceneManager.GetActiveScene().name)); _view?.SetStatus("Loading"); _view?.SetVisible(true);
            UIState.SetOpen(Panels.Loading, true);
            PlayerRigLocator.Find()?.SetInputEnabled(false);
            yield return null;
            yield return HoldUntilReady();
            UIState.SetOpen(Panels.Loading, false);
            yield return Fade(0f);
            _view?.SetVisible(false);
            PlayerRigLocator.Find()?.SetInputEnabled(true);
            IsLoading = false;
        }

        IEnumerator HoldUntilReady()
        {
            if (!_warmed && !Application.isEditor)
            {
                _warmed = true;
                _warm ??= Resources.Load<ShaderVariantCollection>("Shaders/XenoarchVariants");
                if (_warm != null) { _view?.SetStatus("Warming shaders"); yield return null; _warm.WarmUp(); }
            }
            float held = 0f; int quiet = 0;
            while (held < maxHoldSeconds)
            {
                string busy = BusyStatus();
                if (busy == null) { if (++quiet >= settleFrames) break; }
                else { quiet = 0; _view?.SetStatus(busy); }
                held += Time.unscaledDeltaTime;
                yield return null;
            }
            if (held >= maxHoldSeconds) Debug.LogWarning($"[Xenoarch] Loading screen: gave up waiting after {maxHoldSeconds:0}s ({BusyStatus()})");
            else if (held > 0.5f) Debug.Log($"[Xenoarch] Loading screen held {held:0.0}s for shaders/terrain");
            _view?.SetStatus("Ready");
        }

        IEnumerator LoadRoutine(string sceneName, Arrival arrival, Action onLoaded)
        {
            IsLoading = true;
            UIState.SetOpen(Panels.Loading, true);
            LoadStarted?.Invoke(sceneName);
            PlayerRigLocator.Find()?.SetInputEnabled(false);

            if (_view != null)
            {
                _view.SetTitle(TitleFor(sceneName)); NextTitle = null;
                _view.SetStatus("Loading"); _view.SetVisible(true);
            }
            yield return Fade(1f);
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            while (op != null && !op.isDone) { _view?.SetStatus($"Loading {Mathf.RoundToInt(Mathf.Clamp01(op.progress / 0.9f) * 100f)}%"); yield return null; }
            yield return null; // let Awake/OnEnable/Start of the new scene run

            PlacePlayer(arrival);
            onLoaded?.Invoke();
            Debug.Log($"[Xenoarch] Scene loaded: {sceneName}");
            LoadCompleted?.Invoke(sceneName);

            // Hold the screen until shaders and terrain are ready (the cyan placeholder in the Editor is async shader
            // compilation; the camera keeps rendering behind this overlay, which is what triggers those compiles).
            yield return HoldUntilReady();

            UIState.SetOpen(Panels.Loading, false);
            yield return Fade(0f);
            _view?.SetVisible(false);
            PlayerRigLocator.Find()?.SetInputEnabled(true);
            IsLoading = false;
        }

        void Update() { if (IsLoading) _view?.Tick(Time.unscaledDeltaTime); }

        static string BusyStatus()
        {
#if UNITY_EDITOR
            if (UnityEditor.ShaderUtil.anythingCompiling) return "Compiling shaders";
#endif
            foreach (var g in ReadyGates)
            {
                if (g == null) continue;
                string s = null;
                try { s = g(); } catch (Exception e) { Debug.LogException(e); }
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return null;
        }

        static string TitleFor(string sceneName)
        {
            if (!string.IsNullOrEmpty(NextTitle)) return NextTitle;
            if (sceneName == Core.SceneNames.ShipStarter) return "Returning to the ship";
            var body = Core.XenoarchRuntime.Session?.Exploration?.CurrentBody;
            if (sceneName.Contains("Excavation") || sceneName.Contains("Surface")) return body != null ? $"Landing: {body.displayName}" : "Landing";
            if (sceneName == Core.SceneNames.MainMenu) return "Xenoarch";
            return body != null ? body.displayName : sceneName.Replace('_', ' ');
        }

        static void PlacePlayer(Arrival arrival)
        {
            var rig = PlayerRigLocator.Find();
            if (rig == null) return; // menus have no player
            if (arrival.usePose)
            {
                rig.Teleport(arrival.position, Quaternion.Euler(0f, arrival.yaw, 0f));
                return;
            }
            var spawn = PlayerSpawnPoint.Find(string.IsNullOrEmpty(arrival.spawnId) ? Core.PlayerSpawnIds.Default : arrival.spawnId);
            if (spawn != null) rig.Teleport(spawn.transform.position, Quaternion.Euler(0f, spawn.transform.eulerAngles.y, 0f));
            else Debug.LogWarning("[Xenoarch] No PlayerSpawnPoint in scene; player left where it was authored");
        }

        IEnumerator Fade(float target)
        {
            if (_fade == null) BuildOverlay();
            if (_fade == null) yield break;
            _fade.pickingMode = target > 0.5f ? PickingMode.Position : PickingMode.Ignore;
            float start = _alpha, t = 0f, d = Mathf.Max(0.0001f, fadeDuration);
            while (t < d)
            {
                t += Time.unscaledDeltaTime;
                _alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / d));
                _fade.style.opacity = _alpha;
                yield return null;
            }
            _alpha = target;
            _fade.style.opacity = target;
        }

        void BuildOverlay()
        {
            if (_fade != null) return;
            var go = new GameObject("FadeOverlay");
            go.transform.SetParent(transform, false);
            var doc = XenoarchUI.CreateDocument(go, FadeUxml, 1000);
            _fade = doc != null ? doc.rootVisualElement.Q("fade") : null;
            if (_fade == null) { Debug.LogWarning("[Xenoarch] SceneLoader: fade overlay unavailable; transitions will cut"); return; }
            _fade.style.opacity = 0f;
            _fade.pickingMode = PickingMode.Ignore;
            _view = new LoadingScreenView(_fade);
            _view.SetVisible(false);
        }
    }
}
