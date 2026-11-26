using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Tools
{
    /// <summary>
    /// Text placed in the scene, drawn by UI Toolkit in world space (Unity 6 world-space UIDocument; t171u).
    /// Add it with GameObject > Xenoarch > Scene Text (or Add Component > Xenoarch > Scene Text) and edit it in the
    /// Inspector; the Scene view updates live in edit mode. Move / rotate with this object's transform; size the text
    /// with <see cref="fontSize"/> (UI pixels) and <see cref="worldScale"/> (metres per UI pixel).
    /// The UIDocument lives on a hidden child that is rebuilt on enable and never saved, so the scene only stores
    /// this component's settings. From code: <see cref="SetText"/>, <see cref="SetColor"/>.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("Xenoarch/Scene Text")]
    public sealed class XenoarchSceneText : MonoBehaviour
    {
        [TextArea(2, 12)] public string text = "New text";

        [Header("Font (empty = the project's UI Toolkit theme font)")]
        [Tooltip("SDF font asset (UI Toolkit / TextCore). Used first when set.")] public FontAsset fontAsset;
        [Tooltip("Regular font (.ttf / .otf). Used when no font asset is set.")] public Font font;
        [Min(1f)] public float fontSize = 48f;
        public FontStyle fontStyle = FontStyle.Normal;
        public Color color = Color.white;
        [Tooltip("Extra space between letters (UI pixels).")] public float letterSpacing;

        [Header("Outline / shadow")]
        [Tooltip("Text outline width (0 = none).")] [Range(0f, 1f)] public float outlineWidth;
        public Color outlineColor = Color.black;
        public bool shadow;
        public Vector2 shadowOffset = new Vector2(2f, 2f);
        [Min(0f)] public float shadowBlur = 2f;
        public Color shadowColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("Layout")]
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        [Tooltip("Wrap lines at this width (UI pixels). 0 = no wrapping.")] [Min(0f)] public float wrapWidth;
        [Tooltip("Which point of the text sits on this object's position.")] public Pivot pivot = Pivot.Center;

        [Header("Background panel")]
        public bool background;
        public Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("Padding (UI pixels): left, top, right, bottom.")] public Vector4 padding = new Vector4(16f, 10f, 16f, 10f);
        [Min(0f)] public float cornerRadius = 6f;

        [Header("Placement")]
        [Tooltip("Metres per UI pixel (0.01 = 100 px per metre, so 48 px text is about 0.5 m tall).")] [Min(0.0001f)] public float worldScale = 0.01f;
        [Tooltip("Turn to face the camera (Scene view camera in edit mode, Camera.main in play).")] public bool billboard;
        [Tooltip("Billboard turns around the vertical axis only (stays upright).")] public bool billboardYawOnly = true;
        [Tooltip("Order against other world-space UI Toolkit documents.")] public float sortingOrder;

        const string ChildName = "[Scene Text UITK]";
        static PanelSettings _panel;
        static readonly List<XenoarchSceneText> _all = new List<XenoarchSceneText>();
        /// <summary>Editor hook: the camera billboards face in edit mode (set by the editor assembly).</summary>
        public static Func<Camera> EditorCamera;
        public static IReadOnlyList<XenoarchSceneText> All => _all;

        GameObject _child;
        UIDocument _doc;
        VisualElement _box;
        Label _label;
        bool _dirty = true;

        /// <summary>The shared world-space panel (in memory, never saved), themed like the project's UI.</summary>
        public static PanelSettings Panel
        {
            get
            {
                if (_panel != null) return _panel;
                _panel = ScriptableObject.CreateInstance<PanelSettings>();
                _panel.name = "Xenoarch Scene Text (world space)";
                _panel.hideFlags = HideFlags.HideAndDontSave;
                _panel.renderMode = PanelRenderMode.WorldSpace;
                var project = Resources.Load<PanelSettings>("UI/XenoarchPanelSettings");
                if (project != null) _panel.themeStyleSheet = project.themeStyleSheet;
                return _panel;
            }
        }

        public void SetText(string value) { text = value ?? ""; _dirty = true; Refresh(); }
        public void SetColor(Color value) { color = value; _dirty = true; Refresh(); }

        /// <summary>Apply Inspector changes now (also called automatically).</summary>
        public void Refresh() { if (isActiveAndEnabled) { EnsureDocument(); Apply(); } }

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
            _dirty = true;
            EnsureDocument();
            Apply();
        }

        void OnDisable()
        {
            _all.Remove(this);
            if (_child != null)
            {
                if (Application.isPlaying) Destroy(_child); else DestroyImmediate(_child);
            }
            _child = null; _doc = null; _box = null; _label = null;
        }

        void OnValidate() => _dirty = true;

        void Update()
        {
            if (_doc == null || _child == null) { EnsureDocument(); _dirty = true; }
            if (_box == null || _box.panel == null) _dirty = true;   // the document rebuilt its root
            if (_dirty) Apply();
        }

        void LateUpdate()
        {
            if (billboard && Application.isPlaying) FaceCamera(Camera.main);
        }

        void EnsureDocument()
        {
            if (_child != null && _doc != null) return;
            // Leftovers from an older session (not saved, but be safe).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name == ChildName && c.gameObject != _child) { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
            }
            _child = new GameObject(ChildName) { hideFlags = HideFlags.HideAndDontSave };
            _child.SetActive(false);
            _child.layer = gameObject.layer;
            _child.transform.SetParent(transform, false);
            _doc = _child.AddComponent<UIDocument>();
            _doc.panelSettings = Panel;
            _doc.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Dynamic;
            _child.SetActive(true);
            _box = null; _label = null;
        }

        void Apply()
        {
            if (_doc == null) return;
            var root = _doc.rootVisualElement;
            if (root == null) return;
            _dirty = false;
            if (_box == null || _box.parent != root)
            {
                root.Clear();
                root.pickingMode = PickingMode.Ignore;
                _box = new VisualElement { name = "scene-text-box", pickingMode = PickingMode.Ignore };
                _label = new Label { name = "scene-text", pickingMode = PickingMode.Ignore };
                _label.enableRichText = true;
                _box.Add(_label);
                root.Add(_box);
            }
            _doc.pivot = pivot;
            _doc.sortingOrder = sortingOrder;
            _child.transform.localPosition = Vector3.zero;
            if (!billboard) _child.transform.localRotation = Quaternion.identity;
            float s = Mathf.Max(0.0001f, worldScale);
            _child.transform.localScale = Vector3.one * s;   // the object's own scale multiplies on top

            _label.text = text ?? "";
            var st = _label.style;
            if (fontAsset != null) st.unityFontDefinition = FontDefinition.FromSDFFont(fontAsset);
            else if (font != null) st.unityFontDefinition = FontDefinition.FromFont(font);
            else st.unityFontDefinition = StyleKeyword.Null;
            st.fontSize = fontSize;
            st.unityFontStyleAndWeight = fontStyle;
            st.color = color;
            st.letterSpacing = letterSpacing;
            st.unityTextAlign = alignment;
            st.unityTextOutlineWidth = outlineWidth;
            st.unityTextOutlineColor = outlineColor;
            st.textShadow = shadow ? new TextShadow { offset = shadowOffset, blurRadius = shadowBlur, color = shadowColor } : new StyleTextShadow(StyleKeyword.Null);
            st.marginLeft = st.marginRight = st.marginTop = st.marginBottom = 0f;
            st.paddingLeft = st.paddingRight = st.paddingTop = st.paddingBottom = 0f;
            if (wrapWidth > 0f) { st.whiteSpace = WhiteSpace.Normal; st.width = wrapWidth; }
            else { st.whiteSpace = WhiteSpace.NoWrap; st.width = StyleKeyword.Auto; }

            var b = _box.style;
            b.position = Position.Relative; b.flexGrow = 0f; b.flexShrink = 0f;
            root.style.alignItems = Align.FlexStart;
            b.backgroundColor = background ? backgroundColor : Color.clear;
            b.paddingLeft = background ? padding.x : 0f; b.paddingTop = background ? padding.y : 0f;
            b.paddingRight = background ? padding.z : 0f; b.paddingBottom = background ? padding.w : 0f;
            b.borderTopLeftRadius = b.borderTopRightRadius = b.borderBottomLeftRadius = b.borderBottomRightRadius = background ? cornerRadius : 0f;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        /// <summary>Turn the text to face <paramref name="cam"/> (used by the billboard option).</summary>
        public void FaceCamera(Camera cam)
        {
            if (cam == null || _child == null) return;
            var dir = _child.transform.position - cam.transform.position;
            if (billboardYawOnly) dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            _child.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>Approximate size of the text in metres (for gizmos).</summary>
        public Vector2 WorldSize
        {
            get
            {
                if (_box == null || float.IsNaN(_box.layout.width)) return new Vector2(1f, 0.3f);
                var ls = _child != null ? _child.transform.lossyScale : Vector3.one * worldScale;
                return new Vector2(Mathf.Max(0.05f, _box.layout.width * Mathf.Abs(ls.x)), Mathf.Max(0.05f, _box.layout.height * Mathf.Abs(ls.y)));
            }
        }

        public Transform TextTransform => _child != null ? _child.transform : transform;

        void OnDrawGizmos()
        {
            // Pickable outline so the text can be clicked in the Scene view (the UI itself is not pickable there).
            var size = WorldSize;
            var t = TextTransform;
            Vector2 off = PivotOffset(pivot, size);
            Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            Gizmos.color = new Color(0.41f, 0.87f, 0.84f, 0.02f);
            Gizmos.DrawCube(new Vector3(off.x, off.y, 0f), new Vector3(size.x, size.y, 0.001f));
            Gizmos.matrix = Matrix4x4.identity;
        }

        void OnDrawGizmosSelected()
        {
            var size = WorldSize;
            var t = TextTransform;
            Vector2 off = PivotOffset(pivot, size);
            Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            Gizmos.color = new Color(0.41f, 0.87f, 0.84f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(off.x, off.y, 0f), new Vector3(size.x, size.y, 0f));
            Gizmos.matrix = Matrix4x4.identity;
        }

        /// <summary>Centre of the text box relative to the pivot point (x right, y up), in metres.</summary>
        static Vector2 PivotOffset(Pivot p, Vector2 s)
        {
            float hx = s.x * 0.5f, hy = s.y * 0.5f;
            switch (p)
            {
                case Pivot.TopLeft: return new Vector2(hx, -hy);
                case Pivot.TopCenter: return new Vector2(0f, -hy);
                case Pivot.TopRight: return new Vector2(-hx, -hy);
                case Pivot.LeftCenter: return new Vector2(hx, 0f);
                case Pivot.RightCenter: return new Vector2(-hx, 0f);
                case Pivot.BottomLeft: return new Vector2(hx, hy);
                case Pivot.BottomCenter: return new Vector2(0f, hy);
                case Pivot.BottomRight: return new Vector2(-hx, hy);
                default: return Vector2.zero;
            }
        }
    }
}