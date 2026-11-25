using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Scenes
{
    /// <summary>Lives in the Bootstrap scene (build index 0). The runtime already exists; this just hands over to the main menu.</summary>
    public sealed class BootstrapEntry : MonoBehaviour
    {
        void Start()
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null) { Debug.LogError("[Xenoarch] Runtime missing in Bootstrap"); return; }
            rt.ReturnToMainMenu();
        }
    }
}
