using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.Weather
{
    /// <summary>Procedural particle sprites and URP particle materials for weather (nothing imported, nothing licensed).</summary>
    public static class WeatherVisuals
    {
        static Texture2D _dot, _streak;
        static Material _alpha, _additive, _streakMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _dot = _streak = null; _alpha = _additive = _streakMat = null; }

        /// <summary>Soft round flake / grain sprite (64x64, white with a smooth alpha falloff).</summary>
        public static Texture2D SoftDot()
        {
            if (_dot != null) return _dot;
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, false) { name = "XA_Weather_SoftDot", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px); t.Apply(false, false);
            return _dot = t;
        }

        /// <summary>Thin vertical rain streak sprite (16x64): bright core, soft edges, fading ends.</summary>
        public static Texture2D Streak()
        {
            if (_streak != null) return _streak;
            const int W = 16, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { name = "XA_Weather_Streak", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / W * 2f - 1f);
                    float v = (y + 0.5f) / H;
                    float across = Mathf.Clamp01(1f - dx); across *= across;
                    float along = Mathf.Sin(v * Mathf.PI);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(across * along * 255f));
                }
            t.SetPixels32(px); t.Apply(false, false);
            return _streak = t;
        }

        /// <summary>Unlit transparent particle material (alpha blend or additive) with the given sprite.</summary>
        public static Material MakeMaterial(string name, Texture2D tex, bool additive)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name };
            m.hideFlags = HideFlags.None;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_SoftParticlesEnabled", 0f);
            m.SetFloat("_CameraFadingEnabled", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_ALPHAADDITIVE_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public static Material Alpha(WeatherCatalog c)
        {
            if (c != null && c.alphaMaterial != null) return c.alphaMaterial;
            return _alpha != null ? _alpha : (_alpha = Runtime("XA_Weather_Alpha", SoftDot(), false));
        }
        public static Material Additive(WeatherCatalog c)
        {
            if (c != null && c.additiveMaterial != null) return c.additiveMaterial;
            return _additive != null ? _additive : (_additive = Runtime("XA_Weather_Additive", SoftDot(), true));
        }
        public static Material StreakMat(WeatherCatalog c)
        {
            if (c != null && c.streakMaterial != null) return c.streakMaterial;
            return _streakMat != null ? _streakMat : (_streakMat = Runtime("XA_Weather_Streak", Streak(), false));
        }
        static Material Runtime(string n, Texture2D t, bool add) { var m = MakeMaterial(n, t, add); m.hideFlags = HideFlags.DontSave; return m; }
    }
}
