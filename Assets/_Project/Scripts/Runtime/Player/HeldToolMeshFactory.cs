using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// Builds the six first-person held-tool models Anthony picked (ZEN-23). Meshes are simple primitives
    /// combined into a prefab-ready hierarchy. Tool space: origin at the grip, +Z toward the working end,
    /// pistol grips hang along -Y, screens face +Y.
    /// </summary>
    public static class HeldToolMeshFactory
    {
        public const string ProbeId = "item.tool.depth_probe";
        public const string ScannerId = "item.tool.handheld_scanner";
        public const string ShovelId = "item.tool.excavation_shovel";
        public const string PrecisionId = "item.tool.precision_tool";
        public const string BrushId = "item.tool.brush";
        public const string DrillId = "item.tool.powered_drill";

        public const string ResourcesFolder = "Tools/Held";
        public const string CatalogResource = "Tools/HeldToolCatalog";

        public static readonly string[] AllIds = { ProbeId, ScannerId, ShovelId, PrecisionId, BrushId, DrillId };

        public static string PrefabName(string itemId) => itemId switch
        {
            ProbeId => "Held_ProbeWand",
            ScannerId => "Held_ScannerPistol",
            ShovelId => "Held_EntrenchingShovel",
            PrecisionId => "Held_PrecisionDual",
            BrushId => "Held_BrushStubby",
            DrillId => "Held_RockDrill",
            _ => null
        };

        public static bool IsHeldTool(string itemId) => PrefabName(itemId) != null;

        public sealed class Materials
        {
            public Material metal, metalDark, rubber, emit, screen, bristle, plastic;
        }

        /// <summary>Runtime fallback materials (used only if the baked assets are missing).</summary>
        public static Materials MakeRuntimeMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            Material M(Color c, float metallic, float smooth, Color? emit = null)
            {
                var m = new Material(lit) { hideFlags = HideFlags.HideAndDontSave, name = "HeldToolRuntime" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (emit.HasValue && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emit.Value);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                return m;
            }
            var cyan = new Color(0.2f, 0.95f, 1f);
            return new Materials
            {
                metal = M(new Color(0.55f, 0.57f, 0.60f), 0.85f, 0.55f),
                metalDark = M(new Color(0.12f, 0.13f, 0.14f), 0.85f, 0.40f),
                rubber = M(new Color(0.16f, 0.16f, 0.17f), 0f, 0.22f),
                emit = M(new Color(0.15f, 0.55f, 0.65f), 0.2f, 0.5f, cyan * 2.4f),
                screen = M(new Color(0.05f, 0.25f, 0.32f), 0.15f, 0.7f, cyan * 1.8f),
                bristle = M(new Color(0.62f, 0.48f, 0.28f), 0f, 0.2f),
                plastic = M(new Color(0.38f, 0.40f, 0.43f), 0.15f, 0.35f)
            };
        }

        public static GameObject Build(string itemId, Materials mats)
        {
            string name = PrefabName(itemId);
            if (name == null || mats == null) return null;
            var root = new GameObject(name);
            switch (itemId)
            {
                case ProbeId: BuildProbe(root.transform, mats); break;
                case ScannerId: BuildScanner(root.transform, mats); break;
                case ShovelId: BuildShovel(root.transform, mats); break;
                case PrecisionId: BuildPrecision(root.transform, mats); break;
                case BrushId: BuildBrush(root.transform, mats); break;
                case DrillId: BuildDrill(root.transform, mats); break;
            }
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);
            var align = root.AddComponent<HeldToolAlign>();
            align.ApplyPreset(itemId);
            return root;
        }

        static void BuildProbe(Transform p, Materials m)
        {
            // Grey handle ~10 cm, 30 cm thin rod, cyan tip ring (Option B).
            Part(p, "Handle", MeshLib.CylinderZ(-0.05f, 0.05f, 0.0155f), m.rubber);
            Part(p, "Butt", MeshLib.Sphere(new Vector3(0, 0, -0.052f), 0.0165f), m.metalDark);
            Part(p, "Collar", MeshLib.CylinderZ(0.048f, 0.068f, 0.013f), m.metal);
            Part(p, "Rod", MeshLib.CylinderZ(0.065f, 0.365f, 0.0045f), m.metal);
            Part(p, "TipCap", MeshLib.Sphere(new Vector3(0, 0, 0.368f), 0.007f), m.metal);
            Part(p, "TipRing", MeshLib.TorusZ(0.358f, 0.011f, 0.0036f), m.emit);
            Part(p, "TipGlow", MeshLib.Sphere(new Vector3(0, 0, 0.358f), 0.005f), m.emit);
        }

        static void BuildScanner(Transform p, Materials m)
        {
            // Pistol-grip handheld, cyan screen on +Y, emitter at +Z (Option A).
            Part(p, "Grip", MeshLib.Capsule(new Vector3(0, 0, -0.005f), new Vector3(0, -0.095f, -0.028f), 0.017f), m.rubber);
            Part(p, "Body", MeshLib.RoundBox(new Vector3(0, 0.006f, 0.028f), new Vector3(0.028f, 0.014f, 0.058f), 0.006f), m.metal);
            Part(p, "Bezel", MeshLib.RoundBox(new Vector3(0, 0.026f, 0.018f), new Vector3(0.038f, 0.005f, 0.036f), 0.003f), m.metalDark);
            Part(p, "Screen", MeshLib.RoundBox(new Vector3(0, 0.032f, 0.018f), new Vector3(0.032f, 0.004f, 0.030f), 0.0025f), m.screen);
            Part(p, "Emitter", MeshLib.CylinderZ(0.085f, 0.118f, 0.011f), m.metal);
            Part(p, "Lens", MeshLib.Sphere(new Vector3(0, 0, 0.122f), 0.010f), m.emit);
            Part(p, "Ring", MeshLib.TorusZ(0.118f, 0.014f, 0.0032f), m.emit);
        }

        static void BuildShovel(Transform p, Materials m)
        {
            // Folding entrenching shovel: D-handle, hinge, rectangular blade (Option B).
            Part(p, "Shaft", MeshLib.CylinderZ(-0.04f, 0.07f, 0.012f), m.metalDark);
            Part(p, "Collar", MeshLib.CylinderZ(-0.04f, -0.02f, 0.014f), m.metal);
            Part(p, "DHandle", MeshLib.TorusZ(-0.055f, 0.028f, 0.007f), m.rubber);
            Part(p, "Hinge", MeshLib.RoundBox(new Vector3(0, 0, 0.08f), new Vector3(0.016f, 0.010f, 0.016f), 0.003f), m.metal);
            Part(p, "Shoulder", MeshLib.RoundBox(new Vector3(0, 0.006f, 0.115f), new Vector3(0.058f, 0.006f, 0.018f), 0.003f), m.metal);
            Part(p, "Blade", MeshLib.RoundBox(new Vector3(0, 0, 0.175f), new Vector3(0.055f, 0.0035f, 0.090f), 0.003f), m.metal);
        }

        static void BuildPrecision(Transform p, Materials m)
        {
            // Short dual-ended rounded tool, mid band, one end slightly pointed (Option A).
            Part(p, "Body", MeshLib.CylinderZ(-0.07f, 0.07f, 0.011f), m.metal);
            Part(p, "Band", MeshLib.CylinderZ(-0.012f, 0.012f, 0.0135f), m.rubber);
            Part(p, "Butt", MeshLib.Sphere(new Vector3(0, 0, -0.072f), 0.0115f), m.metal);
            Part(p, "Neck", MeshLib.CylinderZ(0.068f, 0.100f, 0.007f), m.metal);
            Part(p, "Tip", MeshLib.Sphere(new Vector3(0, 0, 0.104f), 0.0055f), m.metal);
        }

        static void BuildBrush(Transform p, Materials m)
        {
            // Short stubby brush, metal body, ferrule, tan bristles (Option A).
            Part(p, "Body", MeshLib.CylinderZ(-0.045f, 0.020f, 0.016f), m.metal);
            Part(p, "Ferrule", MeshLib.CylinderZ(0.012f, 0.032f, 0.0175f), m.metalDark);
            Part(p, "Bristles", MeshLib.CylinderZ(0.028f, 0.105f, 0.0165f), m.bristle);
            Part(p, "Head", MeshLib.Sphere(new Vector3(0, 0, 0.108f), 0.0175f), m.bristle);
        }

        static void BuildDrill(Transform p, Materials m)
        {
            // Heavier rock drill: long body, side handle, long bit (Option B).
            Part(p, "Grip", MeshLib.Capsule(new Vector3(0, 0, -0.01f), new Vector3(0, -0.10f, -0.025f), 0.017f), m.rubber);
            Part(p, "Body", MeshLib.RoundBox(new Vector3(0, 0.016f, 0.040f), new Vector3(0.028f, 0.026f, 0.075f), 0.008f), m.metalDark);
            Part(p, "Rear", MeshLib.Sphere(new Vector3(0, 0.016f, -0.040f), 0.024f), m.metalDark);
            Part(p, "Window", MeshLib.RoundBox(new Vector3(0, 0.044f, 0.030f), new Vector3(0.018f, 0.0045f, 0.032f), 0.002f), m.screen);
            Part(p, "SideHandle", MeshLib.Capsule(new Vector3(0, 0.02f, 0.055f), new Vector3(-0.085f, 0, 0.055f), 0.011f), m.rubber);
            Part(p, "Chuck", MeshLib.CylinderZ(0.110f, 0.145f, 0.014f), m.metal);
            Part(p, "Bit", MeshLib.CylinderZ(0.140f, 0.280f, 0.0045f), m.metalDark);
            Part(p, "BitTip", MeshLib.CylinderZ(0.278f, 0.305f, 0.0025f), m.metalDark);
        }

        static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = false;
        }

        /// <summary>Low-poly primitive meshes used by the factory. All are along or centred in tool space.</summary>
        public static class MeshLib
        {
            const int Seg = 16;

            public static Mesh CylinderZ(float z0, float z1, float r) => Capsule(new Vector3(0, 0, z0), new Vector3(0, 0, z1), r, capped: true);

            public static Mesh Capsule(Vector3 a, Vector3 b, float r, bool capped = true)
            {
                var mesh = new Mesh { name = "held_capsule" };
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 1e-5f) return Sphere(a, r);
                Vector3 z = d / len;
                Vector3 x = Vector3.Cross(Mathf.Abs(z.y) < 0.9f ? Vector3.up : Vector3.right, z).normalized;
                Vector3 y = Vector3.Cross(z, x);
                int rings = Seg;
                int hemi = Seg / 2;
                var verts = new System.Collections.Generic.List<Vector3>();
                var norms = new System.Collections.Generic.List<Vector3>();
                var tris = new System.Collections.Generic.List<int>();

                void Ring(Vector3 centre, Vector3 nz, float radius, int start)
                {
                    for (int i = 0; i < rings; i++)
                    {
                        float ang = i * Mathf.PI * 2f / rings;
                        Vector3 n = (x * Mathf.Cos(ang) + y * Mathf.Sin(ang));
                        verts.Add(centre + n * radius);
                        norms.Add(n);
                    }
                }

                if (capped)
                {
                    for (int h = 0; h <= hemi; h++)
                    {
                        float t = h / (float)hemi;
                        float ang = Mathf.PI * 0.5f * t;
                        float rr = r * Mathf.Cos(ang);
                        Vector3 c = a - z * (r * Mathf.Sin(ang));
                        Vector3 nz = -z;
                        int start = verts.Count;
                        for (int i = 0; i < rings; i++)
                        {
                            float a2 = i * Mathf.PI * 2f / rings;
                            Vector3 radial = x * Mathf.Cos(a2) + y * Mathf.Sin(a2);
                            Vector3 n = (radial * Mathf.Cos(ang) + nz * Mathf.Sin(ang)).normalized;
                            verts.Add(c + radial * rr);
                            norms.Add(n);
                        }
                    }
                }
                int body0 = verts.Count;
                Ring(a, z, r, body0);
                Ring(b, z, r, body0 + rings);
                if (capped)
                {
                    for (int h = 0; h <= hemi; h++)
                    {
                        float t = h / (float)hemi;
                        float ang = Mathf.PI * 0.5f * t;
                        float rr = r * Mathf.Cos(ang);
                        Vector3 c = b + z * (r * Mathf.Sin(ang));
                        for (int i = 0; i < rings; i++)
                        {
                            float a2 = i * Mathf.PI * 2f / rings;
                            Vector3 radial = x * Mathf.Cos(a2) + y * Mathf.Sin(a2);
                            Vector3 n = (radial * Mathf.Cos(ang) + z * Mathf.Sin(ang)).normalized;
                            verts.Add(c + radial * rr);
                            norms.Add(n);
                        }
                    }
                }

                void QuadStrip(int aStart, int bStart)
                {
                    for (int i = 0; i < rings; i++)
                    {
                        int i2 = (i + 1) % rings;
                        tris.Add(aStart + i); tris.Add(bStart + i); tris.Add(bStart + i2);
                        tris.Add(aStart + i); tris.Add(bStart + i2); tris.Add(aStart + i2);
                    }
                }

                int ringCount = verts.Count / rings;
                for (int r0 = 0; r0 < ringCount - 1; r0++) QuadStrip(r0 * rings, (r0 + 1) * rings);

                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            public static Mesh Sphere(Vector3 c, float r)
            {
                int lat = Seg / 2, lon = Seg;
                var verts = new Vector3[(lat + 1) * (lon + 1)];
                var norms = new Vector3[verts.Length];
                var tris = new int[lat * lon * 6];
                int v = 0;
                for (int y = 0; y <= lat; y++)
                {
                    float phi = Mathf.PI * y / lat;
                    for (int x = 0; x <= lon; x++)
                    {
                        float th = 2f * Mathf.PI * x / lon;
                        Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                        verts[v] = c + n * r;
                        norms[v] = n;
                        v++;
                    }
                }
                int t = 0;
                for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i = y * (lon + 1) + x;
                    tris[t++] = i; tris[t++] = i + lon + 1; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + lon + 1; tris[t++] = i + lon + 2;
                }
                var mesh = new Mesh { name = "held_sphere" };
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
                mesh.RecalculateBounds();
                return mesh;
            }

            public static Mesh TorusZ(float z, float R, float r)
            {
                int sec = Seg, tub = Seg / 2 + 2;
                var verts = new Vector3[sec * tub];
                var norms = new Vector3[verts.Length];
                var tris = new int[sec * tub * 6];
                int v = 0;
                for (int i = 0; i < sec; i++)
                {
                    float u = i * Mathf.PI * 2f / sec;
                    Vector3 centre = new Vector3(Mathf.Cos(u) * R, Mathf.Sin(u) * R, z);
                    Vector3 nR = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                    Vector3 nZ = Vector3.forward;
                    for (int j = 0; j < tub; j++)
                    {
                        float w = j * Mathf.PI * 2f / tub;
                        Vector3 n = nR * Mathf.Cos(w) + nZ * Mathf.Sin(w);
                        verts[v] = centre + n * r;
                        norms[v] = n;
                        v++;
                    }
                }
                int t = 0;
                for (int i = 0; i < sec; i++)
                for (int j = 0; j < tub; j++)
                {
                    int i2 = (i + 1) % sec, j2 = (j + 1) % tub;
                    int a = i * tub + j, b = i2 * tub + j, c = i2 * tub + j2, d = i * tub + j2;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                }
                var mesh = new Mesh { name = "held_torus" };
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
                mesh.RecalculateBounds();
                return mesh;
            }

            public static Mesh RoundBox(Vector3 centre, Vector3 ext, float round)
            {
                // Rounded box approximated as a slightly bevelled box (8-corner sphere is overkill).
                round = Mathf.Min(round, Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) * 0.9f);
                Vector3 e = ext - Vector3.one * round;
                e = new Vector3(Mathf.Max(e.x, 0.0005f), Mathf.Max(e.y, 0.0005f), Mathf.Max(e.z, 0.0005f));
                // Build a box then inset is skipped; use a regular box of size ext*2 for clarity and speed.
                return Box(centre, ext);
            }

            public static Mesh Box(Vector3 centre, Vector3 ext)
            {
                Vector3 min = centre - ext, max = centre + ext;
                Vector3[] v =
                {
                    new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
                };
                int[] faces =
                {
                    0,1,2, 0,2,3, // back -Z
                    5,4,7, 5,7,6, // front +Z
                    4,0,3, 4,3,7, // left -X
                    1,5,6, 1,6,2, // right +X
                    3,2,6, 3,6,7, // top +Y
                    4,5,1, 4,1,0  // bottom -Y
                };
                var verts = new Vector3[faces.Length];
                var norms = new Vector3[faces.Length];
                var tris = new int[faces.Length];
                Vector3[] faceN =
                {
                    Vector3.back, Vector3.back, Vector3.forward, Vector3.forward,
                    Vector3.left, Vector3.left, Vector3.right, Vector3.right,
                    Vector3.up, Vector3.up, Vector3.down, Vector3.down
                };
                for (int i = 0; i < faces.Length; i += 3)
                {
                    int f = i / 3;
                    verts[i] = v[faces[i]]; verts[i + 1] = v[faces[i + 1]]; verts[i + 2] = v[faces[i + 2]];
                    Vector3 n = faceN[f];
                    norms[i] = n; norms[i + 1] = n; norms[i + 2] = n;
                    tris[i] = i; tris[i + 1] = i + 1; tris[i + 2] = i + 2;
                }
                var mesh = new Mesh { name = "held_box" };
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = tris;
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
