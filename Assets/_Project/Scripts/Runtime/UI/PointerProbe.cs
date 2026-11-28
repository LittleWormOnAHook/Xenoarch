using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Log-only diagnostics for "clicks land in the wrong place" reports (Editor Game view Scale / Play Maximized,
    /// resolutions, display scaling). On every left-click while a menu is open it writes one "[PointerProbe]" line to
    /// Editor.log / Player.log: the Input System pointer, Screen / Display / DPI numbers, and for each UI document the
    /// panel position Unity maps that pointer to and the element it would pick there (both y conventions).
    /// It never changes behaviour. Editor and development builds only.
    /// </summary>
    public sealed class PointerProbe : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<PointerProbe>() != null) return;
            var go = new GameObject("[PointerProbe]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<PointerProbe>();
        }

        void Update()
        {
            var m = Mouse.current;
            if (m == null || !m.leftButton.wasPressedThisFrame || !UIState.BlocksGameplay) return;
            Vector2 p = m.position.ReadValue();
            var sb = new StringBuilder(512);
            sb.Append("[PointerProbe] mouse=").Append(p.ToString("0.#"))
              .Append(" screen=").Append(Screen.width).Append('x').Append(Screen.height)
              .Append(" display=").Append(Display.main.renderingWidth).Append('x').Append(Display.main.renderingHeight)
              .Append(" dpi=").Append(Screen.dpi.ToString("0"))
              .Append(" mode=").Append(Screen.fullScreenMode)
              .Append(" editor=").Append(Application.isEditor);
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var root = doc.rootVisualElement;
                if (root == null || root.panel == null || root.resolvedStyle.display == DisplayStyle.None) continue;
                var ps = doc.panelSettings;
                Vector2 flip = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(p.x, Screen.height - p.y));
                Vector2 raw = RuntimePanelUtils.ScreenToPanel(root.panel, p);
                var hitFlip = root.panel.Pick(flip);
                var hitRaw = root.panel.Pick(raw);
                if (hitFlip == null && hitRaw == null) continue;
                float ratio = root.worldBound.width > 1f ? Screen.width / root.worldBound.width : 0f;
                sb.Append("\n  doc=").Append(doc.name).Append(" order=").Append(doc.sortingOrder)
                  .Append(" panel=").Append(root.worldBound.width.ToString("0")).Append('x').Append(root.worldBound.height.ToString("0"))
                  .Append(" scale=").Append(ratio.ToString("0.###"))
                  .Append(" mode=").Append(ps != null ? ps.scaleMode.ToString() : "?")
                  .Append("\n    flipY->").Append(flip.ToString("0.#")).Append(' ').Append(Describe(hitFlip))
                  .Append("\n    rawY ->").Append(raw.ToString("0.#")).Append(' ').Append(Describe(hitRaw));
            }
            Debug.Log(sb.ToString());
        }

        static string Describe(VisualElement e)
        {
            if (e == null) return "(none)";
            var wb = e.worldBound;
            string cls = e.GetClasses() != null ? string.Join(".", new System.Collections.Generic.List<string>(e.GetClasses())) : "";
            return $"{e.GetType().Name} '{e.name}' .{cls} bounds=({wb.x:0},{wb.y:0},{wb.width:0},{wb.height:0})";
        }
#endif
    }
}