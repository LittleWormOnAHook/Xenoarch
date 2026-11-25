using System.Collections.Generic;
using System.IO;
using System.Linq;
using DMI.Xenoarch.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Adds the Kade full body and the hand slots to XA_Player, editing the existing prefab in place (component file
    /// ids stay stable, so scene instance overrides such as per-scene gravity survive). Never opens or saves scenes.
    /// Kade mesh/avatar/textures/material: copied read-only from Genesis TripoModels (no Invector parts).
    /// Locomotion clips: Kevin Iglesias "Basic Motions" (humanoid), copied from Genesis.
    /// </summary>
    public static class XenoarchBodyBuilder
    {
        public const string KadeFolder = XenoarchSetup.Root + "/Art/Characters/Kade";
        public const string KadeFbx = KadeFolder + "/rugged_male_character_3d_model.fbx";
        public const string GeneratedFolder = KadeFolder + "/Generated";
        public const string NoHeadMeshPath = GeneratedFolder + "/Kade_Body_NoHead.asset";
        public const string HeadMeshPath = GeneratedFolder + "/Kade_HeadNeck.asset";
        public const string ClipFolder = XenoarchSetup.Root + "/Art/Characters/Animations/BasicMotions";
        public const string BodyControllerPath = XenoarchSetup.Root + "/Animation/XA_Body_Locomotion.controller";
        public const string BodyName = "Body";
        /// <summary>Body sits slightly behind the camera so looking down shows chest/legs rather than the inside of the torso.</summary>
        public const float BodyBackOffset = 0.14f;
        /// <summary>Target height of the head bone (eyes sit ~0.1 m above it, at the camera).</summary>
        public const float HeadBoneHeight = XenoarchPlayerBuilder.EyeHeight - 0.1f;

        [MenuItem("Xenoarch/Setup/6. Build Player Body (Kade) + Hand Slots (prefab only)", priority = 26)]
        public static void BuildBodyAndHands()
        {
            ConvertKadeMaterials();
            XenoarchPlayerBuilder.BuildArmsController();
            BuildBodyController();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(XenoarchPlayerBuilder.PlayerPrefabPath) == null) XenoarchPlayerBuilder.BuildPlayerPrefab();
            UpgradePlayerPrefab();
        }

        // ------------------------------------------------------------------ materials

        public static void ConvertKadeMaterials()
        {
            string rpt = Art.VendorMaterialConverter.ReportPath;
            string creepy = "Logs/xenoarch_material_conversion_creepycat.txt";
            if (File.Exists(rpt) && !File.Exists(creepy) && File.ReadAllText(rpt).Contains("_Creepy_Cat")) File.Copy(rpt, creepy);
            Art.VendorMaterialConverter.ConvertFolder(KadeFolder);
            if (File.Exists(rpt)) File.Copy(rpt, "Logs/xenoarch_material_conversion_kade.txt", true);
            if (File.Exists(creepy)) File.Copy(creepy, rpt, true); // keep the Creepy Cat report at its usual path
            // Genesis used a custom AO slot (_DMAOMap); HDRP mask-map G is not AO here, so use Kade_AO for occlusion.
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(KadeFolder + "/rugged_male_character_3d_model.fbm/Kade_AO.png");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { KadeFolder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || ao == null || !m.HasProperty("_OcclusionMap")) continue;
                m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP");
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ clips + controller

        static List<AnimationClip> FbxClips(string file) =>
            AssetDatabase.LoadAllAssetsAtPath($"{ClipFolder}/{file}").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();

        static AnimationClip Clip(string file, string exactName, string suffix = null)
        {
            var clips = FbxClips(file);
            var c = clips.FirstOrDefault(x => x.name == exactName)
                    ?? (suffix != null ? clips.FirstOrDefault(x => x.name.EndsWith(suffix)) : null);
            if (c == null) Debug.LogWarning($"[Xenoarch] Body: clip '{exactName}' not in {file} (have: {string.Join(", ", clips.Select(x => x.name))})");
            return c;
        }

        static readonly (string dir, float x, float z)[] Dirs =
        {
            ("Forwards", 0f, 1f), ("ForwardsLeft", -0.7071f, 0.7071f), ("ForwardsRight", 0.7071f, 0.7071f),
            ("Left", -1f, 0f), ("Right", 1f, 0f),
            ("Backwards", 0f, -1f), ("BackwardsLeft", -0.7071f, -0.7071f), ("BackwardsRight", 0.7071f, -0.7071f),
        };

        public static AnimatorController BuildBodyController()
        {
            XenoarchSetup.EnsureFolder(Path.GetDirectoryName(BodyControllerPath).Replace('\\', '/'));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XenoarchPlayerBuilder.PlayerPrefabPath);
            var fpc = prefab != null ? prefab.GetComponent<FirstPersonController>() : null;
            float walk = fpc != null ? fpc.walkSpeed : 3.2f, sprint = fpc != null ? fpc.sprintSpeed : 5.8f;

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(BodyControllerPath) != null) AssetDatabase.DeleteAsset(BodyControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(BodyControllerPath);
            ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ac.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            var prms = ac.parameters;
            foreach (var q in prms) if (q.name == "Grounded") q.defaultBool = true; // start grounded, not falling
            ac.parameters = prms;
            var sm = ac.layers[0].stateMachine;

            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.useAutomaticThresholds = false;
            int motions = 0;
            var idle = Clip("BasicMotions@Idle01.fbx", "BasicMotions@Idle01");
            if (idle != null) { tree.AddChild(idle, Vector2.zero); motions++; }
            // walk ring at ~1.5 m/s (accel/decel), run ring at the controller's walk speed, sprint ring forward-only.
            foreach (var (file, prefix, speed, forwardOnly) in new[]
            {
                ("BasicMotions@Walk01.fbx", "BasicMotions@Walk01", 1.5f, false),
                ("BasicMotions@Run01.fbx", "BasicMotions@Run01", walk, false),
                ("BasicMotions@Sprint01.fbx", "BasicMotions@Sprint01", sprint, true),
            })
            {
                foreach (var d in Dirs)
                {
                    if (forwardOnly && d.z <= 0f) continue;
                    var c = Clip(file, $"{prefix} - {d.dir}", " " + d.dir);
                    if (c == null) continue;
                    tree.AddChild(c, new Vector2(d.x, d.z) * speed);
                    motions++;
                }
            }
            sm.defaultState = loco;

            var jumpStart = sm.AddState("JumpStart"); jumpStart.motion = Clip("BasicMotions@Jump01.fbx", "BasicMotions@Jump01");
            var air = sm.AddState("InAir"); air.motion = Clip("BasicMotions@Jump01.fbx", "BasicMotions@Jump01 - MidAir", "MidAir");
            var fall = sm.AddState("Fall"); fall.motion = Clip("BasicMotions@Fall01.fbx", "BasicMotions@Fall01");
            var land = sm.AddState("Land"); land.motion = Clip("BasicMotions@Jump01.fbx", "BasicMotions@Jump01 - Land", "Land");

            T(loco, jumpStart, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(jumpStart, air, 0.15f, 0.8f);
            var js2land = T(jumpStart, land, 0.1f, 0.4f); js2land.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(loco, fall, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            T(air, land, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(fall, land, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(land, loco, 0.2f, 0.6f);
            T(land, loco, 0.15f).AddCondition(AnimatorConditionMode.Greater, 1.0f, "Speed");
            var anyJump = T(fall, jumpStart, 0.08f); anyJump.AddCondition(AnimatorConditionMode.If, 0, "Jump"); // coyote-ish jump off a step

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Xenoarch] Body: controller built ({motions} locomotion motions; walk {walk} / sprint {sprint} m/s)");
            return ac;
        }

        static AnimatorStateTransition T(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
        {
            var t = from.AddTransition(to);
            t.duration = duration;
            t.hasExitTime = exitTime >= 0f;
            if (exitTime >= 0f) t.exitTime = exitTime;
            return t;
        }

        // ------------------------------------------------------------------ head split

        /// <summary>Splits the Kade mesh into body-without-head/neck and head/neck-only meshes by skin weights.</summary>
        public static (Mesh noHead, Mesh head) BuildHeadSplit(SkinnedMeshRenderer smr, Animator animator, out string detail)
        {
            var src = smr.sharedMesh;
            var bones = smr.bones;
            Transform neck = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
            if (neck == null) neck = bones.FirstOrDefault(b => b != null && b.name.ToLowerInvariant().Contains("neck"));
            if (neck == null) { detail = "no neck bone"; return (null, null); }
            var isHeadBone = bones.Select(b => b != null && (b == neck || b.IsChildOf(neck))).ToArray();

            var perVertex = src.GetBonesPerVertex();
            var weights = src.GetAllBoneWeights();
            var headW = new float[src.vertexCount];
            int wi = 0;
            for (int v = 0; v < perVertex.Length; v++)
            {
                float w = 0f;
                for (int k = 0; k < perVertex[v]; k++, wi++) if (isHeadBone[weights[wi].boneIndex]) w += weights[wi].weight;
                headW[v] = w;
            }

            var noHead = Object.Instantiate(src); noHead.name = "Kade_Body_NoHead";
            var head = Object.Instantiate(src); head.name = "Kade_HeadNeck";
            int kept = 0, moved = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var keep = new List<int>(tris.Length); var hd = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    float m = Mathf.Max(headW[tris[i]], Mathf.Max(headW[tris[i + 1]], headW[tris[i + 2]]));
                    var dst = m >= 0.5f ? hd : keep;
                    dst.Add(tris[i]); dst.Add(tris[i + 1]); dst.Add(tris[i + 2]);
                }
                noHead.SetTriangles(keep, s, true); head.SetTriangles(hd, s, true);
                kept += keep.Count / 3; moved += hd.Count / 3;
            }
            detail = $"{kept} body tris, {moved} head/neck tris (neck bone '{neck.name}')";
            XenoarchSetup.EnsureFolder(GeneratedFolder);
            return (SaveMesh(noHead, NoHeadMeshPath), SaveMesh(head, HeadMeshPath));
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            EditorUtility.CopySerialized(m, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }

        // ------------------------------------------------------------------ prefab

        public static void UpgradePlayerPrefab()
        {
            string path = XenoarchPlayerBuilder.PlayerPrefabPath;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fpc = root.GetComponent<FirstPersonController>();
                var cam = fpc != null ? fpc.ViewCamera : root.GetComponentInChildren<Camera>(true);
                var mount = cam != null ? cam.transform.Find("ArmsMount") : null;
                var armsDriver = mount != null ? mount.GetComponent<FirstPersonArms>() : null;

                // ---- arms: controller (rebuilt), hand slots, upper-arm refs
                if (armsDriver != null)
                {
                    var armsAnim = mount.GetComponentInChildren<Animator>(true);
                    if (armsAnim != null) armsAnim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(XenoarchPlayerBuilder.ControllerPath);
                    var so = new SerializedObject(armsDriver);
                    so.FindProperty("animator").objectReferenceValue = armsAnim;
                    Transform armsRoot = armsAnim != null ? armsAnim.transform : mount;
                    so.FindProperty("leftUpperArm").objectReferenceValue = FindDeep(armsRoot, "upperarm_l");
                    so.FindProperty("rightUpperArm").objectReferenceValue = FindDeep(armsRoot, "upperarm_r");
                    so.FindProperty("leftSlot").objectReferenceValue = EnsureSlot(FindDeep(armsRoot, "hand_l"), "HandSlot_L");
                    so.FindProperty("rightSlot").objectReferenceValue = EnsureSlot(FindDeep(armsRoot, "hand_r"), "HandSlot_R");
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else Debug.LogError("[Xenoarch] Body: XA_Player has no Head/ArmsMount/FirstPersonArms; rebuild with Setup 5 first");

                // ---- body
                var old = root.transform.Find(BodyName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(KadeFbx);
                if (model == null) Debug.LogError($"[Xenoarch] Body: Kade model missing at {KadeFbx}");
                else BuildBody(root, fpc, model);

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                Debug.Log(ok ? $"[Xenoarch] Body: {path} updated (Kade body + hand slots)" : $"[Xenoarch] Body: FAILED to save {path}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        static void BuildBody(GameObject root, FirstPersonController fpc, GameObject model)
        {
            var body = new GameObject(BodyName);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0f, -BodyBackOffset);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, body.transform);
            inst.name = "Kade";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;

            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            if (animator.avatar == null)
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(KadeFbx).OfType<Avatar>().FirstOrDefault();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BodyControllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // camera is inside the bounds and the shadow must animate

            // scale so the head bone sits just under the camera
            var headBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (headBone != null)
            {
                float h = headBone.position.y - inst.transform.position.y;
                float s = h > 0.01f ? Mathf.Clamp(HeadBoneHeight / h, 0.25f, 4f) : 1f;
                inst.transform.localScale = Vector3.one * s;
                Debug.Log($"[Xenoarch] Body: Kade head bone at {h:F3} m -> scale {s:F3}");
            }
            else Debug.LogWarning("[Xenoarch] Body: Kade avatar is not humanoid / no head bone; scale left at 1");

            var smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
            SkinnedMeshRenderer headSmr = null;
            if (smr != null)
            {
                var (noHead, head) = BuildHeadSplit(smr, animator, out var detail);
                Debug.Log($"[Xenoarch] Body: head split {detail}");
                if (noHead != null)
                {
                    smr.sharedMesh = noHead;
                    smr.shadowCastingMode = ShadowCastingMode.On;
                    smr.updateWhenOffscreen = true;
                    var hgo = new GameObject("Kade_HeadNeck (shadow only)");
                    hgo.transform.SetParent(smr.transform.parent, false);
                    hgo.transform.localPosition = smr.transform.localPosition;
                    hgo.transform.localRotation = smr.transform.localRotation;
                    hgo.transform.localScale = smr.transform.localScale;
                    headSmr = hgo.AddComponent<SkinnedMeshRenderer>();
                    headSmr.sharedMesh = head;
                    headSmr.bones = smr.bones;
                    headSmr.rootBone = smr.rootBone;
                    headSmr.sharedMaterials = smr.sharedMaterials;
                    headSmr.localBounds = smr.localBounds;
                    headSmr.updateWhenOffscreen = true;
                    headSmr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
            else Debug.LogError("[Xenoarch] Body: no SkinnedMeshRenderer in Kade model");
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c); // never block interaction rays

            var pb = body.AddComponent<PlayerBody>();
            var so = new SerializedObject(pb);
            so.FindProperty("controller").objectReferenceValue = fpc;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("bodyRenderer").objectReferenceValue = smr;
            so.FindProperty("headRenderer").objectReferenceValue = headSmr;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform EnsureSlot(Transform hand, string name)
        {
            if (hand == null) return null;
            var t = hand.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(hand, false); }
            return t;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        // ------------------------------------------------------------------ validation

        public static void Validate(System.Action<bool, string> check)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(XenoarchPlayerBuilder.PlayerPrefabPath);
            var body = player != null ? player.GetComponentInChildren<PlayerBody>(true) : null;
            check(body != null, "XA_Player has PlayerBody (Kade)");
            var anim = body != null ? body.GetComponentInChildren<Animator>(true) : null;
            check(anim != null && anim.avatar != null && anim.avatar.isHuman && anim.runtimeAnimatorController != null, "Body Animator: humanoid avatar + locomotion controller");
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(BodyControllerPath);
            int motions = 0;
            if (ac != null) foreach (var st in ac.layers[0].stateMachine.states) if (st.state.motion is BlendTree bt) motions = bt.children.Count(c => c.motion != null);
            check(motions >= 9, $"Body locomotion blend tree has clips ({motions})");
            var noHead = AssetDatabase.LoadAssetAtPath<Mesh>(NoHeadMeshPath);
            var head = AssetDatabase.LoadAssetAtPath<Mesh>(HeadMeshPath);
            check(noHead != null && head != null && head.triangles.Length > 0 && noHead.triangles.Length > head.triangles.Length, "Kade head/neck split meshes generated");
            var rends = body != null ? body.GetComponentsInChildren<SkinnedMeshRenderer>(true) : new SkinnedMeshRenderer[0];
            check(rends.Any(r => r.sharedMesh == noHead && r.shadowCastingMode == ShadowCastingMode.On) && rends.Any(r => r.sharedMesh == head && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Body casts shadows; head is shadows-only for the local camera");
            bool urp = rends.Length > 0 && rends.SelectMany(r => r.sharedMaterials).All(m => m != null && m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline/"));
            check(urp, "Kade materials are URP");
            var arms = player != null ? player.GetComponentInChildren<FirstPersonArms>(true) : null;
            check(arms != null && arms.LeftSlot != null && arms.RightSlot != null, "Arms have HandSlot_L / HandSlot_R");
            var armsAc = AssetDatabase.LoadAssetAtPath<AnimatorController>(XenoarchPlayerBuilder.ControllerPath);
            check(armsAc != null && armsAc.layers.Any(l => l.name == FirstPersonArms.LeftLayer) && armsAc.layers.Any(l => l.name == FirstPersonArms.RightLayer)
                  && armsAc.parameters.Any(p => p.name == "ToolHeld"), "Arms controller: LeftHand/RightHand layers + ToolHeld/LeftHeld/RightHeld");
        }
    }
}
