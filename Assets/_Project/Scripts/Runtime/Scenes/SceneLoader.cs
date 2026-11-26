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
        [Tooltip("Give up waiting when nothing has advanced (shaders / terrain) for this long (s).")] public float stallSeconds = 20f;
        [Tooltip("Hard cap on the loading-screen wait after the scene loads, even while work is still advancing (s).")] public float maxHoldSeconds = 180f;
        [Tooltip("Frames to keep the loading screen up after everything reports ready (lets the first frames render).")] public int settleFrames = 30;
        [Tooltip("Shortest time the loading screen stays up after the scene loads (s), so late materials finish too.")] public float minHoldSeconds = 2.5f;

        /// <summary>Loading-screen gates: each returns a status line while its system is still busy, or null when ready
        /// (shader compile, Digger replay, landing-zone pick...). The loading screen stays up until all return null.</summary>
        public static readonly System.Collections.Generic.List<Func<string>> ReadyGates = new System.Collections.Generic.List<Func<string>>();
        /// <summary>Optional 0..1 progress for a gate in <see cref="ReadyGates"/> (else busy = 0, idle = 1).</summary>
        public static readonly System.Collections.Generic.Dictionary<Func<string>, Func<float>> GateProgress = new System.Collections.Generic.Dictionary<Func<string>, Func<float>>();

        // Loading-bar weights (sum 1): scene async load, shader compile / warm-up, ready gates (Digger replay, landing pick), settle.
        // Stages fill in order (a later stage never adds to the bar before the earlier one is done), so the bar tracks the
        // real work 1:1 from 0%. Shader progress counts only the work left at the start of the load (cached passes are free).
        const float WLoad = 0.30f, WShaders = 0.45f, WGates = 0.17f, WSettle = 0.08f;
        [Tooltip("Editor: after the tracked shaders are compiled, wait at most this long for other async compiles (s).")] public float compileGraceSeconds = 3f;
        [Tooltip("With the hidden warm pass drawing every tracked material, wait at most this long for the compiles it starts (s).")] public float warmGraceSeconds = 25f;
        [Tooltip("Frames the hidden warm pass draws before the shader stage can finish.")] public int warmFrames = 4;
        float _shown;

        /// <summary>Optional title for the next load (else derived: "Returning to the ship" / "Landing: body").</summary>
        public static string NextTitle;
        /// <summary>Optional second line for the next load (e.g. the chosen landing zone).</summary>
        public static string NextSubtitle;
        /// <summary>Arrival of the load in progress / last load (directors use it to place the player at runtime spawns).</summary>
        public static Arrival CurrentArrival { get; private set; }
        LoadingScreenView _view;

        public bool IsLoading { get; private set; }
        /// <summary>True while any loader is loading or covering the scene.</summary>
        public static bool Busy => _instance != null && _instance.IsLoading;
        static SceneLoader _instance;
        void OnEnable() => _instance = this;
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
            _shown = 0f;
            _view?.SetTitle(TitleFor(SceneManager.GetActiveScene().name)); NextTitle = null; _view?.SetSubtitle(null); NextSubtitle = null; _view?.SetVisible(true);
            var track = ShaderPrewarm.TrackScene(SceneManager.GetActiveScene().name);
            ShowProgress("Loading", 0f); // scene already open (direct play): the bar covers the remaining work only
            UIState.SetOpen(Panels.Loading, true);
            PlayerRigLocator.Find()?.SetInputEnabled(false);
            yield return null;
            yield return HoldUntilReady(track, 0f);
            UIState.SetOpen(Panels.Loading, false);
            yield return Fade(0f);
            _view?.SetVisible(false);
            PlayerRigLocator.Find()?.SetInputEnabled(true);
            IsLoading = false;
        }

        /// <summary>Shows a monotonic, never-early percentage: it only reaches 100% when everything is truly ready.</summary>
        void ShowProgress(string status, float p01, bool final = false)
        {
            _shown = Mathf.Max(_shown, Mathf.Clamp01(p01));
            int pct = final ? 100 : Mathf.Min(99, Mathf.FloorToInt(_shown * 100f));
            _view?.SetStatus($"{status}   {pct}%");
            _view?.SetProgress(pct / 100f);
        }

        /// <summary>Weighted stages after the scene is in: shaders (tracked passes compiled / variants warmed), ready
        /// gates (Digger replay, landing pick; each with its own progress when it has one), then settle frames.</summary>
        IEnumerator HoldUntilReady(ShaderPrewarm.Tracker track, float baseP)
        {
            var rig = PlayerRigLocator.Find(); if (rig != null) ShaderPrewarm.TrackRenderers(track, rig.Root);
            DMI.Xenoarch.Excavation.ExcavationSite.PrewarmRuntimeMaterials();
            ShaderPrewarm.TrackRuntime(track);
            ShaderPrewarm.WarmPass warm = null;
            var seen = new System.Collections.Generic.HashSet<Func<string>>();
            float held = 0f, grace = 0f, sinceAdvance = 0f, lastWork = -1f; int quiet = 0; bool shuttleTracked = false, gaveUp = false;
            string lastBusy = null;
            while (true)
            {
                if (!shuttleTracked)
                {
                    var sh = DMI.Xenoarch.Exploration.ShuttleDirector.Instance != null ? DMI.Xenoarch.Exploration.ShuttleDirector.Instance.Shuttle : null;
                    if (sh != null) { ShaderPrewarm.TrackRenderers(track, sh.transform); shuttleTracked = true; }
                }
                // hidden warm pass: draw every tracked material once in this scene's real lighting (behind the overlay)
                var cam = Camera.main;
                if (warm == null && cam != null) warm = ShaderPrewarm.WarmPass.Begin(track, cam);
                warm?.Sync(track); warm?.Tick();
                // shaders (relative to what was left when this load started)
                float sp = track.Relative;
                bool compiling = ShaderPrewarm.AnythingCompiling;
                if (sp >= 1f && compiling) grace += Time.unscaledDeltaTime;
                bool warmPending = warm != null && warm.Frames < warmFrames;
                bool shaderBusy = sp < 1f || warmPending || (compiling && grace < (warm != null ? warmGraceSeconds : compileGraceSeconds));
                float shaderStage = sp < 1f ? sp * 0.97f : shaderBusy ? 0.98f : 1f;
                // gates (only once the shaders are done, so the bar never jumps ahead of real work)
                float gp = Gates(seen, out string gateStatus);
                string busy = shaderBusy ? "Preparing materials" : gateStatus;
                if (busy != null) lastBusy = shaderBusy ? $"shaders {track.Done}/{track.Total} compiled, {(compiling ? "compiler busy" : "compiler idle")}" : gateStatus;
                float gatesStage = shaderBusy ? 0f : gp;
                // settle
                if (busy == null) quiet++; else quiet = 0;
                float settle = busy == null ? Mathf.Min(quiet / (float)Mathf.Max(1, settleFrames), Mathf.Clamp01(held / Mathf.Max(0.01f, minHoldSeconds))) : 0f;
                float p = baseP + (1f - baseP) * (WShaders * shaderStage + WGates * gatesStage + WSettle * settle) / (WShaders + WGates + WSettle);
                ShowProgress(busy ?? "Settling", p);
                if (busy == null && quiet >= settleFrames && held >= minHoldSeconds) break;
                // stall detection: keep waiting while anything advances; give up only after stallSeconds of no progress
                // (monotonic: a gate that flickers busy / idle, or the compiler toggling, does not count as progress)
                float work = track.Done + gp * 1000f + quiet * 0.001f;
                if (work > lastWork + 0.0005f) { lastWork = work; sinceAdvance = 0f; } else sinceAdvance += Time.unscaledDeltaTime * (compiling ? 0.25f : 1f);
                if (shaderBusy && !compiling && sinceAdvance >= 3f)
                {
                    // nothing compiling and no pass finished for 3 s: the rest never compile here (wrong pipeline / no
                    // variants). Skip them now and in later loads instead of holding the screen for them.
                    int n = track.SkipStalled();
                    if (n > 0) { Debug.Log($"[Xenoarch] Loading screen: {n} shader passes never compile in this pipeline; skipped from now on"); sinceAdvance = 0f; }
                }
                if (sinceAdvance >= stallSeconds || held >= maxHoldSeconds) { gaveUp = true; break; }
                held += Time.unscaledDeltaTime;
                yield return null;
            }
            int warmCount = warm != null ? warm.Count : 0;
            warm?.End();
            if (warmCount > 0) Debug.Log($"[Xenoarch] Shader warm pass: drew {warmCount} materials off-screen before reveal");
            if (gaveUp)
                Debug.LogWarning($"[Xenoarch] Loading screen: stopped waiting after {held:0}s ({(sinceAdvance >= stallSeconds ? $"no progress for {stallSeconds:0}s" : $"cap {maxHoldSeconds:0}s")}): " +
                                 $"{lastBusy ?? BusyStatus() ?? "unknown"}; shaders {track.Done}/{track.Total}");
            else if (held > 0.5f) Debug.Log($"[Xenoarch] Loading screen held {held:0.0}s (shaders {track.Done}/{track.Total}, {Mathf.Max(0, track.StartDone)} already compiled before this load)");
            ShowProgress("Ready", 1f, true);
        }

        /// <summary>Ready-gate stage 0..1 over the gates seen busy during this hold (never-busy gates don't count).</summary>
        static float Gates(System.Collections.Generic.HashSet<Func<string>> seen, out string status)
        {
            status = null;
            float sum = 0f; int n = 0;
            foreach (var g in ReadyGates)
            {
                if (g == null) continue;
                string s = null;
                try { s = g(); } catch (Exception e) { Debug.LogException(e); }
                bool busy = !string.IsNullOrEmpty(s);
                if (busy) { seen.Add(g); status ??= s; }
                if (!seen.Contains(g)) continue;
                float p = 1f;
                if (busy) { p = 0f; if (GateProgress.TryGetValue(g, out var f) && f != null) { try { p = Mathf.Min(0.95f, Mathf.Clamp01(f())); } catch (Exception e) { Debug.LogException(e); } } }
                sum += p; n++;
            }
            return n == 0 ? 1f : sum / n;
        }

        IEnumerator LoadRoutine(string sceneName, Arrival arrival, Action onLoaded)
        {
            IsLoading = true;
            CurrentArrival = arrival;
            UIState.SetOpen(Panels.Loading, true);
            LoadStarted?.Invoke(sceneName);
            PlayerRigLocator.Find()?.SetInputEnabled(false);

            if (_view != null)
            {
                _view.SetTitle(TitleFor(sceneName)); NextTitle = null;
                _view.SetSubtitle(NextSubtitle); NextSubtitle = null;
                _view.SetVisible(true);
            }
            _shown = 0f; ShowProgress("Loading scene", 0f);
            yield return Fade(1f);
            // Editor: start async-compiling the destination's materials now, while the scene streams in.
            var track = ShaderPrewarm.TrackScene(sceneName);
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            // op.progress runs 0..0.9 while loading; activation (Awake of the new scene) completes the last part.
            while (op != null && !op.isDone) { ShowProgress("Loading scene", WLoad * 0.9f * Mathf.Clamp01(op.progress / 0.9f)); yield return null; }
            yield return null; // let Awake/OnEnable/Start of the new scene run
            ShowProgress("Placing crew", WLoad * 0.95f);

            PlacePlayer(arrival);
            onLoaded?.Invoke();
            Debug.Log($"[Xenoarch] Scene loaded: {sceneName}");
            LoadCompleted?.Invoke(sceneName);

            // Hold the screen until shaders and terrain are ready (the cyan placeholder in the Editor is async shader
            // compilation; the camera keeps rendering behind this overlay, which is what triggers those compiles).
            yield return HoldUntilReady(track, WLoad);

            UIState.SetOpen(Panels.Loading, false);
            yield return Fade(0f);
            _view?.SetVisible(false);
            PlayerRigLocator.Find()?.SetInputEnabled(true);
            IsLoading = false;
        }

        void Update()
        {
            if (IsLoading) _view?.Tick(Time.unscaledDeltaTime);
            // Background prewarm (on the ship, before any jump): a little per frame normally, much more behind the loading screen.
            ShaderPrewarm.Tick(IsLoading ? 16 : 4);
            var cfg = DMI.Xenoarch.Core.XenoarchRuntime.Session?.Config;
            ShaderPrewarm.DevAudit(cfg != null && cfg.logShaderWarm, IsLoading);
        }


        static string BusyStatus()
        {
            if (ShaderPrewarm.AnythingCompiling) return "shader compiler busy";
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
            // "Returning to the ship" is only set by the shuttle ascent (ShipTravel.ReturnToShip); other ship loads are neutral.
            if (sceneName == Core.SceneNames.ShipStarter) return "Aboard the ship";
            var body = Core.XenoarchRuntime.Session?.Exploration?.CurrentBody;
            if (sceneName.Contains("Excavation") || sceneName.Contains("Surface")) return body != null ? $"Landing: {body.displayName}" : "Landing";
            if (sceneName == Core.SceneNames.MainMenu) return "Xenoarch";
            return body != null ? body.displayName : sceneName.Replace('_', ' ');
        }

        /// <summary>Frame of the last <see cref="PlacePlayer"/> (directors that must act after the player is placed).</summary>
        public static int PlacedFrame { get; private set; } = -1;

        static void PlacePlayer(Arrival arrival)
        {
            PlacedFrame = Time.frameCount;
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

        /// <summary>Phase 1D: fade the screen to black (1) or back (0) for in-scene moves (buried chambers).</summary>
        public IEnumerator FadeTo(float target) => Fade(target);

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
