using DMI.Xenoarch.Weather;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>Procedural meshes, sprites and materials for the sonic tool FX (nothing imported, nothing licensed).</summary>
    public static class SonicFxAssets
    {
        static Mesh _sphere, _cube, _cone;
        static Texture2D _beamTex;
        static Material _beamMat, _headMat, _chunkMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _sphere = _cube = _cone = null; _beamTex = null; _beamMat = _headMat = _chunkMat = null; }

        static Mesh Prim(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        public static Mesh Sphere => _sphere != null ? _sphere : (_sphere = Prim(PrimitiveType.Sphere));
        public static Mesh Cube => _cube != null ? _cube : (_cube = Prim(PrimitiveType.Cube));

        public static Mesh HeadMesh(SonicHead h) => h == SonicHead.Cube ? Cube : h == SonicHead.Cone ? Cone : Sphere;

        /// <summary>Unit cone, apex at y = -0.5, base (radius 0.5) at y = +0.5, so it points down into the soil.</summary>
        public static Mesh Cone
        {
            get
            {
                if (_cone != null) return _cone;
                const int N = 24;
                var v = new Vector3[N + 2 + N + 1];
                var tri = new int[N * 3 + N * 3];
                int apex = 0;
                v[apex] = new Vector3(0f, -0.5f, 0f);
                for (int i = 0; i < N; i++)
                {
                    float a = i / (float)N * Mathf.PI * 2f;
                    v[1 + i] = new Vector3(Mathf.Cos(a) * 0.5f, 0.5f, Mathf.Sin(a) * 0.5f);
                    v[N + 2 + i] = v[1 + i];
                }
                int cap = N + 1;
                v[cap] = new Vector3(0f, 0.5f, 0f);
                int t = 0;
                for (int i = 0; i < N; i++)
                {
                    int a = 1 + i, b = 1 + (i + 1) % N;
                    tri[t++] = apex; tri[t++] = b; tri[t++] = a;          // side
                }
                for (int i = 0; i < N; i++)
                {
                    int a = N + 2 + i, b = N + 2 + (i + 1) % N;
                    tri[t++] = cap; tri[t++] = a; tri[t++] = b;           // base (the cap centre doubles as the shared fan hub)
                }
                // cap hub vertex index N+1 is reused by the base fan; fix the base fan to use it
                for (int i = 0; i < N; i++) tri[N * 3 + i * 3] = cap;
                var m = new Mesh { name = "XA_SonicCone", hideFlags = HideFlags.HideAndDontSave, vertices = v, triangles = tri };
                m.RecalculateNormals(); m.RecalculateBounds();
                return _cone = m;
            }
        }

        /// <summary>Soft-edged strip sprite for beams and fan lines (bright core, fading across the width).</summary>
        public static Texture2D BeamTexture
        {
            get
            {
                if (_beamTex != null) return _beamTex;
                const int W = 4, H = 32;
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { name = "XA_Sonic_Beam", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                {
                    float v = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - v), 1.4f);
                    for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                t.SetPixels32(px); t.Apply(false, false);
                return _beamTex = t;
            }
        }

        /// <summary>Additive material for the beam, fan lines and head wire frame.</summary>
        public static Material BeamMaterial => _beamMat != null ? _beamMat : (_beamMat = Warm(WeatherVisuals.MakeMaterial("XA_Sonic_Beam", BeamTexture, true)));

        /// <summary>Alpha-blended material for the semi-transparent chisel head volume (colour per frame).</summary>
        public static Material HeadMaterial => _headMat != null ? _headMat : (_headMat = Warm(WeatherVisuals.MakeMaterial("XA_Sonic_Head", Texture2D.whiteTexture, false)));

        /// <summary>Lit material for the rock chunks (colour per chunk via a property block).</summary>
        public static Material ChunkMaterial
        {
            get
            {
                if (_chunkMat != null) return _chunkMat;
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _chunkMat = new Material(sh) { name = "XA_Sonic_Chunk" };
                if (_chunkMat.HasProperty("_Smoothness")) _chunkMat.SetFloat("_Smoothness", 0.15f);
                return Warm(_chunkMat);
            }
        }

        static Material Warm(Material m) { DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(m); return m; }
    }
}
