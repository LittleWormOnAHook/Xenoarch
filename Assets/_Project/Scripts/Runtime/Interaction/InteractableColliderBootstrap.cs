using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>
    /// t172u: device scripts (terminals, stations, travel consoles...) can be moved onto any object in a scene. The
    /// <see cref="Interactor"/> finds them by raycast + GetComponentInParent&lt;IInteractable&gt;, so the new object only needs
    /// a collider somewhere in its own hierarchy. On every scene load this checks each active interactable and, when none
    /// of its colliders belong to it, adds a trigger BoxCollider at runtime fitted to its renderers (trigger: it never
    /// changes how the player walks). Nothing is saved into the scene. The Console lists what was added.
    /// </summary>
    public sealed class InteractableColliderBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<InteractableColliderBootstrap>() != null) return;
            var go = new GameObject("[Xenoarch] Interactable colliders");
            DontDestroyOnLoad(go);
            go.AddComponent<InteractableColliderBootstrap>();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => StartCoroutine(Later());
        void OnLoaded(Scene s, LoadSceneMode m) => StartCoroutine(Later());

        IEnumerator Later()
        {
            yield return null; // let scene directors add their own devices first
            FixAll();
        }

        /// <summary>Give every active interactable without its own collider a fitted trigger box. Returns how many.</summary>
        public static int FixAll()
        {
            int added = 0;
            var names = new List<string>();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (!(mb is IInteractable usable) || !mb.isActiveAndEnabled) continue;
                if (mb.gameObject.scene.buildIndex < 0 && mb.gameObject.scene.name == "DontDestroyOnLoad") continue; // runtime UI (e.g. InventoryWindow), not a placed device
                if ((mb.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;                                      // runtime-only helpers (e.g. the helm's built-in orbital scanner)
                if (HasOwnCollider(mb, usable)) continue;
                if (Fit(mb, usable)) { added++; names.Add($"{mb.GetType().Name} on '{mb.name}'"); }
            }
            if (added > 0) Debug.Log($"[Xenoarch] Interaction: added a trigger collider to {added} device(s) placed without one: {string.Join(", ", names)}");
            return added;
        }

        static bool HasOwnCollider(MonoBehaviour mb, IInteractable usable)
        {
            foreach (var c in mb.GetComponentsInChildren<Collider>())
                if (c.enabled && ReferenceEquals(c.GetComponentInParent<IInteractable>(), usable)) return true;
            return false;
        }

        static bool Fit(MonoBehaviour mb, IInteractable usable)
        {
            var t = mb.transform;
            bool any = false;
            var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
            foreach (var r in mb.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!ReferenceEquals(r.GetComponentInParent<IInteractable>(), usable)) continue; // another device inside it
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var lp = t.InverseTransformPoint(corner);
                    min = Vector3.Min(min, lp); max = Vector3.Max(max, lp);
                }
                any = true;
            }
            var box = mb.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (any) { box.center = (min + max) * 0.5f; box.size = Vector3.Max(max - min, Vector3.one * 0.05f); }
            else
            {
                var s = t.lossyScale;
                box.center = Vector3.zero;
                box.size = new Vector3(0.6f / Mathf.Max(1e-3f, Mathf.Abs(s.x)), 0.6f / Mathf.Max(1e-3f, Mathf.Abs(s.y)), 0.6f / Mathf.Max(1e-3f, Mathf.Abs(s.z)));
            }
            return true;
        }
    }
}