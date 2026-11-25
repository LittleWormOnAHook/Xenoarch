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
    /// Single-scene transitions with a fade to black. Pattern taken from ASH ASHLoadingScreen/ASHSceneFlow
    /// (async load behind an overlay), rewritten without ASH dependencies. After the load it places the
    /// <see cref="IPlayerRig"/> at a saved pose or a named <see cref="PlayerSpawnPoint"/>.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour
    {
        public float fadeDuration = 0.35f;

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

        IEnumerator LoadRoutine(string sceneName, Arrival arrival, Action onLoaded)
        {
            IsLoading = true;
            UIState.SetOpen(Panels.Loading, true);
            LoadStarted?.Invoke(sceneName);
            PlayerRigLocator.Find()?.SetInputEnabled(false);

            yield return Fade(1f);
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            while (op != null && !op.isDone) yield return null;
            yield return null; // let Awake/OnEnable/Start of the new scene run

            PlacePlayer(arrival);
            onLoaded?.Invoke();
            Debug.Log($"[Xenoarch] Scene loaded: {sceneName}");
            LoadCompleted?.Invoke(sceneName);

            UIState.SetOpen(Panels.Loading, false);
            yield return Fade(0f);
            PlayerRigLocator.Find()?.SetInputEnabled(true);
            IsLoading = false;
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
        }
    }
}
