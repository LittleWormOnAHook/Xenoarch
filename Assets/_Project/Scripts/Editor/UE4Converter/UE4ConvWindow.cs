using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>Xenoarch/Tools/UE4 -> Unity Converter: point at a UE4 export / pack source folder and convert meshes, textures, animations and audio.</summary>
    public sealed class UE4ConverterWindow : EditorWindow
    {
        ConverterOptions _o;
        Vector2 _scroll, _sumScroll;
        string _summary = "";
        string _status = "";
        List<string> _roots = new List<string>();
        int _rootIdx;
        bool _fMesh = true, _fPrefab = true, _fTex = true, _fMisc = true;
        ConversionReport _last;
        const string DefaultRoot = "Assets/_Project/Ruins";
        static readonly int[] Sizes = { 512, 1024, 2048, 4096, 8192 };
        static readonly int[] Yaws = { 0, 90, 180, 270 };

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity Converter", priority = 310)]
        public static void Open()
        {
            var w = GetWindow<UE4ConverterWindow>(false, "UE4 \u2192 Unity", true);
            w.minSize = new Vector2(480, 560);
            w.Show();
        }

        void OnEnable()
        {
            _o = ConverterOptions.Load();
            RefreshRoots();
        }

        void RefreshRoots()
        {
            _roots.Clear();
            // the user's own "Ruins" folder(s) anywhere in Assets come first
            foreach (var g in AssetDatabase.FindAssets("Ruins t:Folder"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (string.Equals(Path.GetFileName(path), "Ruins", StringComparison.OrdinalIgnoreCase) && !_roots.Contains(path)) _roots.Add(path);
            }
            if (!_roots.Contains(DefaultRoot)) _roots.Add(DefaultRoot);
            if (!string.IsNullOrEmpty(_o.destRoot) && !_roots.Contains(_o.destRoot)) _roots.Add(_o.destRoot);
            _rootIdx = Mathf.Max(0, _roots.IndexOf(_o.destRoot));
            if (string.IsNullOrEmpty(_o.destRoot)) _o.destRoot = _roots[0];
        }

        void OnGUI()
        {
            if (_o == null) OnEnable();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Point at a folder of UE4 exports (FBX/OBJ meshes, PNG/TGA/EXR textures, animations). .uasset files cannot be read: export them from UE4 first (Content Browser > Asset Actions > Export) or use the pack's Source folder.", MessageType.Info);

            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string before = _o.sourceFolder;
                _o.sourceFolder = EditorGUILayout.TextField(_o.sourceFolder);
                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    string p = EditorUtility.OpenFolderPanel("UE4 export / pack source folder", string.IsNullOrEmpty(_o.sourceFolder) ? "" : _o.sourceFolder, "");
                    if (!string.IsNullOrEmpty(p)) { _o.sourceFolder = p; GUI.FocusControl(null); }
                }
                if (_o.sourceFolder != before && !string.IsNullOrWhiteSpace(_o.sourceFolder) && (string.IsNullOrEmpty(_o.packName) || _autoName == _o.packName))
                    _o.packName = _autoName = ConverterOptions.Sanitize(new DirectoryInfo(_o.sourceFolder.TrimEnd('\\', '/')).Name);
            }

            EditorGUILayout.LabelField("Destination", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                int idx = EditorGUILayout.Popup("Root folder", _rootIdx, _roots.Select(r => r == DefaultRoot && !AssetDatabase.IsValidFolder(r) ? r + " (new)" : r).ToArray());
                if (idx != _rootIdx) { _rootIdx = idx; _o.destRoot = _roots[idx]; }
                if (GUILayout.Button("Other...", GUILayout.Width(70)))
                {
                    string p = EditorUtility.OpenFolderPanel("Destination root (inside Assets)", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(p))
                    {
                        string full = p.Replace('\\', '/');
                        string data = Application.dataPath.Replace('\\', '/');
                        if (full.StartsWith(data)) { string a = "Assets" + full.Substring(data.Length); if (!_roots.Contains(a)) _roots.Add(a); _rootIdx = _roots.IndexOf(a); _o.destRoot = a; }
                        else EditorUtility.DisplayDialog("UE4 Converter", "The destination must be inside this project's Assets folder.", "OK");
                    }
                }
            }
            _o.packName = EditorGUILayout.TextField("Pack name", _o.packName);
            EditorGUILayout.LabelField("Output", _o.DestFolder, EditorStyles.miniLabel);

            _fMesh = EditorGUILayout.BeginFoldoutHeaderGroup(_fMesh, "Meshes, scale, axis, rig");
            if (_fMesh)
            {
                _o.scaleMode = (ConvScaleMode)EditorGUILayout.EnumPopup(new GUIContent("Scale fix", "Auto: FBX uses the file's own unit (UE exports cm, Unity converts to m); OBJ is guessed from its size.\nUE cm to m: force x0.01.\nNone: x1. Custom: your factor."), _o.scaleMode);
                if (_o.scaleMode == ConvScaleMode.Custom) _o.customScale = EditorGUILayout.FloatField("Custom factor", _o.customScale);
                _o.bakeAxis = EditorGUILayout.Toggle(new GUIContent("Bake axis conversion", "UE is Z-up/X-forward. Bakes the file's axis into the mesh so prefabs have identity rotation."), _o.bakeAxis);
                _o.objIsZUp = EditorGUILayout.Toggle(new GUIContent("OBJ is Z-up", "OBJ has no axis info; rotate OBJ prefabs -90 degrees on X."), _o.objIsZUp);
                int yi = Mathf.Max(0, Array.IndexOf(Yaws, _o.extraYaw));
                _o.extraYaw = Yaws[EditorGUILayout.Popup("Extra Y rotation", yi, Yaws.Select(y => y + " deg").ToArray())];
                _o.lightmapUVs = EditorGUILayout.Toggle("Generate lightmap UVs", _o.lightmapUVs);
                _o.rig = (RigMode)EditorGUILayout.EnumPopup(new GUIContent("Skeletal rig", "Auto: Humanoid when the name looks humanoid (mannequin, character, player...), otherwise Generic. Humanoid falls back to Generic if mapping fails."), _o.rig);
                _o.importAnimations = EditorGUILayout.Toggle("Import animation takes", _o.importAnimations);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _fPrefab = EditorGUILayout.BeginFoldoutHeaderGroup(_fPrefab, "Prefabs");
            if (_fPrefab)
            {
                _o.makePrefabs = EditorGUILayout.Toggle("Create prefabs", _o.makePrefabs);
                _o.detectLods = EditorGUILayout.Toggle(new GUIContent("Detect LODs (_LOD0/_LOD1)", "Meshes named Name_LOD0, Name_LOD1... become one prefab with a LODGroup."), _o.detectLods);
                _o.generateColliders = EditorGUILayout.Toggle(new GUIContent("Generate colliders", "Uses UE UCX_/UBX_/USP_ shells when present (convex), otherwise a MeshCollider from the visible LOD0 mesh."), _o.generateColliders);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _fTex = EditorGUILayout.BeginFoldoutHeaderGroup(_fTex, "Textures / materials");
            if (_fTex)
            {
                _o.flipNormalGreen = EditorGUILayout.Toggle(new GUIContent("Flip normal green (DX to GL)", "UE normal maps are DirectX; Unity expects OpenGL. Files named *_GL are left alone."), _o.flipNormalGreen);
                _o.cutoutFoliage = EditorGUILayout.Toggle(new GUIContent("Alpha-cutout foliage", "Base colours with real alpha, or a _Mask/_Opacity map, become alpha-clipped double-sided materials."), _o.cutoutFoliage);
                int si = Mathf.Max(0, Array.IndexOf(Sizes, _o.maxTextureSize));
                _o.maxTextureSize = Sizes[EditorGUILayout.Popup("Max texture size", si, Sizes.Select(s => s.ToString()).ToArray())];
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            _fMisc = EditorGUILayout.BeginFoldoutHeaderGroup(_fMisc, "Other");
            if (_fMisc)
            {
                _o.copyAudio = EditorGUILayout.Toggle("Copy audio (wav/ogg/mp3)", _o.copyAudio);
                _o.overwrite = EditorGUILayout.Toggle(new GUIContent("Overwrite existing", "Off: anything that already exists at the destination is left alone and listed as skipped."), _o.overwrite);
                _o.dryRun = EditorGUILayout.Toggle(new GUIContent("Dry run", "Scan and report what would happen; writes nothing under Assets."), _o.dryRun);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
                if (GUILayout.Button(_o.dryRun ? "Dry run" : "Convert", GUILayout.Height(30))) Go();
                GUI.enabled = true;
                if (GUILayout.Button("Reveal destination", GUILayout.Height(30), GUILayout.Width(130)))
                {
                    var a = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_o.DestFolder);
                    if (a != null) { EditorGUIUtility.PingObject(a); Selection.activeObject = a; } else _status = "Destination does not exist yet.";
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                string lastJson = EditorPrefs.GetString(UE4Converter.LastReportPref, "");
                GUI.enabled = _last != null && File.Exists(_last.mdPath ?? "");
                if (GUILayout.Button("Open report (.md)")) EditorUtility.OpenWithDefaultApp(_last.mdPath);
                GUI.enabled = !string.IsNullOrEmpty(lastJson) && File.Exists(lastJson) && !EditorApplication.isPlayingOrWillChangePlaymode;
                if (GUILayout.Button("Revert last conversion"))
                {
                    if (EditorUtility.DisplayDialog("Revert last conversion", "Delete the assets created by the last conversion? (Overwritten assets are not restored.)", "Revert", "Cancel"))
                        _status = UE4Converter.Revert(lastJson);
                }
                GUI.enabled = true;
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);

            if (!string.IsNullOrEmpty(_summary))
            {
                EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
                _sumScroll = EditorGUILayout.BeginScrollView(_sumScroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(260));
                EditorGUILayout.SelectableLabel(_summary, EditorStyles.textArea, GUILayout.ExpandHeight(true), GUILayout.MinHeight(Mathf.Max(120, _summary.Split('\n').Length * 15)));
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndScrollView();
            if (GUI.changed) _o.Save();
        }

        string _autoName;

        void Go()
        {
            _o.Save();
            string err = UE4Converter.Validate(_o);
            if (err != null) { _status = err; _summary = ""; return; }
            if (!_o.dryRun && !EditorUtility.DisplayDialog("UE4 \u2192 Unity", $"Convert\n{_o.sourceFolder}\ninto\n{_o.DestFolder}?", "Convert", "Cancel")) return;
            _last = UE4Converter.Run(_o);
            _summary = _last.Summary();
            _status = _last.mdPath != null ? "Report: " + _last.mdPath : "";
            Repaint();
        }
    }
}