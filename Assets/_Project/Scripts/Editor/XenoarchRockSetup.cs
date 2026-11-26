using System.IO;
using System.Text;
using DMI.Xenoarch.World;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Builds the decorative rock prefabs (Resources/World/Rocks) from the Poly Haven CC0 models in Assets/Xenoarch_Free:
    /// URP Lit materials (base + normal), convex colliders, a cull-only LODGroup, pivot at the bottom centre and the
    /// largest horizontal size normalised to 1 m. Creates the RockScatterProfile only if missing (hand tuning survives).
    /// Touches no scene: rocks are placed at runtime by RockScatterDirector.
    /// </summary>
    public static class XenoarchRockSetup
    {
        public const string Trigger = "Temp/xenoarch_rocks.trigger";
        const string Src = "Assets/Xenoarch_Free/Models/PolyHaven";
        const string PrefabFolder = XenoarchSetup.ResourcesFolder + "/World/Rocks";
        const string MatFolder = XenoarchSetup.Root + "/Art/Rocks";
        const string ProfilePath = XenoarchSetup.ResourcesFolder + "/" + RockScatterProfile.ResourcePath + ".asset";
        static readonly string[] Names = { "moon_rock_01", "moon_rock_03", "moon_rock_05", "moon_rock_07", "boulder_01" };

        [MenuItem("Tools/Xenoarch/World/Build Rock Prefabs + Scatter Profile")]
        public static void Menu() => RunFromTrigger(true);

        public static void RunFromTrigger(bool rebuild = false)
        {
            var sb = new StringBuilder();
            Run(sb, rebuild);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xenoarch_rocks.txt", sb.ToString());
            Debug.Log("[Xenoarch] Rock setup:\n" + sb);
        }

        public static void Run(StringBuilder log, bool rebuild)
        {
            XenoarchSetup.EnsureFolder(PrefabFolder);
            XenoarchSetup.EnsureFolder(MatFolder);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { log.AppendLine("URP Lit shader not found - skipped"); return; }
            var prefabs = new GameObject[Names.Length];
            for (int i = 0; i < Names.Length; i++) prefabs[i] = BuildOne(Names[i], shader, rebuild, log);
            AssetDatabase.SaveAssets();

            var profile = AssetDatabase.LoadAssetAtPath<RockScatterProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RockScatterProfile>();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (prefabs[i] == null) continue;
                    bool boulder = Names[i].StartsWith("boulder");
                    profile.rocks.Add(new RockScatterProfile.RockKind { prefab = prefabs[i], weight = boulder ? 0.1f : 1f, size = boulder ? new Vector2(1.4f, 3.6f) : new Vector2(0.35f, 2f) });
                }
                profile.bodies.Add(new RockScatterProfile.BodyRocks { bodyId = "body.sol.moon", count = 180, clusterShare = 0.4f, tint = new Color(0.86f, 0.86f, 0.88f), maxSlope = 32f });
                profile.bodies.Add(new RockScatterProfile.BodyRocks { bodyId = "body.sol.mars", count = 160, clusterShare = 0.35f, tint = new Color(1f, 0.6f, 0.43f), maxSlope = 30f });
                XenoarchSetup.EnsureFolder(Path.GetDirectoryName(ProfilePath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(profile, ProfilePath);
                AssetDatabase.SaveAssets();
                log.AppendLine($"created {ProfilePath} ({profile.rocks.Count} rock kinds, Moon 180, Mars 160, fallback {profile.fallback.count})");
            }
            else log.AppendLine($"kept existing {ProfilePath}");
        }

        static GameObject BuildOne(string name, Shader shader, bool rebuild, StringBuilder log)
        {
            string prefabPath = $"{PrefabFolder}/Rock_{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null && !rebuild) { log.AppendLine($"kept {prefabPath}"); return existing; }
            string dir = $"{Src}/{name}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}_1k.fbx");
            if (model == null) { log.AppendLine($"missing model {dir}/{name}_1k.fbx"); return null; }

            string matPath = $"{MatFolder}/Rock_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/textures/{name}_diff_1k.jpg");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/textures/{name}_nor_gl_1k.png");
            if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.12f); mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            var root = new GameObject("Rock_" + name);
            var inst = Object.Instantiate(model, root.transform);
            inst.name = "Mesh";
            var rends = inst.GetComponentsInChildren<MeshRenderer>();
            if (rends.Length == 0) { Object.DestroyImmediate(root); log.AppendLine($"{name}: no renderers"); return null; }
            foreach (var r in rends) { var m = r.sharedMaterials; for (int i = 0; i < m.Length; i++) m[i] = mat; r.sharedMaterials = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            float horiz = Mathf.Max(b.size.x, b.size.z, 0.001f);
            float k = 1f / horiz;
            inst.transform.localScale = inst.transform.localScale * k;
            b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            inst.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            int tris = 0;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                tris += mf.sharedMesh.triangles.Length / 3;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true;
            }
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.02f, rends) });
            lod.RecalculateBounds();
            root.isStatic = false;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine($"built {prefabPath}: {tris} tris, native size {b.size.x / k:0.00} x {b.size.y / k:0.00} x {b.size.z / k:0.00} (scaled x{k:0.000})");
            return prefab;
        }
    }
}
