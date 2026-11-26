using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Soft alpha ring projected onto the ground (URP Decal Projector) where the equipped dig tool will cut. Sized to the
    /// stroke radius, aligned to the surface normal. Gold when aimed at diggable ground in reach, red when aimed at a dig
    /// site out of reach or at a refused layer, dim white when not on a dig site. Needs the Decal renderer feature (the 1C
    /// setup adds it to the URP renderers); materials come from Resources/FX/DigRing_*.mat (made by setup) or are built at
    /// runtime as a fallback.
    /// </summary>
    public sealed class DigAimRing : MonoBehaviour
    {
        public enum State { Hidden, Valid, Invalid, Neutral }

        DecalProjector _proj;
        Material _valid, _invalid, _neutral;
        State _state = State.Hidden;

        public static DigAimRing Create(Transform parent)
        {
            var go = new GameObject("Dig Aim Ring");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<DigAimRing>();
            r.Init();
            return r;
        }

        void Init()
        {
            _proj = gameObject.AddComponent<DecalProjector>();
            _proj.pivot = Vector3.zero;
            _proj.size = new Vector3(0.7f, 0.7f, 0.8f);
            _proj.fadeFactor = 1f;
            _proj.drawDistance = 30f;
            _valid = Load("FX/DigRing_Valid", new Color(1f, 0.78f, 0.3f, 0.85f));
            _invalid = Load("FX/DigRing_Invalid", new Color(1f, 0.25f, 0.2f, 0.8f));
            _neutral = Load("FX/DigRing_Neutral", new Color(1f, 1f, 1f, 0.35f));
            _proj.enabled = false;
        }

        static Material Load(string res, Color tint)
        {
            var m = Resources.Load<Material>(res);
            if (m != null) { DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(m); return m; }
            var sh = Shader.Find("Shader Graphs/Decal");
            if (sh == null) return null;
            m = new Material(sh) { name = res + " (runtime)" };
            m.SetTexture("Base_Map", RingTexture(tint));
            m.enableInstancing = true;
            DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(m);
            return m;
        }

        /// <summary>Soft ring: transparent centre, bright band near the edge, alpha falloff both sides.</summary>
        public static Texture2D RingTexture(Color tint, int size = 128)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "DigRing", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.08f, 2f));          // ring
                    float fill = d < 0.82f ? 0.12f * (d / 0.82f) : 0f;                       // faint inner wash
                    float a = Mathf.Clamp01(band + fill) * tint.a;
                    px[y * size + x] = new Color(tint.r, tint.g, tint.b, a);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        public void Show(Vector3 point, Vector3 normal, float radius, State state)
        {
            if (_proj == null) return;
            if (state == State.Hidden) { Hide(); return; }
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up));
            float d = Mathf.Max(0.1f, radius) * 2f;
            _proj.size = new Vector3(d, d, Mathf.Max(0.6f, d));
            if (state != _state)
            {
                var m = state == State.Valid ? _valid : state == State.Invalid ? _invalid : _neutral;
                if (m != null) _proj.material = m;
                _state = state;
            }
            _proj.enabled = _proj.material != null;
        }

        public void Hide()
        {
            if (_proj != null) _proj.enabled = false;
            _state = State.Hidden;
        }
    }
}
