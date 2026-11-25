using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Builds the Xenoarch first-person player: arms AnimatorController from FPS_Starter_Pack clips (copied as looping
    /// .anim assets, vendor files untouched), the XA_Player prefab (CharacterController + FirstPersonController +
    /// interaction + HUD + vendor arms as a nested prefab instance), and swaps the placeholder capsule in the scenes.
    /// </summary>
    public static class XenoarchPlayerBuilder
    {
        public const string VendorRoot = "Assets/FPS_Starter_Pack";
        public const string ArmsPrefabPath = VendorRoot + "/Prefabs/Hands_Separate/Hands_01_01.prefab";
        public const string AnimFolder = XenoarchSetup.Root + "/Animation/FPArms";
        public const string ControllerPath = XenoarchSetup.Root + "/Animation/XA_FP_Arms.controller";
        public const string PlayerPrefabPath = XenoarchSetup.Root + "/Prefabs/Player/XA_Player.prefab";
        public const float EyeHeight = 1.62f;
        /// <summary>Hands origin relative to the camera, taken from the vendor demo scene (camera at 0,1.62,0.071 over hands at 0,0,0).</summary>
        public static readonly Vector3 ArmsOffset = new Vector3(0f, -1.62f, -0.071f);

        // clip key -> (fbx path, loop)
        static readonly Dictionary<string, (string fbx, bool loop)> Clips = new Dictionary<string, (string, bool)>
        {
            { "Idle", (VendorRoot + "/Animations/Hands_Empty/Hands_Empty@Idle.FBX", true) },
            { "Walk", (VendorRoot + "/Animations/Hands_Empty/Hands_Empty@Walk.FBX", true) },
            { "Run", (VendorRoot + "/Animations/Hands_Empty/Hands_Empty@Run.FBX", true) },
            { "Jump", (VendorRoot + "/Animations/Hands_Empty/Hands_Empty@Jump.FBX", false) },
            { "Take", (VendorRoot + "/Animations/Hands_Empty/Hands_Empty@Take.FBX", false) },
            { "DeviceUp", (VendorRoot + "/Animations/PC/Hands_PC@Up.FBX", false) },
            { "DeviceIdle", (VendorRoot + "/Animations/PC/Hands_PC@Watch_Idle.FBX", true) },
            { "DeviceDown", (VendorRoot + "/Animations/PC/Hands_PC@Down.FBX", false) },
        };

        static readonly Dictionary<string, float> SceneGravity = new Dictionary<string, float>
        {
            { SceneNames.ShipStarter, 9.81f }, { SceneNames.TradeHubSol, 9.81f },
            { SceneNames.MoonExcavation01, 1.62f }, { SceneNames.MarsExcavation01, 3.71f },
        };

        public static float GravityFor(string sceneName) => SceneGravity.TryGetValue(sceneName, out var g) ? g : 9.81f;

        [MenuItem("Xenoarch/Setup/5. Build First-Person Player + Swap Into Scenes", priority = 25)]
        public static void BuildAll()
        {
            BuildArmsController();
            BuildPlayerPrefab();
            ReplacePlaceholderPlayers();
        }

        // ------------------------------------------------------------------ animation

        static AnimationClip LoadVendorClip(string fbx)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        /// <summary>Copies a vendor clip to our folder (looping as required). Re-copies each run so vendor updates flow in.</summary>
        static AnimationClip CopyClip(string key, string fbx, bool loop)
        {
            var src = LoadVendorClip(fbx);
            if (src == null) { Debug.LogError($"[Xenoarch] Player: clip not found in {fbx}"); return null; }
            string path = $"{AnimFolder}/XA_Arms_{key}.anim";
            var dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (dst == null) { dst = new AnimationClip(); AssetDatabase.CreateAsset(dst, path); }
            EditorUtility.CopySerialized(src, dst);
            dst.name = $"XA_Arms_{key}";
            var settings = AnimationUtility.GetAnimationClipSettings(dst);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(dst, settings);
            EditorUtility.SetDirty(dst);
            return dst;
        }

        public static AnimatorController BuildArmsController()
        {
            XenoarchSetup.EnsureFolder(AnimFolder);
            var c = new Dictionary<string, AnimationClip>();
            foreach (var kv in Clips) c[kv.Key] = CopyClip(kv.Key, kv.Value.fbx, kv.Value.loop);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Interact", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("UsingDevice", AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;

            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(c["Idle"], 0f);
            tree.AddChild(c["Walk"], 1f);
            tree.AddChild(c["Run"], 2f);
            sm.defaultState = loco;

            var jump = sm.AddState("Jump"); jump.motion = c["Jump"];
            var take = sm.AddState("Interact"); take.motion = c["Take"];
            var devUp = sm.AddState("DeviceUp"); devUp.motion = c["DeviceUp"];
            var devIdle = sm.AddState("DeviceIdle"); devIdle.motion = c["DeviceIdle"];
            var devDown = sm.AddState("DeviceDown"); devDown.motion = c["DeviceDown"];

            T(loco, jump, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(jump, loco, 0.15f, 0.8f);
            T(loco, take, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Interact");
            T(take, loco, 0.15f, 0.9f);
            T(loco, devUp, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "UsingDevice");
            T(devUp, devIdle, 0.1f, 0.95f);
            T(devIdle, devDown, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "UsingDevice");
            T(devDown, loco, 0.15f, 0.9f);
            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Xenoarch] Player: arms controller built ({c.Values.Count(x => x != null)}/{Clips.Count} clips)");
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

        // ------------------------------------------------------------------ prefab

        public static GameObject BuildPlayerPrefab()
        {
            XenoarchSetup.EnsureFolder(System.IO.Path.GetDirectoryName(PlayerPrefabPath).Replace('\\', '/'));
            var root = new GameObject("XA_Player");
            try
            {
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.03f;

                var head = new GameObject("Head", typeof(Camera), typeof(AudioListener));
                head.tag = "MainCamera";
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0, EyeHeight, 0);
                var cam = head.GetComponent<Camera>();
                cam.nearClipPlane = 0.03f; cam.fieldOfView = 70f;

                var fpc = root.AddComponent<FirstPersonController>();
                var so = new SerializedObject(fpc);
                so.FindProperty("viewCamera").objectReferenceValue = cam;
                so.FindProperty("head").objectReferenceValue = head.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<InputSystemInteractionInput>();
                root.AddComponent<Interactor>();
                root.AddComponent<InteractionPromptHud>();

                var mount = new GameObject("ArmsMount");
                mount.transform.SetParent(head.transform, false);
                mount.transform.localPosition = ArmsOffset;
                Animator animator = null;
                var armsSrc = AssetDatabase.LoadAssetAtPath<GameObject>(ArmsPrefabPath);
                if (armsSrc == null) Debug.LogError($"[Xenoarch] Player: arms prefab missing at {ArmsPrefabPath}");
                else
                {
                    var arms = (GameObject)PrefabUtility.InstantiatePrefab(armsSrc, mount.transform);
                    arms.name = "Arms (FPS_Starter_Pack " + armsSrc.name + ")";
                    arms.transform.localPosition = Vector3.zero; arms.transform.localRotation = Quaternion.identity;
                    animator = arms.AddComponent<Animator>();
                    animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    foreach (var r in arms.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        string n = r.gameObject.name.ToLowerInvariant();
                        // body parts around the camera would clip into view; only arms/hands are needed in first person
                        if (n.Contains("head") || n.Contains("neck")) r.enabled = false;
                    }
                }
                var armsDriver = mount.AddComponent<FirstPersonArms>();
                var aso = new SerializedObject(armsDriver);
                aso.FindProperty("controller").objectReferenceValue = fpc;
                aso.FindProperty("interactor").objectReferenceValue = root.GetComponent<Interactor>();
                aso.FindProperty("animator").objectReferenceValue = animator;
                aso.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath, out bool ok);
                Debug.Log(ok ? $"[Xenoarch] Player: prefab saved {PlayerPrefabPath}" : $"[Xenoarch] Player: FAILED to save {PlayerPrefabPath}");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Instantiates the player prefab into the active scene (used by the scene builder).</summary>
        public static GameObject InstantiatePlayer(Vector3 pos, float yaw, float gravity)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) ?? BuildPlayerPrefab();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var fpc = go.GetComponent<FirstPersonController>();
            if (fpc != null && !Mathf.Approximately(fpc.gravity, gravity))
            {
                fpc.gravity = gravity;
                PrefabUtility.RecordPrefabInstancePropertyModifications(fpc);
            }
            return go;
        }

        // ------------------------------------------------------------------ scenes

        /// <summary>Replaces every PlaceholderPlayerRig in the gameplay scenes with the XA_Player prefab (same pose).</summary>
        public static int ReplacePlaceholderPlayers()
        {
            int replaced = 0;
            foreach (var name in new[] { SceneNames.ShipStarter, SceneNames.TradeHubSol, SceneNames.MoonExcavation01, SceneNames.MarsExcavation01 })
            {
                string path = XenoarchSetup.ScenePath(name);
                if (!System.IO.File.Exists(path)) continue;
                var scene = SceneManager.GetSceneByPath(path);
                bool wasOpen = scene.IsValid() && scene.isLoaded;
                bool wasDirty = wasOpen && scene.isDirty;
                if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var previous = SceneManager.GetActiveScene();
                try
                {
                    SceneManager.SetActiveScene(scene);
                    int n = 0;
                    foreach (var root in scene.GetRootGameObjects())
                    {
#pragma warning disable CS0618
                        var old = root.GetComponent<PlaceholderPlayerRig>();
#pragma warning restore CS0618
                        if (old == null) continue;
                        var pos = root.transform.position; float yaw = root.transform.eulerAngles.y;
                        Object.DestroyImmediate(root);
                        InstantiatePlayer(pos, yaw, GravityFor(name));
                        n++;
                    }
                    if (n > 0)
                    {
                        replaced += n;
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (wasDirty) Debug.LogWarning($"[Xenoarch] Player: {name} had unsaved edits; player swapped but scene NOT saved. Save it yourself.");
                        else EditorSceneManager.SaveScene(scene);
                        Debug.Log($"[Xenoarch] Player: {name} placeholder capsule -> XA_Player");
                    }
                }
                finally
                {
                    if (previous.IsValid() && previous != scene) SceneManager.SetActiveScene(previous);
                    if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
                }
            }
            Debug.Log($"[Xenoarch] Player: replaced {replaced} placeholder rig(s)");
            return replaced;
        }

        /// <summary>Fraction of arms-clip curve paths that resolve on the arms prefab (generic rig binding check).</summary>
        public static float ArmsBindingCoverage(out string detail)
        {
            detail = "";
            var arms = AssetDatabase.LoadAssetAtPath<GameObject>(ArmsPrefabPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimFolder}/XA_Arms_Walk.anim");
            if (arms == null || clip == null) { detail = "arms prefab or Walk clip missing"; return 0f; }
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var paths = bindings.Select(b => b.path).Distinct().ToList();
            int ok = paths.Count(p => p.Length == 0 || arms.transform.Find(p) != null);
            var missing = paths.Where(p => p.Length > 0 && arms.transform.Find(p) == null).Take(3);
            detail = $"{ok}/{paths.Count} paths" + (ok < paths.Count ? "; e.g. missing " + string.Join(", ", missing) : "");
            return paths.Count == 0 ? 0f : ok / (float)paths.Count;
        }
    }
}
