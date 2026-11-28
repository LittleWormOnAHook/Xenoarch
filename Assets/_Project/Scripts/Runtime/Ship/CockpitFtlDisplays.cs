using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Live NAV/FTL and SYSTEMS/SCAN readouts on the Constellation cockpit's Screen_D and Screen_B. Runtime only:
    /// finds the vendor meshes, instances their materials onto RenderTextures driven by UITK, and never writes the
    /// Creepy Cat assets or the open ship scene. Mouse look stays on in every seat. Hold Alt to free the cursor and
    /// click a gauge (or a region) to zoom in, or left-click while looking at one (reticle). Esc, right-click or a second click zooms out.
    /// Screen mapping is derived from each face's mesh UVs. Clicks use a mesh ray, not convex-collider textureCoord.
    /// </summary>
    public sealed class CockpitFtlDisplays : MonoBehaviour
    {
        public const int TexWidth = 1280;
        public const int TexHeight = 960;
        const float ZoomSeconds = 0.45f;
        const float GaugeZoomDist = 0.17f;
        const float RegionZoomDist = 0.3f;
        const float GaugeZoomFov = 28f;
        const float RegionZoomFov = 38f;
        const int IgnoreRaycastLayer = 2;
        const float LyPerAu = 1f / 63241f;
        const float SecondsPerYear = 31557600f;
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        public static CockpitFtlDisplays Instance { get; private set; }
        /// <summary>True while a gauge zoom is in progress or held, so FTL must not overwrite the camera FOV.</summary>
        public static bool ZoomBusy => Instance != null && Instance._zoom.kind != ZoomKind.None;

        public static bool TryHandleEscape()
        {
            if (Instance == null || Instance._zoom.kind == ZoomKind.None) return false;
            Instance.StartZoom(ZoomKind.Out, default, 0f, 0f);
            return true;
        }

        public static void CancelZoom(bool snap = false)
        {
            if (Instance != null) Instance.CancelZoomInternal(snap);
        }

        public static void Ensure()
        {
            if (Instance != null) return;
            var host = new GameObject("[Cockpit FTL Displays]");
            SceneManager.MoveGameObjectToScene(host, SceneManager.GetActiveScene());
            host.AddComponent<CockpitFtlDisplays>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (LooksLikeShip(SceneManager.GetActiveScene().name)) Ensure();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (LooksLikeShip(scene.name)) Ensure();
        }

        static bool LooksLikeShip(string name) =>
            name == SceneNames.ShipStarter || (!string.IsNullOrEmpty(name) && name.IndexOf("Ship", System.StringComparison.OrdinalIgnoreCase) >= 0);

        enum ZoomKind { None, In, Out, Hold }

        struct ZoomState
        {
            public ZoomKind kind;
            public Vector3 fromPos, toPos, restPos;
            public Quaternion fromRot, toRot, restRot;
            public float fromFov, toFov, restFov;
            public float t;
            public string regionId;
            public Camera cam;
        }

        sealed class ScreenFace
        {
            public bool left;
            public Transform transform;
            public MeshRenderer renderer;
            public MeshFilter filter;
            public MeshCollider collider;
            public Material original;
            public int originalLayer;
            public Material instance;
            public RenderTexture rt;
            public PanelSettings panelSettings;
            public UIDocument doc;
            public VisualElement root;
            public bool flipU, flipV;
            public readonly List<HitRegion> regions = new List<HitRegion>();
            public Labels labels = new Labels();
            public WaveElement waveA, waveB;
            public SpectrumElement spectrum;
            public GaugeElement g0, g1, g2;
        }

        sealed class HitRegion
        {
            public string id;
            public VisualElement element;
            public bool isGauge;
        }

        sealed class Labels
        {
            public Label status, origin, dest, speed, speedSub, remain, remainSub, eta, etaSub, progress;
            public Label fuel, fuelSub, range, rangeSub, core, coreSub, energy, energySub;
            public Label stage, window, heading, swirl, coords;
        }

        ScreenFace _left, _right;
        ZoomState _zoom;
        float _nextIdleRepaint;
        bool _bound;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            if (!TryBindScreens())
            {
                Debug.LogWarning("[Xenoarch] Cockpit FTL displays: Screen_D / Screen_B not found in this scene.");
                return;
            }
            if (TryBuildPanels())
            {
                ApplyLive(true);
                _bound = true;
            }
            else StartCoroutine(BuildWhenReady());
        }

        IEnumerator BuildWhenReady()
        {
            for (int i = 0; i < 8; i++)
            {
                yield return null;
                if (TryBuildPanels())
                {
                    ApplyLive(true);
                    _bound = true;
                    yield break;
                }
            }
            Debug.LogWarning("[Xenoarch] Cockpit FTL displays: UITK panel root was not ready.");
        }

        bool TryBuildPanels()
        {
            if (_left?.doc == null || _right?.doc == null) return false;
            if (_left.doc.rootVisualElement == null || _right.doc.rootVisualElement == null) return false;
            if (_left.root != null) return true;
            BuildPanel(_left);
            BuildPanel(_right);
            return _left.root != null && _right.root != null;
        }

        void OnDestroy()
        {
            CancelZoomInternal(true);
            RestoreFace(_left);
            RestoreFace(_right);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_bound) return;
            bool ftl = ShipFtlSequence.Active != null && ShipFtlSequence.Active.Running;
            if (ftl || Time.unscaledTime >= _nextIdleRepaint)
            {
                ApplyLive(false);
                if (!ftl) _nextIdleRepaint = Time.unscaledTime + 0.2f;
            }
            TickZoom();
            TickCursorInput();
        }

        void TickCursorInput()
        {
            if (CockpitSeat.Occupied == null || CockpitSeat.Occupied.Transitioning) return;
            if (UIState.BlocksGameplay) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && _zoom.kind != ZoomKind.None)
            {
                StartZoom(ZoomKind.Out, default, 0f, 0f);
                return;
            }
            if (kb != null && kb.escapeKey.wasPressedThisFrame && _zoom.kind != ZoomKind.None)
            {
                StartZoom(ZoomKind.Out, default, 0f, 0f);
                return;
            }
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            var cam = CameraOf(CockpitSeat.Occupied);
            if (cam == null) return;
            // Locked cursor: click the gauge under the reticle. Free cursor (hold Alt): aim with the pointer.
            Vector2 sp = UIState.ConsoleCursor
                ? mouse.position.ReadValue()
                : new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            var ray = cam.ScreenPointToRay(sp);
            // Convex trigger MeshColliders have no valid textureCoord (they also used to block the helm
            // walk when solid). Intersect the actual mesh so UV and the hit point stay exact.
            ScreenFace face = null;
            Vector2 uv = default;
            Vector3 hitPoint = default;
            float best = float.MaxValue;
            if (TryMeshUv(_left, ray, 4.5f, out var uvL, out var pL, out var dL) && dL < best)
            { best = dL; face = _left; uv = uvL; hitPoint = pL; }
            if (TryMeshUv(_right, ray, 4.5f, out var uvR, out var pR, out var dR) && dR < best)
            { best = dR; face = _right; uv = uvR; hitPoint = pR; }
            if (face == null)
            {
                if (_zoom.kind == ZoomKind.Hold || _zoom.kind == ZoomKind.In) StartZoom(ZoomKind.Out, default, 0f, 0f);
                Debug.Log($"[CockpitClick] hit=none uv=- element=- sp={sp} cursor={UIState.ConsoleCursor}");
                return;
            }
            var region = RegionAt(face, uv);
            string id = region != null ? region.id : (face.left ? "left-region" : "right-region");
            Debug.Log($"[CockpitClick] hit={face.transform.name} uv={uv.x:F3},{uv.y:F3} element={id} flipU={face.flipU} flipV={face.flipV} cursor={UIState.ConsoleCursor}");
            if ((_zoom.kind == ZoomKind.Hold || _zoom.kind == ZoomKind.In) && _zoom.regionId == id)
            {
                StartZoom(ZoomKind.Out, default, 0f, 0f);
                return;
            }
            float dist = region != null && region.isGauge ? GaugeZoomDist : RegionZoomDist;
            float fov = region != null && region.isGauge ? GaugeZoomFov : RegionZoomFov;
            _zoom.regionId = id;
            StartZoom(ZoomKind.In, hitPoint, dist, fov);
        }

        static Camera CameraOf(CockpitSeat seat)
        {
            var rig = PlayerRigLocator.Find();
            return rig != null ? rig.ViewCamera : Camera.main;
        }

        HitRegion RegionAt(ScreenFace face, Vector2 uv)
        {
            // Mesh UV, then the same U/V flip applied to the material, then y-up texture to y-down UITK.
            float ru = face.flipU ? 1f - uv.x : uv.x;
            float rv = face.flipV ? 1f - uv.y : uv.y;
            float px = ru * TexWidth;
            float py = (1f - rv) * TexHeight;
            var pt = new Vector2(px, py);
            foreach (var r in face.regions)
            {
                if (r.element == null) continue;
                if (r.element.worldBound.Contains(pt)) return r;
            }
            return null;
        }

        void StartZoom(ZoomKind kind, Vector3 worldPoint, float dist, float fov)
        {
            var cam = CameraOf(CockpitSeat.Occupied);
            if (cam == null) return;
            if (_zoom.cam == null || _zoom.kind == ZoomKind.None)
            {
                _zoom.restPos = cam.transform.position;
                _zoom.restRot = cam.transform.rotation;
                _zoom.restFov = cam.fieldOfView;
                _zoom.cam = cam;
            }
            if (kind == ZoomKind.Out)
            {
                _zoom.fromPos = cam.transform.position;
                _zoom.fromRot = cam.transform.rotation;
                _zoom.fromFov = cam.fieldOfView;
                _zoom.toPos = _zoom.restPos;
                _zoom.toRot = _zoom.restRot;
                _zoom.toFov = _zoom.restFov;
                _zoom.kind = ZoomKind.Out;
                _zoom.t = 0f;
                _zoom.regionId = null;
                return;
            }
            Vector3 normal = (_left != null && Vector3.Distance(worldPoint, _left.renderer.bounds.center) < Vector3.Distance(worldPoint, _right != null ? _right.renderer.bounds.center : worldPoint + Vector3.one * 100f)
                ? _left.transform : _right.transform).forward;
            // Face the camera toward the screen; many cockpit screens point their forward through the glass (or the opposite).
            Vector3 toCam = cam.transform.position - worldPoint;
            if (Vector3.Dot(normal, toCam) < 0f) normal = -normal;
            Vector3 dest = worldPoint + normal * dist;
            _zoom.fromPos = cam.transform.position;
            _zoom.fromRot = cam.transform.rotation;
            _zoom.fromFov = cam.fieldOfView;
            _zoom.toPos = dest;
            _zoom.toRot = Quaternion.LookRotation((worldPoint - dest).normalized, _zoom.restRot * Vector3.up);
            _zoom.toFov = fov;
            _zoom.kind = ZoomKind.In;
            _zoom.t = 0f;
        }

        void TickZoom()
        {
            if (_zoom.kind != ZoomKind.In && _zoom.kind != ZoomKind.Out) return;
            var cam = _zoom.cam;
            if (cam == null) { _zoom.kind = ZoomKind.None; return; }
            _zoom.t += Time.unscaledDeltaTime / ZoomSeconds;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_zoom.t));
            cam.transform.position = Vector3.Lerp(_zoom.fromPos, _zoom.toPos, k);
            cam.transform.rotation = Quaternion.Slerp(_zoom.fromRot, _zoom.toRot, k);
            cam.fieldOfView = Mathf.Lerp(_zoom.fromFov, _zoom.toFov, k);
            if (_zoom.t < 1f) return;
            if (_zoom.kind == ZoomKind.Out)
            {
                cam.transform.position = _zoom.restPos;
                cam.transform.rotation = _zoom.restRot;
                cam.fieldOfView = _zoom.restFov;
                _zoom.kind = ZoomKind.None;
                _zoom.cam = null;
            }
            else _zoom.kind = ZoomKind.Hold;
        }

        void CancelZoomInternal(bool snap)
        {
            if (_zoom.kind == ZoomKind.None) return;
            var cam = _zoom.cam;
            if (cam != null)
            {
                if (snap)
                {
                    cam.transform.position = _zoom.restPos;
                    cam.transform.rotation = _zoom.restRot;
                    cam.fieldOfView = _zoom.restFov;
                    _zoom.kind = ZoomKind.None;
                    _zoom.cam = null;
                    return;
                }
                StartZoom(ZoomKind.Out, default, 0f, 0f);
            }
            else { _zoom.kind = ZoomKind.None; }
        }

        bool TryBindScreens()
        {
            MeshRenderer left = null, right = null;
            float best = float.MaxValue;
            var seats = FindObjectsByType<CockpitSeat>(FindObjectsInactive.Exclude);
            var helms = FindObjectsByType<CockpitHelm>(FindObjectsInactive.Exclude);
            Vector3 anchor = seats.Length > 0 ? seats[0].transform.position : helms.Length > 0 ? helms[0].transform.position : Vector3.zero;
            var all = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
            // Pair Screen_D + Screen_B that share a parent and sit near the helm.
            var dList = new List<MeshRenderer>();
            var bList = new List<MeshRenderer>();
            foreach (var r in all)
            {
                if (r == null) continue;
                if (r.name == "Screen_D") dList.Add(r);
                else if (r.name == "Screen_B") bList.Add(r);
            }
            foreach (var d in dList)
            foreach (var b in bList)
            {
                if (d.transform.parent != b.transform.parent) continue;
                float s = (d.transform.position - anchor).sqrMagnitude + (b.transform.position - anchor).sqrMagnitude;
                if (s >= best) continue;
                best = s;
                left = d;
                right = b;
            }
            // Local X of Screen_D is the left screen on the Constellation helm (Computer_01 / RD-X2).
            if (left == null || right == null) return false;
            if (left.transform.localPosition.x > right.transform.localPosition.x)
            { var tmp = left; left = right; right = tmp; }
            _left = BindFace(left, true);
            _right = BindFace(right, false);
            return _left != null && _right != null;
        }

        ScreenFace BindFace(MeshRenderer r, bool left)
        {
            var face = new ScreenFace { left = left, renderer = r, transform = r.transform, filter = r.GetComponent<MeshFilter>(), original = r.sharedMaterial };
            if (face.filter == null || face.filter.sharedMesh == null) return null;
            var col = r.GetComponent<MeshCollider>();
            if (col == null) col = r.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = face.filter.sharedMesh;
            // Trigger: a solid MeshCollider on Screen_D / Screen_B blocked the walk to the helm.
            // Non-convex meshes cannot be triggers, so cook a convex hull (these screens are thin panels).
            col.convex = true;
            col.isTrigger = true;
            face.originalLayer = r.gameObject.layer;
            r.gameObject.layer = IgnoreRaycastLayer;
            face.collider = col;

            face.rt = new RenderTexture(TexWidth, TexHeight, 0, RenderTextureFormat.ARGB32) { name = left ? "FTL_NavRT" : "FTL_SysRT", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            face.rt.Create();

            var src = XenoarchUI.LoadPanelSettings();
            face.panelSettings = src != null ? Instantiate(src) : ScriptableObject.CreateInstance<PanelSettings>();
            face.panelSettings.targetTexture = face.rt;
            face.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            face.panelSettings.scale = 1f;
            face.panelSettings.clearDepthStencil = true;
            face.panelSettings.colorClearValue = new Color(0.02f, 0.055f, 0.12f, 1f);

            var go = new GameObject(left ? "FTL Nav Panel" : "FTL Sys Panel");
            go.transform.SetParent(transform, false);
            face.doc = go.AddComponent<UIDocument>();
            face.doc.panelSettings = face.panelSettings;
            face.doc.sortingOrder = 0;

            face.instance = face.original != null ? new Material(face.original) { name = (face.original.name) + " (FTL RT)" } : new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            AssignTex(face.instance, face.rt);
            if (face.instance.HasProperty(EmissionColorId))
            {
                face.instance.EnableKeyword("_EMISSION");
                face.instance.SetColor(EmissionColorId, new Color(1.4f, 1.6f, 1.8f));
                if (face.instance.HasProperty(EmissionMapId))
                    face.instance.SetTexture(EmissionMapId, face.rt);
            }
            ApplyScreenMapping(face);
            r.sharedMaterial = face.instance;
            return face;
        }

        static void AssignTex(Material m, Texture tex)
        {
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, tex);
            if (m.HasProperty(MainTexId)) m.SetTexture(MainTexId, tex);
        }

        void ApplyScreenMapping(ScreenFace face)
        {
            DeriveUvOrientation(face.filter.sharedMesh, face.transform, ViewerHint(face.transform), out face.flipU, out face.flipV);
            ApplySt(face.instance, BaseMapId, face.flipU, face.flipV);
            if (face.instance.HasProperty(MainTexId)) ApplySt(face.instance, MainTexId, face.flipU, face.flipV);
            if (face.instance.HasProperty(EmissionMapId)) ApplySt(face.instance, EmissionMapId, face.flipU, face.flipV);
            Debug.Log($"[CockpitScreen] {face.transform.name} flipU={face.flipU} flipV={face.flipV}");
        }

        static void ApplySt(Material m, int id, bool flipU, bool flipV)
        {
            m.SetTextureScale(id, new Vector2(flipU ? -1f : 1f, flipV ? -1f : 1f));
            m.SetTextureOffset(id, new Vector2(flipU ? 1f : 0f, flipV ? 1f : 0f));
        }

        static Vector3 ViewerHint(Transform screen)
        {
            if (CockpitSeat.Occupied != null)
            {
                var cam = CameraOf(CockpitSeat.Occupied);
                if (cam != null) return cam.transform.position;
                return CockpitSeat.Occupied.transform.position;
            }
            var seats = FindObjectsByType<CockpitSeat>(FindObjectsInactive.Exclude);
            Transform best = null;
            float bd = float.MaxValue;
            if (seats != null)
            {
                foreach (var s in seats)
                {
                    if (s == null) continue;
                    float d = (s.transform.position - screen.position).sqrMagnitude;
                    if (d < bd) { bd = d; best = s.transform; }
                }
            }
            return best != null ? best.position : screen.position + screen.TransformDirection(Vector3.back);
        }

        // Map the RT so visual left is U=0 and visual top is V=1 (Unity texture top = UITK panel top).
        // The previous hardcoded scale.y = -1 was a vertical mirror: upside down and backwards on both faces.
        static void DeriveUvOrientation(Mesh mesh, Transform t, Vector3 viewFrom, out bool flipU, out bool flipV)
        {
            flipU = false;
            flipV = false;
            if (mesh == null || t == null || mesh.vertexCount < 3) return;
            var verts = mesh.vertices;
            var uvs = mesh.uv;
            var tris = mesh.triangles;
            if (uvs == null || uvs.Length < verts.Length || tris == null || tris.Length < 3) return;

            Vector3 n = Vector3.zero;
            Vector3 c = Vector3.zero;
            float areaSum = 0f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(verts[tris[i]]);
                Vector3 b = t.TransformPoint(verts[tris[i + 1]]);
                Vector3 d = t.TransformPoint(verts[tris[i + 2]]);
                Vector3 cr = Vector3.Cross(b - a, d - a);
                float area = cr.magnitude;
                if (area < 1e-12f) continue;
                n += cr;
                c += (a + b + d) * (area / 3f);
                areaSum += area;
            }
            if (areaSum < 1e-12f) return;
            c /= areaSum;
            n.Normalize();
            if (Vector3.Dot(n, viewFrom - c) < 0f) n = -n;

            Vector3 upHint = t.root != null ? t.root.up : Vector3.up;
            Vector3 up = Vector3.ProjectOnPlane(upHint, n);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, n);
            if (up.sqrMagnitude < 1e-6f) return;
            up.Normalize();
            Vector3 right = Vector3.Cross(n, up);
            if (right.sqrMagnitude < 1e-6f) return;
            right.Normalize();

            float sumS = 0f, sumT = 0f, sumU = 0f, sumV = 0f, sumSU = 0f, sumTV = 0f;
            int count = 0;
            for (int i = 0; i < verts.Length && i < uvs.Length; i++)
            {
                Vector3 w = t.TransformPoint(verts[i]);
                float s = Vector3.Dot(w - c, right);
                float vt = Vector3.Dot(w - c, up);
                float u = uvs[i].x;
                float v = uvs[i].y;
                sumS += s; sumT += vt; sumU += u; sumV += v;
                sumSU += s * u; sumTV += vt * v;
                count++;
            }
            if (count < 2) return;
            float inv = 1f / count;
            flipU = (sumSU * inv - (sumS * inv) * (sumU * inv)) < 0f;
            flipV = (sumTV * inv - (sumT * inv) * (sumV * inv)) < 0f;
        }

        static bool TryMeshUv(ScreenFace face, Ray worldRay, float maxDist, out Vector2 uv, out Vector3 worldPoint, out float dist)
        {
            uv = default;
            worldPoint = default;
            dist = 0f;
            if (face == null || face.filter == null || face.filter.sharedMesh == null) return false;
            var mesh = face.filter.sharedMesh;
            var t = face.filter.transform;
            var verts = mesh.vertices;
            var uvs = mesh.uv;
            var tris = mesh.triangles;
            if (uvs == null || uvs.Length < verts.Length || tris == null || tris.Length < 3) return false;
            Vector3 origin = t.InverseTransformPoint(worldRay.origin);
            Vector3 dir = t.InverseTransformDirection(worldRay.direction);
            if (dir.sqrMagnitude < 1e-12f) return false;
            dir.Normalize();

            float best = float.MaxValue;
            bool hit = false;
            Vector2 bestUv = default;
            Vector3 bestLocal = default;
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (!RayTriangle(origin, dir, verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]], out float td, out Vector3 bary))
                    continue;
                if (td < 0f) continue;
                Vector3 local = bary.x * verts[tris[i]] + bary.y * verts[tris[i + 1]] + bary.z * verts[tris[i + 2]];
                Vector3 world = t.TransformPoint(local);
                float wd = Vector3.Dot(world - worldRay.origin, worldRay.direction.normalized);
                if (wd < 0f || wd > maxDist || wd >= best) continue;
                best = wd;
                bestUv = bary.x * uvs[tris[i]] + bary.y * uvs[tris[i + 1]] + bary.z * uvs[tris[i + 2]];
                bestLocal = local;
                hit = true;
            }
            if (!hit) return false;
            uv = bestUv;
            worldPoint = t.TransformPoint(bestLocal);
            dist = best;
            return true;
        }

        static bool RayTriangle(Vector3 orig, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2, out float t, out Vector3 bary)
        {
            const float eps = 1e-6f;
            t = 0f;
            bary = default;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -eps && det < eps) return false;
            float inv = 1f / det;
            Vector3 s = orig - v0;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            if (t < 0f) return false;
            bary = new Vector3(1f - u - v, u, v);
            return true;
        }

        static void RestoreFace(ScreenFace face)
        {
            if (face == null) return;
            if (face.renderer != null)
            {
                if (face.original != null) face.renderer.sharedMaterial = face.original;
                face.renderer.gameObject.layer = face.originalLayer;
            }
            if (face.instance != null) Destroy(face.instance);
            if (face.rt != null) { face.rt.Release(); Destroy(face.rt); }
            if (face.panelSettings != null) Destroy(face.panelSettings);
        }

        void BuildPanel(ScreenFace face)
        {
            var root = face.doc.rootVisualElement;
            if (root == null) return;
            XenoarchUI.FillRoot(root);
            root.pickingMode = PickingMode.Ignore;
            root.style.backgroundColor = new Color(0.02f, 0.055f, 0.12f, 1f);
            root.style.paddingLeft = 22; root.style.paddingRight = 22; root.style.paddingTop = 14; root.style.paddingBottom = 10;
            root.style.flexDirection = FlexDirection.Column;
            face.root = root;
            if (face.left) BuildLeft(face, root);
            else BuildRight(face, root);
        }

        void BuildLeft(ScreenFace face, VisualElement root)
        {
            var L = face.labels;
            Header(root, "UNC", "RD-X2  V3", "NAV  /  FTL", true);
            L.status = StatusBar(root, new Color(0.07f, 0.2f, 0.12f));
            Section(root, "ROUTE VECTOR", "LOCKED");
            var route = Row(root, 72);
            var originCol = Col(route);
            Label(originCol, "ORIGIN", 13, C.Label, false);
            L.origin = Label(originCol, "—", 30, C.White, true);
            var mid = Col(route); mid.style.justifyContent = Justify.Center; mid.style.alignItems = Align.Center;
            Label(mid, "▸  FTL  ▸", 20, C.Cyan, true);
            var destCol = Col(route); destCol.style.alignItems = Align.FlexEnd;
            Label(destCol, "DESTINATION", 13, C.Label, false);
            L.dest = Label(destCol, "—", 26, C.White, true); L.dest.style.unityTextAlign = TextAnchor.MiddleRight;

            Section(root, "FTL PARAMETERS", "RX 22");
            var pr = Row(root, 108);
            var c0 = Col(pr); Label(c0, "FTL SPEED", 13, C.Label, false); L.speed = Label(c0, "0 c", 32, C.Cyan, true); L.speedSub = Label(c0, "0 ly/hr", 15, C.CyanMid, false);
            var c1 = Col(pr); Label(c1, "DIST REMAINING", 13, C.Label, false); L.remain = Label(c1, "—", 32, C.Yellow, true); L.remainSub = Label(c1, "", 14, C.White, false);
            var c2 = Col(pr); Label(c2, "ETA", 13, C.Label, false); L.eta = Label(c2, "—", 32, C.Green, true); L.etaSub = Label(c2, "", 14, C.White, false);

            L.progress = Label(root, "JUMP PROGRESS", 14, C.Cyan, false);
            var barHost = new VisualElement(); barHost.style.height = 18; barHost.style.marginTop = 4; barHost.style.marginBottom = 10;
            root.Add(barHost);
            var bar = HBar(barHost); L.progress.userData = bar;

            Section(root, "SUBSPACE FIELD  /  TRANSMITTER DETECT", "LIVE TRACE");
            var waves = Row(root, 96);
            face.waveA = Wave(waves, C.Cyan, 4.6f, 0.2f);
            face.waveB = Wave(waves, new Color(0.31f, 1f, 0.78f), 6.1f, 1.3f);

            Section(root, "SIGNAL FTD DETECT  /  VECTOR LOCK", "BEARING  +0.3°");
            var ftd = Row(root, 96);
            Diamonds(ftd);
            var side = Box(ftd);
            side.style.width = Length.Percent(42);
            L.stage = Kv(side, "STAGE");
            L.window = Kv(side, "WINDOW");
            L.heading = Kv(side, "HEADING");
            L.swirl = Kv(side, "SWIRL");

            Section(root, "HUD FUNCTIONS", "RD-X2");
            var gauges = GaugeRow(root);
            face.g0 = Gauge(gauges, "JUMP", C.Cyan); face.g1 = Gauge(gauges, "STAB", C.Green); face.g2 = Gauge(gauges, "FLUX", C.Yellow);
            Register(face, "gauge-jump", face.g0, true);
            Register(face, "gauge-stab", face.g1, true);
            Register(face, "gauge-flux", face.g2, true);
            Register(face, "params", pr, false);
            Register(face, "route", route, false);
        }

        void BuildRight(ScreenFace face, VisualElement root)
        {
            var L = face.labels;
            Header(root, "UNC", "RT-X4  V2", "SYSTEMS  /  SCAN", false);
            L.status = StatusBar(root, new Color(0.07f, 0.16f, 0.23f));
            Section(root, "SCANNED PARAMETERS  /  SHIP SYSTEMS", "CONSTELLATION MK I");

            SysRow(root, "FUEL", out L.fuel, out L.fuelSub, C.Cyan, out var fuelBar);
            L.fuel.userData = fuelBar;
            SysRow(root, "FTL RANGE", out L.range, out L.rangeSub, C.Green, out var rangeBar);
            L.range.userData = rangeBar;
            SysRow(root, "CORE TEMP", out L.core, out L.coreSub, C.Orange, out var coreBar);
            L.core.userData = coreBar;
            SysRow(root, "ENERGY BUS", out L.energy, out L.energySub, C.Yellow, out var energyBar);
            L.energy.userData = energyBar;

            Section(root, "FILTER 23  /  SENSOR SUITE", "SCAN LVL");
            var filters = Row(root, 56);
            FilterTile(filters, "BAND A", "PASS", C.Green);
            FilterTile(filters, "BAND B", "PASS", C.Green);
            FilterTile(filters, "NOISE GATE", "ON", C.Cyan);
            FilterTile(filters, "SUBSPACE", "OPEN", C.Yellow);

            Section(root, "SIGNAL TPU ANALYSIS  /  SUBSPACE SPECTRUM", "FILTER 23");
            face.spectrum = new SpectrumElement();
            face.spectrum.style.height = 96;
            face.spectrum.style.marginBottom = 8;
            face.spectrum.style.backgroundColor = new Color(0.016f, 0.047f, 0.1f);
            root.Add(face.spectrum);

            Section(root, "GALACTIC FIX  (ly, Sol frame)", "INTERPOLATED");
            L.coords = Label(root, "X  —     Y  —     Z  —", 16, C.White, false);
            L.coords.style.marginBottom = 8;

            Section(root, "DRIVE GAUGES", "RT-X4");
            var gauges = GaugeRow(root);
            face.g0 = Gauge(gauges, "FUEL", C.Cyan); face.g1 = Gauge(gauges, "CORE", C.Orange); face.g2 = Gauge(gauges, "PWR", C.Yellow);
            Register(face, "gauge-fuel", face.g0, true);
            Register(face, "gauge-core", face.g1, true);
            Register(face, "gauge-pwr", face.g2, true);
            Register(face, "systems", L.fuel.parent?.parent, false);
        }

        void Register(ScreenFace face, string id, VisualElement e, bool gauge)
        {
            if (e != null) face.regions.Add(new HitRegion { id = id, element = e, isGauge = gauge });
        }

        void ApplyLive(bool force)
        {
            var x = XenoarchRuntime.Session?.Exploration;
            var seq = ShipFtlSequence.Active;
            bool jumping = seq != null && seq.Running;
            var ship = x?.Ship;
            var here = x?.CurrentBody;
            var hereSys = x?.CurrentSystem;
            var plotted = x?.PlottedBody;

            string originName, destName, originSysName, destSysName;
            float totalLy, remainLy, traveledLy, progress, etaSec, speedC, speedLyHr, distAu;
            bool interstellar;
            Vector3 gal;
            string stage, etaText, statusLeft, statusRight;

            if (jumping)
            {
                originName = seq.OriginBody != null ? seq.OriginBody.displayName : (here != null ? here.displayName : "—");
                destName = seq.DestinationBody != null ? seq.DestinationBody.displayName : "—";
                originSysName = seq.OriginSystem != null ? seq.OriginSystem.displayName : "";
                destSysName = seq.DestinationSystem != null ? seq.DestinationSystem.displayName : destName;
                interstellar = seq.InterstellarJump;
                totalLy = interstellar ? seq.DistanceLy : seq.DistanceAU * LyPerAu;
                distAu = seq.DistanceAU;
                progress = seq.CurrentPhase == ShipFtlSequence.Phase.Jump ? seq.JumpProgress
                    : seq.CurrentPhase == ShipFtlSequence.Phase.Drop ? 1f
                    : 0f;
                traveledLy = totalLy * progress;
                remainLy = totalLy * (1f - progress);
                etaSec = seq.CurrentPhase == ShipFtlSequence.Phase.Jump ? Mathf.Max(0f, seq.JumpDuration - seq.JumpElapsed)
                    : seq.CurrentPhase == ShipFtlSequence.Phase.Spool ? seq.JumpDuration
                    : 0f;
                float seconds = Mathf.Max(0.001f, seq.JumpDuration);
                float lyPerSec = totalLy / seconds;
                speedLyHr = lyPerSec * 3600f;
                speedC = lyPerSec * SecondsPerYear;
                gal = Vector3.Lerp(seq.OriginSystem != null ? seq.OriginSystem.galacticPosition : Vector3.zero,
                    seq.DestinationSystem != null ? seq.DestinationSystem.galacticPosition : Vector3.zero, progress);
                stage = seq.CurrentPhase == ShipFtlSequence.Phase.Spool ? "SPOOL"
                    : seq.CurrentPhase == ShipFtlSequence.Phase.Drop ? "DROP-OUT" : "CRUISE";
                etaText = FormatEta(etaSec);
                statusLeft = $"●  LIVE   •   FTL IN PROGRESS   •   JUMP CLOCK  {FormatEta(seq.JumpElapsed)} / {FormatEta(seq.JumpDuration)}";
                statusRight = "●  LIVE   •   DRIVE ONLINE   •   STARDATE  2187.03.05";
            }
            else
            {
                originName = here != null ? here.displayName : "—";
                destName = plotted != null ? plotted.displayName : "NO COURSE";
                originSysName = hereSys != null ? hereSys.displayName : "";
                destSysName = plotted != null && x != null ? (x.Galaxy != null && x.Galaxy.SystemOf(plotted.Id) is StarSystemDefinition ds ? ds.displayName : destName) : "—";
                interstellar = plotted != null && x != null && x.IsInterstellar(plotted);
                totalLy = plotted != null && x != null ? (interstellar ? x.DistanceLy(plotted) : x.DistanceAU(here, plotted) * LyPerAu) : 0f;
                distAu = plotted != null && x != null ? x.DistanceAU(here, plotted) : 0f;
                progress = 0f; traveledLy = 0f; remainLy = totalLy; etaSec = 0f; speedC = 0f; speedLyHr = 0f;
                gal = hereSys != null ? hereSys.galacticPosition : Vector3.zero;
                stage = "IDLE";
                etaText = plotted != null ? "READY" : "—";
                statusLeft = "●  LIVE   •   DRIVE IDLE   •   AWAITING COURSE";
                statusRight = "●  LIVE   •   DRIVE STANDBY   •   STARDATE  2187.03.05";
            }

            float fuel = ship != null ? ship.fuel : 0f, cap = ship != null && ship.fuelCapacity > 0f ? ship.fuelCapacity : 100f;
            float fuelT = Mathf.Clamp01(fuel / cap);
            float rangeLy = x != null ? x.FtlRangeLy : 0f;
            float env = jumping && seq.CurrentPhase == ShipFtlSequence.Phase.Jump ? Mathf.Sin(Mathf.Clamp01(seq.JumpProgress) * Mathf.PI) : 0f;
            float coreT = jumping ? Mathf.Clamp01(0.48f + 0.4f * env) : 0.38f;
            float pwrT = jumping ? Mathf.Clamp01(0.70f + 0.22f * env) : 0.64f;
            int coreK = Mathf.RoundToInt(Mathf.Lerp(480f, 1180f, coreT));

            // Left
            var Ll = _left.labels;
            Set(Ll.status, statusLeft, jumping ? C.Green : C.Cyan);
            Set(Ll.origin, originSysName != "" && jumping ? originSysName : originName);
            Set(Ll.dest, destSysName != "" ? destSysName : destName);
            if (jumping && seq.CurrentPhase == ShipFtlSequence.Phase.Jump)
            {
                Set(Ll.speed, FormatC(speedC));
                Set(Ll.speedSub, $"{speedLyHr:0.0}  ly/hr");
            }
            else if (jumping && seq.CurrentPhase == ShipFtlSequence.Phase.Spool)
            {
                Set(Ll.speed, "SPINNING UP");
                Set(Ll.speedSub, "drive spool");
            }
            else
            {
                Set(Ll.speed, "0 c");
                Set(Ll.speedSub, "0 ly/hr");
            }
            if (interstellar || totalLy >= 0.01f)
            {
                Set(Ll.remain, jumping ? $"{remainLy:0.00} ly" : (plotted != null ? $"{totalLy:0.00} ly" : "—"));
                Set(Ll.remainSub, jumping ? $"TRAVELED  {traveledLy:0.00} ly" : (interstellar ? "INTERSTELLAR LEG" : (distAu > 0f ? $"{distAu:0.##} AU" : "")));
            }
            else
            {
                Set(Ll.remain, plotted != null && !jumping ? $"{distAu:0.##} AU" : (jumping ? $"{seq.DistanceAU * (1f - progress):0.##} AU" : "—"));
                Set(Ll.remainSub, jumping ? $"TRAVELED  {seq.DistanceAU * progress:0.##} AU" : "");
            }
            Set(Ll.eta, etaText);
            Set(Ll.etaSub, totalLy >= 0.01f ? $"LEG  {totalLy:0.00} ly" : (distAu > 0f ? $"LEG  {distAu:0.##} AU" : ""));
            float barT = jumping ? progress : 0f;
            Set(Ll.progress, jumping ? $"JUMP PROGRESS   {Mathf.RoundToInt(barT * 100f)}%" : "JUMP PROGRESS   —");
            if (Ll.progress.userData is VisualElement barFill) barFill.style.width = Length.Percent(barT * 100f);
            Set(Ll.stage, stage, jumping && seq.CurrentPhase == ShipFtlSequence.Phase.Jump ? C.Green : C.Yellow);
            Set(Ll.window, jumping ? "FWD LOCK" : "READY", C.Cyan);
            Set(Ll.heading, "142.7°");
            Set(Ll.swirl, jumping ? "1.2 rad/s" : "0.0 rad/s");
            _left.g0.SetValue(barT, $"{Mathf.RoundToInt(barT * 100f)}");
            _left.g1.SetValue(jumping ? 0.91f : 1f, jumping ? "91" : "100");
            _left.g2.SetValue(jumping ? Mathf.Clamp01(0.55f + 0.4f * env) : 0.12f, jumping ? Mathf.RoundToInt((0.55f + 0.4f * env) * 100f).ToString() : "12");
            float amp = jumping ? 0.85f : 0.22f;
            if (_left.waveA != null) { _left.waveA.Amplitude = amp; _left.waveA.MarkDirtyRepaint(); }
            if (_left.waveB != null) { _left.waveB.Amplitude = jumping ? 0.7f : 0.18f; _left.waveB.MarkDirtyRepaint(); }

            // Right
            var Lr = _right.labels;
            Set(Lr.status, statusRight, C.Cyan);
            Set(Lr.fuel, $"{fuel:0.0} / {cap:0}");
            float commit = jumping ? seq.ReservedFuel : plotted != null && x != null ? x.FuelCost(plotted) : 0f;
            Set(Lr.fuelSub, commit > 0f ? $"COMMIT  {commit:0.0}" : "HOLDING");
            if (Lr.fuel.userData is VisualElement fb) fb.style.width = Length.Percent(fuelT * 100f);
            Set(Lr.range, $"{rangeLy:0.0} ly");
            Set(Lr.rangeSub, rangeLy > 0f ? "NAV DRIVE FITTED" : "IN-SYSTEM ONLY");
            if (Lr.range.userData is VisualElement rb) rb.style.width = Length.Percent(rangeLy > 0f ? 100f : 8f);
            Set(Lr.core, $"{coreK} K   {Mathf.RoundToInt(coreT * 100f)}%");
            Set(Lr.coreSub, "DISPLAY-ONLY");
            if (Lr.core.userData is VisualElement cb) cb.style.width = Length.Percent(coreT * 100f);
            Set(Lr.energy, $"{Mathf.RoundToInt(pwrT * 100f)}%");
            Set(Lr.energySub, "DISPLAY-ONLY");
            if (Lr.energy.userData is VisualElement eb) eb.style.width = Length.Percent(pwrT * 100f);
            Set(Lr.coords, $"X  {FmtAxis(gal.x)}       Y  {FmtAxis(gal.y)}       Z  {FmtAxis(gal.z)}");
            _right.g0.SetValue(fuelT, Mathf.RoundToInt(fuelT * 100f).ToString());
            _right.g1.SetValue(coreT, Mathf.RoundToInt(coreT * 100f).ToString());
            _right.g2.SetValue(pwrT, Mathf.RoundToInt(pwrT * 100f).ToString());
            if (_right.spectrum != null) { _right.spectrum.Hot = jumping; _right.spectrum.MarkDirtyRepaint(); }
        }

        static string FmtAxis(float v) => v < 0f ? $"−{Mathf.Abs(v):0.000}" : $"{v:0.000}";

        static string FormatC(float c)
        {
            if (c >= 1000000f) return $"{c:N0} c";
            if (c >= 1000f) return $"{c:N0} c";
            if (c >= 10f) return $"{c:0.0} c";
            return $"{c:0.00} c";
        }

        static string FormatEta(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int m = Mathf.FloorToInt(seconds / 60f);
            float s = seconds - m * 60f;
            return m > 0 ? $"{m:00}:{s:00.0}" : $"00:{s:00.0}";
        }

        static void Set(Label l, string t) { if (l != null) l.text = t; }
        static void Set(Label l, string t, Color c) { if (l == null) return; l.text = t; l.style.color = c; }

        // ----- UITK chrome -----
        static void Header(VisualElement parent, string brand, string model, string role, bool _)
        {
            var row = Row(parent, 40);
            var left = Row(row, 40);
            var b = Label(left, brand, 26, C.Cyan, true); b.style.marginRight = 10; b.style.unityFontStyleAndWeight = FontStyle.Bold;
            Label(left, model, 18, C.White, true);
            var r = Label(row, role, 24, C.Yellow, true);
            r.style.unityTextAlign = TextAnchor.MiddleRight;
            r.style.flexGrow = 1;
            r.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        static Label StatusBar(VisualElement parent, Color bg)
        {
            var bar = new VisualElement();
            bar.style.height = 28; bar.style.backgroundColor = bg; bar.style.marginBottom = 8;
            bar.style.justifyContent = Justify.Center; bar.style.paddingLeft = 12;
            parent.Add(bar);
            var l = Label(bar, "", 13, C.Green, false);
            return l;
        }

        static void Section(VisualElement parent, string title, string right)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 26;
            row.style.backgroundColor = new Color(0.03f, 0.125f, 0.227f);
            row.style.marginTop = 2; row.style.marginBottom = 8;
            row.style.alignItems = Align.Center;
            var accent = new VisualElement(); accent.style.width = 4; accent.style.backgroundColor = C.Cyan; accent.style.height = Length.Percent(100);
            row.Add(accent);
            var t = Label(row, title, 15, C.Cyan, true); t.style.paddingLeft = 10; t.style.flexGrow = 1;
            var r = Label(row, right, 12, C.Yellow, false); r.style.paddingRight = 10;
            parent.Add(row);
        }

        static VisualElement Row(VisualElement parent, float h)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.height = h;
            e.style.marginBottom = 4;
            parent.Add(e);
            return e;
        }

        static VisualElement Col(VisualElement parent)
        {
            var e = new VisualElement();
            e.style.flexGrow = 1; e.style.flexDirection = FlexDirection.Column;
            parent.Add(e);
            return e;
        }

        static VisualElement Box(VisualElement parent)
        {
            var e = new VisualElement();
            e.style.flexGrow = 1;
            e.style.backgroundColor = new Color(0.016f, 0.047f, 0.1f);
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = C.Grid;
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = 1;
            e.style.paddingLeft = e.style.paddingRight = 8; e.style.paddingTop = 6;
            parent.Add(e);
            return e;
        }

        static Label Label(VisualElement parent, string text, int size, Color color, bool bold)
        {
            var l = new Label(text);
            l.pickingMode = PickingMode.Ignore;
            l.style.color = color;
            l.style.fontSize = size;
            l.style.marginTop = l.style.marginBottom = l.style.marginLeft = l.style.marginRight = 0;
            l.style.paddingTop = l.style.paddingBottom = l.style.paddingLeft = l.style.paddingRight = 0;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.overflow = Overflow.Hidden;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            parent.Add(l);
            return l;
        }

        static VisualElement HBar(VisualElement parent)
        {
            parent.style.backgroundColor = new Color(0.04f, 0.11f, 0.19f);
            parent.style.borderLeftColor = parent.style.borderRightColor = parent.style.borderTopColor = parent.style.borderBottomColor = C.CyanDim;
            parent.style.borderLeftWidth = parent.style.borderRightWidth = parent.style.borderTopWidth = parent.style.borderBottomWidth = 1;
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(0);
            fill.style.backgroundColor = C.Cyan;
            parent.Add(fill);
            return fill;
        }

        static void SysRow(VisualElement parent, string key, out Label value, out Label sub, Color barColor, out VisualElement bar)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 52;
            row.style.marginBottom = 2;
            parent.Add(row);
            var left = new VisualElement(); left.style.width = Length.Percent(42); left.style.flexDirection = FlexDirection.Column;
            row.Add(left);
            Label(left, key, 13, C.Label, false);
            value = Label(left, "—", 22, C.White, true);
            var right = new VisualElement(); right.style.flexGrow = 1; right.style.flexDirection = FlexDirection.Column; right.style.justifyContent = Justify.FlexEnd;
            row.Add(right);
            sub = Label(right, "", 11, C.Label, false);
            sub.style.unityTextAlign = TextAnchor.MiddleRight;
            var track = new VisualElement(); track.style.height = 12; track.style.marginTop = 4;
            right.Add(track);
            bar = HBar(track);
            bar.style.backgroundColor = barColor;
        }

        static void FilterTile(VisualElement parent, string k, string v, Color c)
        {
            var e = Box(parent);
            e.style.alignItems = Align.Center; e.style.justifyContent = Justify.Center; e.style.marginRight = 6;
            Label(e, k, 12, C.Label, false);
            Label(e, v, 16, c, true);
        }

        static Label Kv(VisualElement parent, string key)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.height = 20; row.style.justifyContent = Justify.SpaceBetween;
            parent.Add(row);
            Label(row, key, 13, C.Label, false);
            return Label(row, "—", 14, C.White, true);
        }

        static VisualElement GaugeRow(VisualElement parent)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.flexGrow = 1;
            e.style.minHeight = 250;
            e.style.justifyContent = Justify.SpaceAround;
            e.style.alignItems = Align.Center;
            parent.Add(e);
            return e;
        }

        static GaugeElement Gauge(VisualElement parent, string name, Color color)
        {
            var g = new GaugeElement { GaugeName = name, Accent = color };
            g.style.width = 260; g.style.height = 240;
            g.style.flexGrow = 0;
            parent.Add(g);
            return g;
        }

        static WaveElement Wave(VisualElement parent, Color color, float freq, float phase)
        {
            var w = new WaveElement { Accent = color, Frequency = freq, Phase = phase };
            w.style.flexGrow = 1; w.style.height = 96; w.style.marginRight = 8;
            w.style.backgroundColor = new Color(0.016f, 0.047f, 0.1f);
            parent.Add(w);
            return w;
        }

        static void Diamonds(VisualElement parent)
        {
            var d = new DiamondElement();
            d.style.flexGrow = 1; d.style.height = 96; d.style.marginRight = 8;
            d.style.backgroundColor = new Color(0.016f, 0.047f, 0.1f);
            parent.Add(d);
        }

        static class C
        {
            public static readonly Color Cyan = new Color(0.35f, 0.86f, 1f);
            public static readonly Color CyanMid = new Color(0.24f, 0.7f, 0.86f);
            public static readonly Color CyanDim = new Color(0.16f, 0.47f, 0.63f);
            public static readonly Color White = new Color(0.86f, 0.96f, 1f);
            public static readonly Color Yellow = new Color(1f, 0.82f, 0.27f);
            public static readonly Color Green = new Color(0.31f, 0.9f, 0.55f);
            public static readonly Color Orange = new Color(1f, 0.55f, 0.2f);
            public static readonly Color Label = new Color(0.27f, 0.59f, 0.71f);
            public static readonly Color Grid = new Color(0.07f, 0.2f, 0.31f);
        }

        sealed class GaugeElement : VisualElement
        {
            public string GaugeName;
            public Color Accent;
            public float Fill;
            public string Value = "0";
            readonly Label _v, _u, _n;
            public GaugeElement()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnPaint;
                _v = new Label(Value) { pickingMode = PickingMode.Ignore };
                _v.style.position = Position.Absolute; _v.style.left = 0; _v.style.right = 0; _v.style.top = Length.Percent(28);
                _v.style.unityTextAlign = TextAnchor.MiddleCenter; _v.style.fontSize = 44; _v.style.color = C.White;
                _v.style.unityFontStyleAndWeight = FontStyle.Bold;
                _u = new Label("%") { pickingMode = PickingMode.Ignore };
                _u.style.position = Position.Absolute; _u.style.left = 0; _u.style.right = 0; _u.style.top = Length.Percent(52);
                _u.style.unityTextAlign = TextAnchor.MiddleCenter; _u.style.fontSize = 14; _u.style.color = C.Label;
                _n = new Label(GaugeName) { pickingMode = PickingMode.Ignore };
                _n.style.position = Position.Absolute; _n.style.left = 0; _n.style.right = 0; _n.style.bottom = 4;
                _n.style.unityTextAlign = TextAnchor.MiddleCenter; _n.style.fontSize = 18; _n.style.color = C.CyanMid;
                Add(_v); Add(_u); Add(_n);
            }
            public void SetValue(float t, string v)
            {
                Fill = Mathf.Clamp01(t);
                Value = v;
                if (_v != null) _v.text = v;
                if (_n != null) _n.text = GaugeName;
                MarkDirtyRepaint();
            }
            void OnPaint(MeshGenerationContext ctx)
            {
                var r = contentRect;
                if (r.width < 4f || r.height < 4f) return;
                var p = ctx.painter2D;
                Vector2 c = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.44f);
                float rad = Mathf.Min(r.width, r.height) * 0.38f;
                DrawArc(p, c, rad, 225f, -30f, new Color(0.08f, 0.2f, 0.31f), 16f);
                DrawArc(p, c, rad, 225f, Mathf.Lerp(225f, -30f, Fill), Accent, 16f);
                p.strokeColor = C.CyanDim; p.lineWidth = 2f;
                p.BeginPath(); p.Arc(c, rad + 12f, 0, 360f); p.Stroke();
            }
            static void DrawArc(Painter2D p, Vector2 c, float rad, float fromDeg, float toDeg, Color col, float width)
            {
                p.strokeColor = col; p.lineWidth = width; p.lineCap = LineCap.Round;
                p.BeginPath();
                int steps = 32;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float a = Mathf.Lerp(fromDeg, toDeg, t) * Mathf.Deg2Rad;
                    var pt = new Vector2(c.x + Mathf.Cos(a) * rad, c.y - Mathf.Sin(a) * rad);
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.Stroke();
            }
        }

        sealed class WaveElement : VisualElement
        {
            public Color Accent = C.Cyan;
            public float Frequency = 4f, Phase, Amplitude = 0.5f;
            public WaveElement() { generateVisualContent += OnPaint; pickingMode = PickingMode.Ignore; }
            void OnPaint(MeshGenerationContext ctx)
            {
                var r = contentRect; if (r.width < 4f) return;
                var p = ctx.painter2D;
                p.strokeColor = C.Grid; p.lineWidth = 1f;
                for (int i = 1; i < 4; i++)
                {
                    float y = r.y + r.height * i / 4f;
                    p.BeginPath(); p.MoveTo(new Vector2(r.x, y)); p.LineTo(new Vector2(r.xMax, y)); p.Stroke();
                }
                p.strokeColor = Accent; p.lineWidth = 2f; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                float ph = Phase + Time.unscaledTime * Frequency * 0.6f;
                int n = 64;
                for (int i = 0; i <= n; i++)
                {
                    float u = i / (float)n;
                    float s = Mathf.Sin(u * Mathf.PI * 2f * Frequency + ph) * Amplitude
                            + Mathf.Sin(u * Mathf.PI * 2f * Frequency * 2.7f + ph * 1.7f) * Amplitude * 0.28f;
                    var pt = new Vector2(r.x + u * r.width, r.y + r.height * 0.5f - s * r.height * 0.42f);
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.Stroke();
            }
        }

        sealed class SpectrumElement : VisualElement
        {
            public bool Hot;
            public SpectrumElement() { generateVisualContent += OnPaint; pickingMode = PickingMode.Ignore; }
            void OnPaint(MeshGenerationContext ctx)
            {
                var r = contentRect; if (r.width < 4f) return;
                var p = ctx.painter2D;
                const int bars = 36;
                float bw = r.width / bars;
                float t = Time.unscaledTime;
                for (int i = 0; i < bars; i++)
                {
                    float u = i / (float)(bars - 1);
                    float env = Mathf.Exp(-((u - 0.62f) * (u - 0.62f)) / 0.05f) * (Hot ? 0.75f : 0.35f)
                              + Mathf.Exp(-((u - 0.3f) * (u - 0.3f)) / 0.08f) * (Hot ? 0.5f : 0.22f);
                    float pulse = 0.85f + 0.15f * Mathf.Sin(t * 6f + i * 0.4f);
                    float h = Mathf.Clamp01(0.18f + env * pulse) * (r.height - 8f);
                    var col = u < 0.75f ? C.Cyan : Color.Lerp(C.Cyan, new Color(1f, 0.27f, 0.27f), (u - 0.75f) / 0.25f);
                    p.fillColor = col;
                    p.BeginPath();
                    float x = r.x + i * bw;
                    p.MoveTo(new Vector2(x, r.yMax - 2));
                    p.LineTo(new Vector2(x, r.yMax - 2 - h));
                    p.LineTo(new Vector2(x + bw - 3, r.yMax - 2 - h));
                    p.LineTo(new Vector2(x + bw - 3, r.yMax - 2));
                    p.ClosePath();
                    p.Fill();
                }
            }
        }

        sealed class DiamondElement : VisualElement
        {
            public DiamondElement() { generateVisualContent += OnPaint; pickingMode = PickingMode.Ignore; }
            void OnPaint(MeshGenerationContext ctx)
            {
                var r = contentRect; if (r.width < 4f) return;
                var p = ctx.painter2D;
                const int cols = 7, rows = 4;
                float gx = r.width / (cols + 0.4f), gy = r.height / (rows + 0.4f);
                float s = Mathf.Min(gx, gy) * 0.32f;
                int hi = 4, hj = 1;
                p.lineWidth = 1.5f;
                for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    float cx = r.x + gx * (i + 0.7f), cy = r.y + gy * (j + 0.7f);
                    bool on = i == hi && j == hj;
                    float ss = on ? s * 1.25f : s;
                    p.strokeColor = on ? C.Yellow : ((i + j) % 2 == 0 ? new Color(1f, 0.27f, 0.27f) : new Color(0.27f, 0.43f, 1f));
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx, cy - ss));
                    p.LineTo(new Vector2(cx + ss, cy));
                    p.LineTo(new Vector2(cx, cy + ss));
                    p.LineTo(new Vector2(cx - ss, cy));
                    p.ClosePath();
                    p.Stroke();
                }
            }
        }

    }
}
