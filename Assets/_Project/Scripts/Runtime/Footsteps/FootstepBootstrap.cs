using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Footsteps
{
    /// <summary>
    /// Attaches <see cref="PlayerFootsteps"/> to the current Kade rig at runtime so the player
    /// prefab does not have to be edited. Mirrors the PlayerToolsBootstrap pattern.
    /// </summary>
    public sealed class FootstepBootstrap : MonoBehaviour
    {
        Transform _done;
        float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<FootstepBootstrap>() != null) return;
            var go = new GameObject("[Xenoarch] Footsteps");
            DontDestroyOnLoad(go);
            go.AddComponent<FootstepBootstrap>();
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (_done != null && _done.gameObject.activeInHierarchy)
            {
                if (_done.GetComponent<PlayerFootsteps>() != null) return;
            }
            var rig = PlayerRigLocator.Find();
            if (rig == null || rig.Root == null) return;
            var root = rig.Root;
            if (root.GetComponent<PlayerFootsteps>() == null)
            {
                var fs = root.gameObject.AddComponent<PlayerFootsteps>();
                fs.map = FootstepSurfaceMap.Live;
            }
            _done = root;
        }
    }
}
