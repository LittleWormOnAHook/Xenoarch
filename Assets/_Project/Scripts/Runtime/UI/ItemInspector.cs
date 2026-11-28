using System.Collections.Generic;
using DMI.Xenoarch.Audio;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Ship;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// 3D item inspector (approved inspector_v1 mockup). One offscreen camera and RenderTexture, enabled only while
    /// the window is open. Preview fallbacks: worldPrefab, held-tool prefab / HeldToolMeshFactory, ArtifactShape
    /// primitive, or a textured icon quad.
    /// </summary>
    public sealed class ItemInspector : MonoBehaviour
    {
        public const string PanelId = "item.inspector";
        public const int Layer = 31;
        const int RtSize = 1024;

        static ItemInspector _i;
        int _closedFrame = -1;
        public static bool IsOpen => _i != null && _i._open;
        public static bool ClosedThisFrame => _i != null && _i._closedFrame == Time.frameCount;

        UIDocument _doc;
        VisualElement _root, _view;
        bool _open;
        ItemStack _stack;
        RenderTexture _rt;
        Camera _cam;
        Transform _rig, _stage, _pivot;
        GameObject _preview;
        float _yaw, _pitch, _dist, _panX, _panY;
        bool _rotating, _panning;
        Vector2 _lastPtr;
        float _baseDist = 1.6f;

        bool _dockRight = true;

        /// <summary>Open (or retarget) the docked inspector. <paramref name="panelPos"/> is the clicked panel position:
        /// the inspector docks on the opposite half of the screen so the grid you clicked stays visible and clickable.</summary>
        public static void Open(ItemStack stack, Vector2? panelPos = null)
        {
            if (stack == null || stack.IsEmpty) return;
            if (_i == null)
            {
                var go = new GameObject("Xenoarch Item Inspector");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<ItemInspector>();
            }
            _i.Show(stack, panelPos);
        }

        public static void Close()
        {
            if (_i != null) _i.Hide();
        }

        void Show(ItemStack stack, Vector2? panelPos)
        {
            bool wasOpen = _open;
            _stack = stack.Clone();
            EnsureUi();
            if (!wasOpen)
            {
                float w = _doc.rootVisualElement.layout.width;
                if (float.IsNaN(w) || w < 100f) w = 1920f;
                _dockRight = !panelPos.HasValue || panelPos.Value.x < w * 0.5f;
            }
            EnsureRig();
            BuildPreview();
            BuildUi();
            _open = true;
            if (_cam != null) _cam.enabled = true;
            XenoarchUI.Show(_root, true);
            UIState.SetOpen(PanelId, true);
            if (!wasOpen) UISound.Play(UISoundEvent.WindowOpen);
        }

        void Hide()
        {
            if (!_open) return;
            _open = false;
            _closedFrame = Time.frameCount;
            ClearPreview();
            if (_cam != null) _cam.enabled = false;
            XenoarchUI.Show(_root, false);
            UIState.SetOpen(PanelId, false);
            UISound.Play(UISoundEvent.WindowClose);
        }

        void Update()
        {
            if (!_open) return;
            if (_cam != null) _cam.Render();
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) ResetView();
            if (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) Hide();
        }

        void OnDestroy()
        {
            ClearPreview();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_i == this) _i = null;
            UIState.SetOpen(PanelId, false);
        }

        void EnsureUi()
        {
            if (_doc != null) return;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 60;
            var docRoot = _doc.rootVisualElement;
            XenoarchUI.FillRoot(docRoot);
            docRoot.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch");
            if (uss != null) docRoot.styleSheets.Add(uss);
            _root = new VisualElement();
            _root.AddToClassList("xa-fill");
            _root.AddToClassList("xa-inspect-root");
            _root.pickingMode = PickingMode.Ignore; // docked, not modal: only the panel itself takes clicks
            docRoot.Add(_root);
            XenoarchUI.Show(_root, false);
        }

        // Keep the docked panel fully inside the panel at every aspect: nudge it in if an edge would leave the screen.
        void ClampOnScreen(VisualElement m)
        {
            var parent = m.parent;
            if (parent == null) return;
            float pw = parent.layout.width;
            if (float.IsNaN(pw) || pw < 100f) return;
            var l = m.layout;
            if (float.IsNaN(l.width) || l.width < 10f) return;
            const float edge = 12f;
            float x = Mathf.Clamp(l.x, edge, Mathf.Max(edge, pw - l.width - edge));
            if (Mathf.Abs(x - l.x) < 0.5f) return;
            m.style.right = StyleKeyword.Auto;
            m.style.left = x;
        }

        void EnsureRig()
        {
            if (_rig != null) return;
            _rt = new RenderTexture(RtSize, RtSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "XenoarchItemPreview",
                antiAliasing = 1,
                hideFlags = HideFlags.DontSave
            };
            _rt.Create();

            var root = new GameObject("PreviewRig");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(0f, -8000f, 0f);
            _rig = root.transform;

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_rig, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.008f, 0.012f, 1f);
            _cam.fieldOfView = 32f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 24f;
            _cam.targetTexture = _rt;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -50;
            _cam.enabled = false;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;

            void Lit(string name, Vector3 pos, Color c, float intensity, float range)
            {
                var go = new GameObject(name);
                go.transform.SetParent(_rig, false);
                go.transform.localPosition = pos;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = c;
                l.intensity = intensity;
                l.range = range;
                l.cullingMask = 1 << Layer;
                l.shadows = LightShadows.None;
            }
            Lit("Key", new Vector3(-1.2f, 1.6f, -1.4f), new Color(1f, 0.82f, 0.62f), 3.2f, 12f);
            Lit("Fill", new Vector3(1.4f, 0.4f, -0.8f), new Color(0.45f, 0.85f, 0.9f), 1.6f, 12f);
            Lit("Rim", new Vector3(0.2f, 0.8f, 1.6f), new Color(0.7f, 0.35f, 0.4f), 1.2f, 10f);

            var stage = new GameObject("Stage");
            stage.transform.SetParent(_rig, false);
            _stage = stage.transform;
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(_stage, false);
            _pivot = pivot.transform;
            SetLayer(root);
        }

        void BuildPreview()
        {
            ClearPreview();
            ResetView();
            var s = XenoarchRuntime.Session;
            if (s == null || _stack == null) return;
            s.Items.TryGetItem(_stack.itemId, out var def);

            GameObject inst = null;
            if (def != null && def.worldPrefab != null)
                inst = Instantiate(def.worldPrefab);
            if (inst == null && def != null)
            {
                var cat = HeldToolCatalog.Load();
                var prefab = cat != null ? cat.PrefabFor(def.Id) : null;
                if (prefab != null) inst = Instantiate(prefab);
                else if (HeldToolMeshFactory.IsHeldTool(def.Id))
                    inst = HeldToolMeshFactory.Build(def.Id, HeldToolMeshFactory.MakeRuntimeMaterials());
            }
            if (inst == null && def is ArtifactDefinition art)
                inst = BuildShape(art);
            if (inst == null && def != null)
                inst = BuildStandIn(def);
            if (inst == null)
                inst = BuildIconQuad(def);

            if (inst == null) return;
            inst.transform.SetParent(_pivot, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            SetLayer(inst);
            // Fit in view: scale so the longest side is ~0.9 units, centre on the pivot.
            var rends = inst.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var size = Mathf.Max(0.05f, b.size.magnitude);
                float scale = 0.95f / size;
                inst.transform.localScale = inst.transform.localScale * scale;
                inst.transform.localPosition = -(_pivot.InverseTransformPoint(b.center) * scale);
            }
            _preview = inst;
            ApplyCamera();
        }

        GameObject BuildShape(ArtifactDefinition d)
        {
            var go = new GameObject("ArtifactShape_" + d.shape);
            var mat = PreviewMat(d.tint);
            float s = Mathf.Clamp(d.size, 0.05f, 0.6f);
            GameObject part;
            switch (d.shape)
            {
                case ArtifactShape.Tablet:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.transform.localScale = new Vector3(s * 0.7f, s * 0.08f, s);
                    break;
                case ArtifactShape.Shard:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.transform.localScale = new Vector3(s * 0.35f, s * 0.15f, s);
                    part.transform.localRotation = Quaternion.Euler(12f, 18f, -8f);
                    break;
                case ArtifactShape.Rod:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    part.transform.localScale = new Vector3(s * 0.12f, s * 0.5f, s * 0.12f);
                    break;
                case ArtifactShape.Disc:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    part.transform.localScale = new Vector3(s * 0.5f, s * 0.06f, s * 0.5f);
                    break;
                case ArtifactShape.Box:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.transform.localScale = Vector3.one * s * 0.6f;
                    break;
                case ArtifactShape.Crystal:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.transform.localScale = new Vector3(s * 0.35f, s, s * 0.35f);
                    part.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                    break;
                case ArtifactShape.Ring:
                    part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    part.transform.localScale = new Vector3(s * 0.55f, s * 0.07f, s * 0.55f);
                    break;
                default: // Device
                    part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.transform.localScale = new Vector3(s * 0.5f, s * 0.25f, s * 0.7f);
                    break;
            }
            part.transform.SetParent(go.transform, false);
            foreach (var col in part.GetComponentsInChildren<Collider>(true))
                Destroy(col);
            var rend = part.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------ procedural stand-ins by kind
        // Items with no mesh (materials, valuables, components, consumables) get a simple model that reads as the item.
        static Color StandInTint(string id)
        {
            Color C(float r, float g, float b) => new Color(r, g, b, 1f);
            if (id.Contains("copper")) return C(0.85f, 0.45f, 0.25f);
            if (id.Contains("gold") || id.Contains("auriferous")) return C(1f, 0.78f, 0.25f);
            if (id.Contains("platin")) return C(0.8f, 0.82f, 0.88f);
            if (id.Contains("nickel")) return C(0.62f, 0.66f, 0.62f);
            if (id.Contains("iridium")) return C(0.58f, 0.6f, 0.78f);
            if (id.Contains("meteoric") || id.Contains("iron")) return C(0.46f, 0.32f, 0.27f);
            if (id.Contains("silver")) return C(0.86f, 0.88f, 0.92f);
            if (id.Contains("alloy")) return C(0.6f, 0.66f, 0.72f);
            if (id.Contains("quartz")) return C(0.92f, 0.86f, 0.62f);
            if (id.Contains("methane")) return C(0.55f, 0.86f, 0.96f);
            if (id.Contains("ice") || id.Contains("frost")) return C(0.62f, 0.9f, 1f);
            if (id.Contains("salt")) return C(0.92f, 0.9f, 0.86f);
            if (id.Contains("regolith")) return C(0.55f, 0.42f, 0.35f);
            if (id.Contains("sulfide")) return C(0.5f, 0.62f, 0.5f);
            if (id.Contains("helium")) return C(0.78f, 0.92f, 0.96f);
            if (id.Contains("fuel")) return C(0.95f, 0.5f, 0.2f);
            if (id.Contains("reagent")) return C(0.4f, 0.9f, 0.5f);
            if (id.Contains("starstone")) return C(0.72f, 0.5f, 1f);
            if (id.Contains("vael")) return C(0.3f, 0.8f, 0.8f);
            if (id.Contains("geode")) return C(0.66f, 0.4f, 0.82f);
            if (id.Contains("circuit")) return C(0.2f, 0.62f, 0.4f);
            return C(0.55f, 0.65f, 0.7f);
        }

        static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            var p = GameObject.CreatePrimitive(type);
            foreach (var col in p.GetComponentsInChildren<Collider>(true)) Destroy(col);
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.transform.localRotation = Quaternion.Euler(euler);
            var r = p.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            return p;
        }

        static Material StandMat(Color c, float metallic, float smooth)
        {
            var m = PreviewMat(c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        GameObject BuildStandIn(ItemDefinition def)
        {
            string id = def.Id.ToLowerInvariant();
            var go = new GameObject("StandIn_" + def.Id);
            var t = go.transform;
            Color tint = StandInTint(id);
            var P = PrimitiveType.Cube; var S = PrimitiveType.Sphere; var Cy = PrimitiveType.Cylinder;
            var dark = StandMat(new Color(0.16f, 0.17f, 0.2f), 0.7f, 0.5f);

            if (id.Contains("frost") || id.Contains("ice"))
            {
                // Frost crystal cluster: tall faceted prisms radiating from a base.
                var m = StandMat(tint, 0.05f, 0.9f);
                Part(t, P, new Vector3(0f, 0.18f, 0f), new Vector3(0.12f, 0.46f, 0.12f), new Vector3(8f, 20f, 4f), m);
                Part(t, P, new Vector3(0.16f, 0.1f, 0.04f), new Vector3(0.09f, 0.34f, 0.09f), new Vector3(10f, 60f, -28f), m);
                Part(t, P, new Vector3(-0.15f, 0.09f, 0.03f), new Vector3(0.1f, 0.3f, 0.1f), new Vector3(-6f, 110f, 30f), m);
                Part(t, P, new Vector3(0.03f, 0.07f, -0.15f), new Vector3(0.08f, 0.26f, 0.08f), new Vector3(-32f, 10f, 8f), m);
                Part(t, P, new Vector3(-0.04f, 0.06f, 0.16f), new Vector3(0.08f, 0.24f, 0.08f), new Vector3(30f, 200f, -6f), m);
                Part(t, Cy, new Vector3(0f, -0.02f, 0f), new Vector3(0.42f, 0.03f, 0.42f), Vector3.zero, StandMat(tint * 0.6f, 0.1f, 0.5f));
            }
            else if (id.Contains("ingot"))
            {
                var m = StandMat(tint, 0.95f, 0.65f);
                Part(t, P, new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.14f, 0.24f), Vector3.zero, m);
                Part(t, P, new Vector3(0f, 0.15f, 0f), new Vector3(0.42f, 0.08f, 0.17f), Vector3.zero, m);
            }
            else if (id.Contains("fuel_cell") || id.Contains("helium") || id.Contains("methane") || id.Contains("container") || id.Contains("reagent"))
            {
                var m = StandMat(tint, 0.35f, 0.7f);
                bool sphere = id.Contains("helium");
                if (sphere) Part(t, S, Vector3.zero, Vector3.one * 0.42f, Vector3.zero, m);
                else Part(t, Cy, Vector3.zero, new Vector3(0.22f, 0.26f, 0.22f), Vector3.zero, m);
                Part(t, Cy, new Vector3(0f, sphere ? 0.22f : 0.27f, 0f), new Vector3(0.14f, 0.025f, 0.14f), Vector3.zero, dark);
                if (!sphere) Part(t, Cy, new Vector3(0f, -0.27f, 0f), new Vector3(0.24f, 0.02f, 0.24f), Vector3.zero, dark);
                Part(t, Cy, new Vector3(0f, sphere ? 0.0f : 0.02f, 0f), new Vector3(sphere ? 0.44f : 0.235f, sphere ? 0.025f : 0.05f, sphere ? 0.44f : 0.235f), Vector3.zero, StandMat(Color.Lerp(tint, Color.white, 0.5f), 0.2f, 0.5f));
            }
            else if (id.Contains("coin"))
            {
                Part(t, Cy, Vector3.zero, new Vector3(0.4f, 0.025f, 0.4f), new Vector3(90f, 0f, 0f), StandMat(tint, 0.9f, 0.7f));
                Part(t, Cy, new Vector3(0f, 0f, 0.012f), new Vector3(0.28f, 0.01f, 0.28f), new Vector3(90f, 0f, 0f), StandMat(tint * 0.7f, 0.9f, 0.5f));
            }
            else if (id.Contains("starstone"))
            {
                var m = StandMat(tint, 0.2f, 0.95f);
                Part(t, P, Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), new Vector3(45f, 0f, 35f), m);
                Part(t, P, Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), new Vector3(0f, 45f, 54.7f), m);
            }
            else if (id.Contains("geode"))
            {
                Part(t, S, Vector3.zero, new Vector3(0.44f, 0.4f, 0.42f), Vector3.zero, StandMat(new Color(0.42f, 0.36f, 0.32f), 0f, 0.2f));
                var m = StandMat(tint, 0.1f, 0.9f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f * Mathf.Deg2Rad;
                    Part(t, P, new Vector3(Mathf.Cos(a) * 0.08f, 0.2f, Mathf.Sin(a) * 0.08f), new Vector3(0.05f, 0.12f, 0.05f), new Vector3(Mathf.Sin(a) * 25f, 0f, -Mathf.Cos(a) * 25f), m);
                }
            }
            else if (id.Contains("nugget") || id.Contains("iridium") || id.Contains("meteoric"))
            {
                var m = StandMat(tint, id.Contains("meteoric") ? 0.6f : 0.9f, 0.45f);
                Part(t, S, Vector3.zero, new Vector3(0.34f, 0.24f, 0.28f), new Vector3(10f, 20f, 0f), m);
                Part(t, S, new Vector3(0.12f, -0.03f, 0.05f), new Vector3(0.2f, 0.17f, 0.2f), Vector3.zero, m);
                Part(t, S, new Vector3(-0.1f, 0.06f, -0.06f), new Vector3(0.16f, 0.15f, 0.16f), Vector3.zero, m);
            }
            else if (id.Contains("ore") || id.Contains("quartz") || id.Contains("regolith") || id.Contains("sulfide") || id.Contains("salt"))
            {
                bool pile = id.Contains("regolith") || id.Contains("salt");
                var rock = StandMat(pile ? tint : Color.Lerp(new Color(0.3f, 0.27f, 0.26f), tint, 0.25f), 0f, 0.15f);
                if (pile)
                {
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i * 2.4f; float r = 0.05f + 0.16f * Mathf.Sqrt(i / 14f);
                        Part(t, S, new Vector3(Mathf.Cos(a) * r, 0.04f + 0.16f * (1f - i / 14f), Mathf.Sin(a) * r), Vector3.one * (0.09f + 0.03f * ((i * 7) % 3)), Vector3.zero, rock);
                    }
                }
                else
                {
                    Part(t, P, Vector3.zero, new Vector3(0.34f, 0.26f, 0.3f), new Vector3(14f, 28f, 8f), rock);
                    Part(t, P, new Vector3(0.12f, -0.04f, 0.06f), new Vector3(0.22f, 0.2f, 0.22f), new Vector3(-8f, 60f, 14f), rock);
                    var fleck = StandMat(tint, id.Contains("quartz") ? 0.1f : 0.85f, 0.8f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad;
                        Part(t, P, new Vector3(Mathf.Cos(a) * 0.13f, 0.12f - 0.05f * (i % 2), Mathf.Sin(a) * 0.12f), Vector3.one * 0.06f, new Vector3(30f * i, 20f * i, 10f), fleck);
                    }
                }
            }
            else if (id.Contains("plate"))
            {
                Part(t, P, Vector3.zero, new Vector3(0.5f, 0.04f, 0.38f), Vector3.zero, StandMat(tint, 0.9f, 0.6f));
                var bolt = dark;
                foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                    Part(t, Cy, new Vector3(sx * 0.2f, 0.03f, sz * 0.15f), new Vector3(0.04f, 0.012f, 0.04f), Vector3.zero, bolt);
            }
            else if (id.Contains("circuit"))
            {
                Part(t, P, Vector3.zero, new Vector3(0.5f, 0.025f, 0.36f), Vector3.zero, StandMat(tint, 0.1f, 0.6f));
                Part(t, P, new Vector3(-0.08f, 0.03f, 0.04f), new Vector3(0.14f, 0.035f, 0.14f), Vector3.zero, dark);
                Part(t, P, new Vector3(0.12f, 0.025f, -0.06f), new Vector3(0.08f, 0.025f, 0.06f), Vector3.zero, dark);
                Part(t, P, new Vector3(0.14f, 0.025f, 0.09f), new Vector3(0.05f, 0.02f, 0.05f), Vector3.zero, StandMat(new Color(0.9f, 0.75f, 0.3f), 0.9f, 0.6f));
            }
            else if (id.Contains("sensor"))
            {
                Part(t, P, new Vector3(0f, -0.06f, 0f), new Vector3(0.36f, 0.14f, 0.26f), Vector3.zero, dark);
                Part(t, Cy, new Vector3(0f, 0.08f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Vector3.zero, dark);
                Part(t, Cy, new Vector3(0f, 0.2f, 0f), new Vector3(0.36f, 0.02f, 0.36f), new Vector3(-20f, 0f, 0f), StandMat(tint, 0.8f, 0.7f));
                Part(t, S, new Vector3(0f, 0.17f, 0.04f), Vector3.one * 0.05f, Vector3.zero, StandMat(new Color(0.4f, 0.9f, 0.9f), 0f, 0.9f));
            }
            else if (id.Contains("wiring"))
            {
                var m = StandMat(new Color(0.85f, 0.45f, 0.25f), 0.9f, 0.5f);
                for (int i = 0; i < 4; i++)
                    Part(t, Cy, new Vector3(0f, -0.1f + i * 0.065f, 0f), new Vector3(0.36f, 0.02f, 0.36f), Vector3.zero, i % 2 == 0 ? m : StandMat(new Color(0.15f, 0.15f, 0.18f), 0.2f, 0.4f));
                Part(t, Cy, new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0.3f, 0.1f), Vector3.zero, dark);
            }
            else if (id.Contains("flood"))
            {
                Part(t, P, new Vector3(-0.08f, 0f, 0f), new Vector3(0.3f, 0.3f, 0.3f), Vector3.zero, dark);
                Part(t, Cy, new Vector3(0.12f, 0f, 0f), new Vector3(0.28f, 0.04f, 0.28f), new Vector3(0f, 0f, 90f), StandMat(new Color(1f, 0.95f, 0.75f), 0f, 0.95f));
                Part(t, P, new Vector3(-0.08f, -0.2f, 0f), new Vector3(0.1f, 0.12f, 0.2f), Vector3.zero, dark);
            }
            else if (id.Contains("grasshopper") || id.Contains("vehicle"))
            {
                Part(t, P, new Vector3(0f, 0.06f, 0f), new Vector3(0.7f, 0.18f, 0.4f), Vector3.zero, StandMat(new Color(0.72f, 0.72f, 0.68f), 0.5f, 0.5f));
                Part(t, P, new Vector3(0.1f, 0.2f, 0f), new Vector3(0.28f, 0.13f, 0.3f), Vector3.zero, StandMat(new Color(0.25f, 0.5f, 0.6f), 0.2f, 0.9f));
                foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                    Part(t, Cy, new Vector3(sx * 0.24f, -0.03f, sz * 0.24f), new Vector3(0.16f, 0.04f, 0.16f), new Vector3(90f, 0f, 0f), dark);
            }
            else
            {
                // Generic fragment: a small faceted shard.
                var m = StandMat(tint, 0.3f, 0.6f);
                Part(t, P, Vector3.zero, new Vector3(0.18f, 0.4f, 0.14f), new Vector3(14f, 30f, 20f), m);
                Part(t, P, new Vector3(0.1f, -0.06f, 0.02f), new Vector3(0.12f, 0.26f, 0.1f), new Vector3(-10f, 70f, -25f), m);
            }
            return go;
        }

        GameObject BuildIconQuad(ItemDefinition d)
        {
            var go = new GameObject("IconQuad");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = Vector3.one * 0.7f;
            foreach (var col in quad.GetComponentsInChildren<Collider>(true)) Destroy(col);
            var rend = quad.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = PreviewMat(Color.white);
                if (d != null && d.icon != null && d.icon.texture != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", d.icon.texture);
                    else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", d.icon.texture);
                    mat.SetFloat("_Surface", 1f);
                }
                rend.sharedMaterial = mat;
            }
            return go;
        }

        static Material PreviewMat(Color tint)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var m = new Material(lit) { hideFlags = HideFlags.HideAndDontSave, name = "ItemPreviewMat" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint); else m.color = tint;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.25f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.4f);
            return m;
        }

        void ClearPreview()
        {
            if (_preview != null) { Destroy(_preview); _preview = null; }
            if (_pivot != null)
                for (int i = _pivot.childCount - 1; i >= 0; i--) Destroy(_pivot.GetChild(i).gameObject);
        }

        void ResetView()
        {
            _yaw = 25f; _pitch = -12f; _dist = _baseDist; _panX = 0f; _panY = 0f;
            ApplyCamera();
        }

        void ApplyCamera()
        {
            if (_cam == null || _stage == null) return;
            _stage.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _cam.transform.localPosition = new Vector3(_panX, _panY, -_dist);
            _cam.transform.localRotation = Quaternion.identity;
            _cam.transform.LookAt(_rig.TransformPoint(new Vector3(_panX, _panY, 0f)));
        }

        void SetLayer(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = Layer;
        }

        void BuildUi()
        {
            _root.Clear();
            var s = XenoarchRuntime.Session;
            if (s == null || _stack == null) return;
            s.Items.TryGetItem(_stack.itemId, out var def);
            var artInst = !string.IsNullOrEmpty(_stack.instanceId) ? s.Artifacts.Get(_stack.instanceId) : null;
            var artDef = def as ArtifactDefinition;

            // Docked beside the grid: no full-screen dim, so the other items stay clickable (right-click retargets).
            var modal = new VisualElement(); modal.AddToClassList("xa-inspect");
            modal.AddToClassList(_dockRight ? "xa-inspect--right" : "xa-inspect--left");
            _root.Add(modal);

            _view = new VisualElement(); _view.AddToClassList("xa-inspect__view");
            _view.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_rt));
            _view.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            _view.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            _view.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            _view.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            modal.Add(_view);
            var cap = new Label("3D VIEWPORT  ·  INTERACTIVE"); cap.AddToClassList("xa-inspect__cap"); _view.Add(cap);
            var hints = new VisualElement(); hints.AddToClassList("xa-inspect__hints");
            Key(hints, "LMB", "Drag to rotate");
            Key(hints, "Wheel", "Zoom");
            Key(hints, "RMB", "Pan");
            Key(hints, "R", "Reset view");
            _view.Add(hints);
            WireView(_view);

            var side = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden }; side.AddToClassList("xa-inspect__side"); modal.Add(side);
            modal.RegisterCallback<GeometryChangedEvent>(_ => ClampOnScreen(modal));

            string heading = artInst != null ? s.Artifacts.DisplayName(artInst) : (def != null ? def.DisplayNameOrId : _stack.itemId);
            var kicker = new Label(artDef != null ? "ARTIFACT" : ResourceGroups.GroupLabel(def).ToUpperInvariant());
            kicker.AddToClassList("xa-inspect__kicker"); side.Add(kicker);
            var title = new Label(heading); title.AddToClassList("xa-inspect__title"); side.Add(title);

            var chips = new VisualElement(); chips.AddToClassList("xa-inspect__chips"); side.Add(chips);
            if (artDef != null)
            {
                Chip(chips, artInst != null && artInst.Identified ? artDef.rarity.ToString() : "Unknown");
                Chip(chips, artInst != null && artInst.Identified ? artDef.artifactCategory.ToString() : "Unidentified");
                if (artInst != null && artInst.Identified) Chip(chips, "Identified");
            }
            else Chip(chips, ResourceGroups.GroupLabel(def));

            if (artDef != null && artDef.HasTranslation)
            {
                var box = new VisualElement(); box.AddToClassList("xa-inspect__insc"); side.Add(box);
                var ih = new Label("INSCRIPTION  ·  PARTIAL TRANSLATION"); ih.AddToClassList("xa-term__h"); box.Add(ih);
                int revealed = 0, total = artDef.translation != null ? artDef.translation.Count : 0;
                if (artInst != null)
                {
                    foreach (var seg in s.Artifacts.RevealedText(artInst))
                    {
                        revealed++;
                        var p = new Label("“" + seg.text + "”"); p.AddToClassList("xa-inspect__quote"); box.Add(p);
                    }
                }
                int sealedN = Mathf.Max(0, total - revealed);
                if (sealedN > 0)
                {
                    var g = new Label($"[ {sealedN} segment{(sealedN == 1 ? "" : "s")} still sealed ]");
                    g.AddToClassList("xa-term__dim"); box.Add(g);
                }
                float t01 = artInst != null ? artInst.translation : 0f;
                var barrow = new VisualElement(); barrow.style.flexDirection = FlexDirection.Row; barrow.style.alignItems = Align.Center; barrow.style.marginTop = 8;
                var pct = new Label($"{t01 * 100f:0}%"); pct.AddToClassList("xa-term__dim"); pct.style.width = 48; barrow.Add(pct);
                var bar = new VisualElement(); bar.AddToClassList("xa-term__bar"); bar.style.flexGrow = 1;
                var fill = new VisualElement(); fill.AddToClassList("xa-term__bar-fill"); fill.style.width = Length.Percent(Mathf.Clamp01(t01) * 100f);
                fill.style.backgroundColor = XenoarchPalette.Highlight;
                bar.Add(fill); barrow.Add(bar);
                var frac = new Label($"{revealed} / {Mathf.Max(1, total)}"); frac.AddToClassList("xa-term__dim"); frac.style.marginLeft = 8; barrow.Add(frac);
                box.Add(barrow);
                var clue = new Label("Clue: rotate and zoom to search the surface for marks the lab has not transcribed.");
                clue.AddToClassList("xa-term__dim"); clue.style.marginTop = 8; box.Add(clue);
            }

            var sh = new Label("SPECIFICATIONS"); sh.AddToClassList("xa-term__h"); sh.style.marginTop = 8; side.Add(sh);
            var spec = new VisualElement(); spec.AddToClassList("xa-inspect__spec"); side.Add(spec);
            void Row(string lab, string val)
            {
                var r = new VisualElement(); r.AddToClassList("xa-inspect__row");
                var a = new Label(lab); a.AddToClassList("xa-inspect__lab"); r.Add(a);
                var b = new Label(val ?? "—"); b.AddToClassList("xa-inspect__val"); r.Add(b);
                spec.Add(r);
            }
            Row("Name", heading);
            Row("Category", artDef != null
                ? (artInst != null && artInst.Identified ? artDef.artifactCategory.ToString() : "Unidentified")
                : ResourceGroups.GroupLabel(def));
            Row("Rarity", artDef != null && artInst != null && artInst.Identified ? artDef.rarity.ToString() : "—");
            float mass = def != null ? def.mass : 0f;
            Row("Weight", _stack.count > 1 ? $"{mass:0.##} kg each · {mass * _stack.count:0.##} kg" : $"{mass:0.##} kg");
            Row("Material type", artDef != null && (artInst == null || artInst.Done(ResearchOp.MaterialAnalysis))
                ? ResourceGroups.MaterialType(def)
                : (artDef != null ? "Unknown" : ResourceGroups.MaterialType(def)));
            Row("Amount", _stack.count.ToString());
            float value = artInst != null ? s.Artifacts.Value(artInst) : (def != null ? def.baseValue * _stack.count : 0f);
            Row("Value", $"{value:0} cr");
            Row("Origin", ResourceGroups.OriginLabel(_stack, artInst));
            Row("Condition", artInst != null ? $"{artInst.condition * 100f:0}% · {(artInst.condition >= 0.8f ? "Intact" : artInst.condition >= 0.45f ? "Worn" : "Damaged")}" : "—");
            Row("Age estimate", artDef != null && (artInst == null || artInst.Done(ResearchOp.AgeEstimation)) ? artDef.ageEstimate : (artDef != null ? "Unknown" : "—"));
            if (def is DMI.Xenoarch.Materials.MaterialDefinition)
                Row("Grade / purity", ResourceGroups.GradeLabel(def, _stack));

            var notes = new VisualElement(); notes.style.marginTop = 12; side.Add(notes);
            var nh = new Label("NOTES"); nh.AddToClassList("xa-term__h"); notes.Add(nh);
            string note = def != null ? def.description : "";
            if (artDef != null && artInst != null && artInst.Identified && !string.IsNullOrEmpty(artDef.researchedDescription))
                note = artDef.researchedDescription;
            var nt = new Label(string.IsNullOrEmpty(note) ? "No additional notes." : note);
            nt.AddToClassList("xa-term__dim"); nt.style.whiteSpace = WhiteSpace.Normal; notes.Add(nt);

            var actions = new VisualElement(); actions.AddToClassList("xa-inspect__actions"); side.Add(actions);
            var close = new Button(Hide) { text = "Close" }; close.AddToClassList("xa-term__btn"); close.AddToClassList("xa-term__btn--primary"); actions.Add(close);
            var move = new Button(() =>
            {
                if (TryMoveToHold()) { Toast.Show("Moved to ship storage."); Hide(); }
            }) { text = "Move to Hold" };
            move.AddToClassList("xa-term__btn"); actions.Add(move);
            if (artDef != null)
            {
                var lab = new Button(() =>
                {
                    Toast.Show("Use the research lab aboard the ship to study this find. It stays in your backpack until then.");
                }) { text = "Send to Lab" };
                lab.AddToClassList("xa-term__btn"); actions.Add(lab);
            }
        }

        bool TryMoveToHold()
        {
            var s = XenoarchRuntime.Session;
            if (s == null || _stack == null) return false;
            InventoryModel from = null; int index = -1;
            if (FindStack(s.Inventory, _stack, out index)) from = s.Inventory;
            else
            {
                foreach (var kv in s.Holds)
                    if (FindStack(kv.Value, _stack, out index)) { from = kv.Value; break; }
            }
            if (from == null || index < 0) { Toast.Show("Couldn't find that item."); return false; }
            if (from != s.Inventory) { Toast.Show("Already in ship storage."); return false; }
            s.Items.TryGetItem(_stack.itemId, out var d);
            InventoryModel dest = null;
            foreach (var role in new[] { StorageRole.General, StorageRole.Equipment, StorageRole.Artifacts })
            {
                if (!s.Holds.TryGetValue(role, out var h)) continue;
                if (d != null && !ShipHold.Accepts(role, d.category)) continue;
                if (h.FreeSlots <= 0 && string.IsNullOrEmpty(_stack.instanceId)) continue;
                dest = h; break;
            }
            if (dest == null) { Toast.Show("Ship storage is full."); return false; }
            var st = from.Slots[index].Clone();
            return ShipHold.Transfer(from, dest, st) > 0;
        }

        static bool FindStack(InventoryModel inv, ItemStack want, out int index)
        {
            index = -1;
            if (inv == null || want == null) return false;
            if (!string.IsNullOrEmpty(want.instanceId))
            {
                for (int i = 0; i < inv.Slots.Count; i++)
                    if (inv.Slots[i].instanceId == want.instanceId) { index = i; return true; }
                return false;
            }
            for (int i = 0; i < inv.Slots.Count; i++)
            {
                var s = inv.Slots[i];
                if (!s.IsEmpty && s.itemId == want.itemId && s.SameMeta(want) && s.count == want.count) { index = i; return true; }
            }
            return false;
        }

        void WireView(VisualElement view)
        {
            view.RegisterCallback<WheelEvent>(e =>
            {
                _dist = Mathf.Clamp(_dist * (e.delta.y > 0 ? 1.08f : 0.92f), 0.45f, 5f);
                ApplyCamera();
                e.StopPropagation();
            });
            view.RegisterCallback<PointerDownEvent>(e =>
            {
                view.CapturePointer(e.pointerId);
                _lastPtr = (Vector2)e.position;
                if (e.button == 0) _rotating = true;
                else if (e.button == 1) _panning = true;
                e.StopPropagation();
            });
            view.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_rotating && !_panning) return;
                var dlt = (Vector2)e.position - _lastPtr;
                _lastPtr = (Vector2)e.position;
                if (_rotating) { _yaw -= dlt.x * 0.35f; _pitch = Mathf.Clamp(_pitch - dlt.y * 0.3f, -80f, 80f); }
                if (_panning) { _panX -= dlt.x * 0.002f * _dist; _panY += dlt.y * 0.002f * _dist; }
                ApplyCamera();
                e.StopPropagation();
            });
            view.RegisterCallback<PointerUpEvent>(e =>
            {
                _rotating = _panning = false;
                if (view.HasPointerCapture(e.pointerId)) view.ReleasePointer(e.pointerId);
            });
            view.RegisterCallback<PointerCaptureOutEvent>(_ => { _rotating = _panning = false; });
        }

        static void Key(VisualElement parent, string key, string label)
        {
            var row = new VisualElement(); row.AddToClassList("xa-inspect__hint");
            var k = new Label(key); k.AddToClassList("xa-key"); k.AddToClassList("xa-m-key"); row.Add(k);
            var l = new Label(label); l.AddToClassList("xa-term__dim"); row.Add(l);
            parent.Add(row);
        }

        static void Chip(VisualElement parent, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var c = new Label(text.ToUpperInvariant()); c.AddToClassList("xa-inspect__chip"); parent.Add(c);
        }
    }
}
