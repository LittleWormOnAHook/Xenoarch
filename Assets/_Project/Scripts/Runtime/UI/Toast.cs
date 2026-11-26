using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Short top-centre notice (recoveries, research results, journal updates). Survives scene loads.</summary>
    public sealed class Toast : MonoBehaviour
    {
        static Toast _inst;
        Label _label;
        float _hideAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _inst = null;

        public static void Show(string text, float seconds = 5f)
        {
            Debug.Log($"[Xenoarch] Toast: {text}");
            if (!Application.isPlaying) return;
            if (_inst == null)
            {
                var go = new GameObject("[Xenoarch Toast]");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<Toast>();
                _inst.Build();
            }
            if (_inst._label == null) return;
            _inst._label.text = text;
            XenoarchUI.Show(_inst._label, true);
            _inst._hideAt = Time.unscaledTime + seconds;
        }

        void Build()
        {
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 60;
            var root = doc.rootVisualElement; XenoarchUI.FillRoot(root);
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute; root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            root.style.alignItems = Align.Center;
            _label = new Label { pickingMode = PickingMode.Ignore };
            var s = _label.style;
            s.marginTop = 70; s.paddingLeft = 18; s.paddingRight = 18; s.paddingTop = 8; s.paddingBottom = 8;
            s.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDark, 0.9f);
            s.color = XenoarchPalette.Text;
            s.fontSize = 20; s.maxWidth = 900; s.whiteSpace = WhiteSpace.Normal;
            s.borderTopLeftRadius = 6; s.borderTopRightRadius = 6; s.borderBottomLeftRadius = 6; s.borderBottomRightRadius = 6;
            root.Add(_label);
            XenoarchUI.Show(_label, false);
        }

        void Update()
        {
            if (_label != null && _hideAt > 0f && Time.unscaledTime >= _hideAt) { XenoarchUI.Show(_label, false); _hideAt = 0f; }
        }
    }
}
