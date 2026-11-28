using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// UI Toolkit helpers shared by the placeholder screens. All UXML/USS/PanelSettings live in Resources/UI so screens
    /// can be created from code on any GameObject (scenes only carry the screen component).
    /// </summary>
    public static class XenoarchUI
    {
        public const string PanelSettingsResource = "UI/XenoarchPanelSettings";
        static PanelSettings _fallback;

        public static PanelSettings LoadPanelSettings()
        {
            var ps = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (ps != null) return ps;
            if (_fallback == null)
            {
                Debug.LogWarning($"[Xenoarch] UI: Resources/{PanelSettingsResource} missing (run Xenoarch/Setup); using a runtime PanelSettings");
                _fallback = ScriptableObject.CreateInstance<PanelSettings>();
                _fallback.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _fallback.referenceResolution = new Vector2Int(1920, 1080);
                _fallback.match = 0.5f;
            }
            return _fallback;
        }

        /// <summary>Adds (or reuses) a UIDocument on <paramref name="host"/> showing Resources/<paramref name="uxmlResource"/>.</summary>
        public static UIDocument CreateDocument(GameObject host, string uxmlResource, float sortingOrder)
        {
            var tree = Resources.Load<VisualTreeAsset>(uxmlResource);
            if (tree == null) { Debug.LogError($"[Xenoarch] UI: UXML Resources/{uxmlResource} not found"); return null; }
            if (!host.TryGetComponent(out UIDocument doc)) doc = host.AddComponent<UIDocument>();
            doc.panelSettings = LoadPanelSettings();
            doc.sortingOrder = sortingOrder;
            doc.visualTreeAsset = tree;
            var root = doc.rootVisualElement;
            if (root != null)
            {
                FillRoot(root);
                root.pickingMode = PickingMode.Ignore;
            }
            return doc;
        }

        /// <summary>Make a UIDocument root fill the whole panel. Without this a code-built document root (no UXML) can
        /// end up 0 px tall, so anything centred inside it sits at the top edge, half off-screen.</summary>
        public static void FillRoot(VisualElement root)
        {
            if (root == null) return;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            DMI.Xenoarch.Audio.UISoundBind.Attach(root);
            UIClickKit.Attach(root);
        }

        public static void Show(VisualElement e, bool visible)
        {
            if (e != null) e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
