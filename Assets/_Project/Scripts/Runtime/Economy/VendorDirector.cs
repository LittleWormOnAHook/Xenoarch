using DMI.Xenoarch.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Economy
{
    /// <summary>
    /// Phase 1D: on each scene load, gives every <see cref="VendorDefinition"/> for that scene a working terminal on its
    /// anchor object (replacing the "offline" placeholder interactable). Runtime only; the scene file is not changed.
    /// </summary>
    public sealed class VendorDirector : MonoBehaviour
    {
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => Bind(SceneManager.GetActiveScene());

        EconomyService _hooked;
        void Update()
        {
            // Rank-up toast; re-hooks when a new game / load replaces the session.
            var eco = XenoarchRuntime.Session?.Economy;
            if (eco == _hooked) return;
            if (_hooked != null) _hooked.RankUp -= OnRankUp;
            _hooked = eco;
            if (eco != null) eco.RankUp += OnRankUp;
        }
        void OnDestroy() { if (_hooked != null) _hooked.RankUp -= OnRankUp; }
        static void OnRankUp(int rank, string name) => DMI.Xenoarch.UI.Toast.Show($"Notoriety rank: {name}", 6f);
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Bind(s); }

        void Bind(Scene scene)
        {
            if (XenoarchRuntime.Session == null || !scene.IsValid()) return;
            int n = 0;
            foreach (var v in VendorDefinition.All())
            {
                if (v == null || v.sceneName != scene.name || string.IsNullOrEmpty(v.anchorName)) continue;
                var anchor = Find(scene, v.anchorName);
                if (anchor == null) { Debug.LogWarning($"[Xenoarch] Vendor {v.Id}: anchor '{v.anchorName}' not found in {scene.name}"); continue; }
                foreach (var p in anchor.GetComponents<DMI.Xenoarch.Interaction.StationPlaceholderInteractable>()) Destroy(p);
                var t = anchor.GetComponent<VendorTerminal>() ?? anchor.gameObject.AddComponent<VendorTerminal>();
                t.vendor = v; t.terminalName = v.displayName;
                if (anchor.GetComponentInChildren<Collider>() == null) { var bc = anchor.gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(1.2f, 2f, 1.2f); bc.center = Vector3.up; }
                n++;
            }
            if (n > 0) Debug.Log($"[Xenoarch] Vendors: {n} open in {scene.name}");
        }

        static Transform Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root.transform;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            }
            return null;
        }
    }
}
