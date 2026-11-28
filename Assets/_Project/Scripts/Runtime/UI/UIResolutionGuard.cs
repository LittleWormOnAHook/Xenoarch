using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// t380u: every Xenoarch panel uses XenoarchPanelSettings (1920x1080 reference, Shrink: the panel is never smaller
    /// than 1920x1080, whatever the aspect). This watches the window/Game view size, dpi and fullscreen mode and, when
    /// any of them change (and a few frames after start, when the Editor Game view may still be settling), re-applies
    /// the panel settings and repaints every open document so no panel keeps a stale size.
    /// </summary>
    public sealed class UIResolutionGuard : MonoBehaviour
    {
        /// <summary>Screen pixels per panel pixel under the Shrink scale mode.</summary>
        public static float PanelScale => Mathf.Max(0.01f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));

        int _w, _h, _fs, _frames; float _dpi;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<UIResolutionGuard>() != null) return;
            var go = new GameObject("[UIResolutionGuard]");
            DontDestroyOnLoad(go);
            go.AddComponent<UIResolutionGuard>();
        }

        void Update()
        {
            _frames++;
            bool changed = Screen.width != _w || Screen.height != _h || !Mathf.Approximately(Screen.dpi, _dpi) || (int)Screen.fullScreenMode != _fs;
            if (!changed && _frames != 3 && _frames != 30 && _frames != 150) return;
            _w = Screen.width; _h = Screen.height; _dpi = Screen.dpi; _fs = (int)Screen.fullScreenMode;
            Refresh(changed);
        }

        static void Refresh(bool changed)
        {
            var ps = XenoarchUI.LoadPanelSettings();
            if (ps != null) ps.scale = ps.scale; // setter re-applies the panel settings (scale/ref resolution) to every panel
            int n = 0;
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                var r = d.rootVisualElement;
                if (r == null) continue;
                r.MarkDirtyRepaint(); n++;
            }
            if (changed) Debug.Log($"[UIResolutionGuard] screen {Screen.width}x{Screen.height} dpi {Screen.dpi:0} aspect {(float)Screen.width / Mathf.Max(1, Screen.height):0.00}, panel scale {PanelScale:0.000}, refreshed {n} documents");
        }
    }
}
