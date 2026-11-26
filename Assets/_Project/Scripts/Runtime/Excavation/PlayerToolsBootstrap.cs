using DMI.Xenoarch.Core;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// ZEN-21: hotbar keys 2-8 are handled by <see cref="DigTool"/> and <see cref="DepthProbeTool"/>, which used to be
    /// added only on planets with dig sites or caches (ExcavationDirector / ValuableSiteDirector), so on the ship, the hub
    /// or an empty site the keys did nothing. This keeps both on the player in every scene with a game session (checked
    /// once a second until the current rig has them). Away from a site the tools equip and say there is nothing to dig.
    /// </summary>
    public sealed class PlayerToolsBootstrap : MonoBehaviour
    {
        Transform _done;
        float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PlayerToolsBootstrap>() != null) return;
            var go = new GameObject("[Xenoarch] Player tools");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerToolsBootstrap>();
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (_done != null && _done.gameObject.activeInHierarchy) return;
            if (XenoarchRuntime.Session == null) return;
            var rig = PlayerRigLocator.Find();
            if (rig == null || rig.Root == null) return;
            var root = rig.Root;
            if (root.GetComponent<DigTool>() == null) root.gameObject.AddComponent<DigTool>();
            DepthProbeTool.Ensure(root);
            if (root.GetComponent<HeldToolVisuals>() == null) root.gameObject.AddComponent<HeldToolVisuals>();
            _done = root;
        }
    }
}