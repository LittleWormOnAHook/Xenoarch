using DMI.Xenoarch.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Short top-centre notice (recoveries, research results, journal updates). Survives scene loads.
    /// t246u: Morpheus card (icon, kind heading, text, teal edge); the previous notice stays dimmed under the newest.</summary>
    public sealed class Toast : MonoBehaviour
    {
        static Toast _inst;
        VisualElement _stack, _cur, _prev;
        float _hideAt, _prevHideAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _inst = null;

        public static void Show(string text, float seconds = 5f) => Show(text, seconds, null, null);

        /// <summary>Card with a kind heading (e.g. "New discovery") and a glyph name (XenoarchIcons, e.g. "ui-new-discovery").</summary>
        public static void Show(string text, float seconds, string kind, string glyph)
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
            if (_inst._stack == null) return;
            _inst.Push(text, seconds, kind, glyph ?? "ui-info");
        }

        void Build()
        {
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 60;
            var root = doc.rootVisualElement; XenoarchUI.FillRoot(root);
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            XenoarchIcons.EnsureStyles(root);
            _stack = new VisualElement { pickingMode = PickingMode.Ignore }; _stack.AddToClassList("xa-m-toast-stack");
            root.Add(_stack);
        }

        void Push(string text, float seconds, string kind, string glyph)
        {
            // the current card becomes the dimmed previous one
            _prev?.RemoveFromHierarchy();
            _prev = _cur; _prevHideAt = Mathf.Min(_hideAt, Time.unscaledTime + 2.5f);
            if (_prev != null) _prev.AddToClassList("xa-m-toast--old");

            var card = new VisualElement { pickingMode = PickingMode.Ignore }; card.AddToClassList("xa-m-toast");
            card.Add(XenoarchIcons.Glyph(glyph, "xa-m-toast__icon"));
            var col = new VisualElement { pickingMode = PickingMode.Ignore }; col.style.flexGrow = 1; col.style.flexShrink = 1;
            if (!string.IsNullOrEmpty(kind)) MUI.L(col, kind.ToUpperInvariant(), "xa-m-toast__kind");
            MUI.L(col, text, "xa-m-toast__text");
            card.Add(col);
            // t362u: toast text/icons used to swallow clicks and hover on whatever sits under the banner (e.g. nav terminal buttons)
            foreach (var ve in card.Query<VisualElement>().ToList()) ve.pickingMode = PickingMode.Ignore;
            _stack.Insert(0, card);
            _cur = card; _hideAt = Time.unscaledTime + seconds;
            if (!string.IsNullOrEmpty(kind)) UISound.Play(UISoundEvent.Notification);
        }

        void Update()
        {
            if (_cur != null && _hideAt > 0f && Time.unscaledTime >= _hideAt) { _cur.RemoveFromHierarchy(); _cur = null; _hideAt = 0f; }
            if (_prev != null && Time.unscaledTime >= _prevHideAt) { _prev.RemoveFromHierarchy(); _prev = null; }
        }
    }
}
