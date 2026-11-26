using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.Tools.EditorTools
{
    /// <summary>Scene Text editor side (t171u): the GameObject > Xenoarch > Scene Text menu item, edit-mode billboards
    /// facing the Scene view camera, and a live-refresh Inspector.</summary>
    [InitializeOnLoad]
    static class XenoarchSceneTextEditorHooks
    {
        static XenoarchSceneTextEditorHooks()
        {
            XenoarchSceneText.EditorCamera = () => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        static void OnSceneGui(SceneView view)
        {
            if (Application.isPlaying || view == null || view.camera == null) return;
            foreach (var t in XenoarchSceneText.All)
                if (t != null && t.billboard) t.FaceCamera(view.camera);
        }

        [MenuItem("GameObject/Xenoarch/Scene Text", false, 10)]
        static void Create(MenuCommand cmd)
        {
            var go = new GameObject("Scene Text");
            var view = SceneView.lastActiveSceneView;
            if (cmd.context is GameObject parent) go.transform.SetParent(parent.transform, false);
            if (view != null)
            {
                go.transform.position = view.pivot;
                // Face the Scene view camera, upright.
                var fwd = view.camera != null ? view.camera.transform.forward : Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }
            go.AddComponent<XenoarchSceneText>();
            Undo.RegisterCreatedObjectUndo(go, "Create Scene Text");
            Selection.activeGameObject = go;
        }
    }

    [CustomEditor(typeof(XenoarchSceneText)), CanEditMultipleObjects]
    sealed class XenoarchSceneTextInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) foreach (var t in targets) (t as XenoarchSceneText)?.Refresh();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Face Scene camera"))
                {
                    var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                    if (cam != null)
                        foreach (var t in targets)
                        {
                            var st = (XenoarchSceneText)t;
                            Undo.RecordObject(st.transform, "Face Scene camera");
                            var dir = st.transform.position - cam.transform.position; dir.y = 0f;
                            if (dir.sqrMagnitude > 1e-4f) st.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                        }
                }
                if (GUILayout.Button("Refresh")) foreach (var t in targets) (t as XenoarchSceneText)?.Refresh();
            }
            EditorGUILayout.HelpBox("Text uses UI Toolkit (world space). Rich text tags like <b>, <i>, <color=#FFA967> work. Size: Font Size (UI px) x World Scale (m per px).", MessageType.None);
        }
    }
}