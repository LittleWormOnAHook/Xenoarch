using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>Tracks whether the chisel head volume overlaps a buried find (trigger on the head).</summary>
    public sealed class SonicHeadTrigger : MonoBehaviour
    {
        float _near = -10f;
        public bool Near => Time.time - _near < 0.2f;
        void OnTriggerStay(Collider c) { if (c != null && c.GetComponentInParent<BuriedArtifact>() != null) _near = Time.time; }
    }

    /// <summary>
    /// Visuals for the sonic tools: a beam from the emitter that ends in a semi-transparent chisel head (sphere, disc,
    /// cube or cone) with a wire frame, or, for Fine focus and the brush, a fan of fine lines from one point that spread
    /// over the ground. Pure feedback: the stroke itself is decided by the cell grid. Added by <see cref="DigTool"/>.
    /// </summary>
    public sealed class SonicBeam : MonoBehaviour
    {
        public struct Frame
        {
            public SonicToolKind kind;
            public bool aimed, inReach, firing, nearFind, locked;
            public Vector3 point, normal;
            public float radius, spread;
            public Camera cam;
        }

        const int MaxFan = 11, BeamPoints = 10;
        static readonly Color Teal = new Color(0.41f, 0.875f, 0.84f), Amber = new Color(1f, 0.66f, 0.40f), Coral = new Color(1f, 0.59f, 0.55f);

        GameObject _root, _headGo;
        LineRenderer _beam;
        MeshFilter _mf; MeshRenderer _mr; MeshCollider _mc; SonicHeadTrigger _trig;
        readonly LineRenderer[] _wire = new LineRenderer[6];
        readonly LineRenderer[] _fan = new LineRenderer[MaxFan];
        readonly Gradient[] _fanGrad = new Gradient[MaxFan];
        readonly Vector3[] _fanEnd = new Vector3[MaxFan];
        readonly float[] _fanNext = new float[MaxFan];
        readonly Vector3[] _beamPts = new Vector3[BeamPoints];
        readonly GradientColorKey[] _ck = new GradientColorKey[3];
        readonly GradientAlphaKey[] _ak = new GradientAlphaKey[3];
        static readonly RaycastHit[] _hits = new RaycastHit[10];
        FirstPersonArms _arms; Transform _muzzle; float _nextFind;
        SonicHead _shape = (SonicHead)(-1);
        MaterialPropertyBlock _mpb;

        public bool HeadNear => _trig != null && _trig.Near;

        void Awake() { _arms = GetComponentInChildren<FirstPersonArms>(true); }
        void OnDestroy() { if (_root != null) Destroy(_root); }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        void Build()
        {
            if (_root != null) return;
            _root = new GameObject("Sonic Tool FX");
            _beam = NewLine("Beam", _root.transform, true);
            _beam.positionCount = BeamPoints; _beam.numCapVertices = 4;
            _headGo = new GameObject("Sonic Head") { layer = 2 };
            _headGo.transform.SetParent(_root.transform, false);
            _mf = _headGo.AddComponent<MeshFilter>();
            _mr = _headGo.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = SonicFxAssets.HeadMaterial;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _mr.receiveShadows = false;
            _mc = _headGo.AddComponent<MeshCollider>(); _mc.convex = true; _mc.isTrigger = true;
            var rb = _headGo.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            _trig = _headGo.AddComponent<SonicHeadTrigger>();
            for (int i = 0; i < _wire.Length; i++)
            {
                _wire[i] = NewLine("Wire" + i, _headGo.transform, false);
                _wire[i].widthMultiplier = 0.006f;
            }
            for (int i = 0; i < MaxFan; i++)
            {
                _fan[i] = NewLine("Fan" + i, _root.transform, true);
                _fan[i].positionCount = 2; _fan[i].widthMultiplier = 0.012f;
                _fanGrad[i] = new Gradient();
            }
            _mpb = new MaterialPropertyBlock();
        }

        static LineRenderer NewLine(string name, Transform parent, bool world)
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = world;
            lr.sharedMaterial = SonicFxAssets.BeamMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
            lr.numCornerVertices = 2;
            return lr;
        }

        Vector3 MuzzlePos(Camera cam)
        {
            if ((_muzzle == null || !_muzzle.gameObject.activeInHierarchy) && Time.time >= _nextFind)
            {
                _nextFind = Time.time + 0.5f;
                if (_arms == null) _arms = GetComponentInChildren<FirstPersonArms>(true);
                _muzzle = _arms != null && _arms.RightSlot != null ? FindDeep(_arms.RightSlot, "SonicMuzzle") : null;
            }
            if (_muzzle != null && _muzzle.gameObject.activeInHierarchy) return _muzzle.position;
            return cam.transform.TransformPoint(new Vector3(0.2f, -0.18f, 0.5f));
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t)
            {
                if (c.name == name) return c;
                var d = FindDeep(c, name);
                if (d != null) return d;
            }
            return null;
        }

        public void Tick(in Frame f)
        {
            if (f.kind == SonicToolKind.None || f.cam == null) { Hide(); return; }
            Build();
            bool fine = SonicTools.IsFine(f.kind);
            bool show = f.aimed;
            if (_root.activeSelf != show) _root.SetActive(show);
            if (!show) return;

            Color col = f.locked ? Coral * 0.8f : !f.inReach ? Coral : f.nearFind ? Amber : Teal;
            Vector3 tip = MuzzlePos(f.cam);
            float t = Time.time;

            // ---- chisel head + beam (excavator, coarse) ----
            bool head = !fine;
            _headGo.SetActive(head);
            _beam.gameObject.SetActive(head);
            if (head)
            {
                SetShape(SonicTools.Head);
                var up = f.normal.sqrMagnitude > 0.01f ? f.normal.normalized : Vector3.up;
                var rot = Quaternion.FromToRotation(Vector3.up, up);
                float r = Mathf.Max(0.05f, f.radius);
                Vector3 scale; float lift;
                switch (SonicTools.Head)
                {
                    case SonicHead.Disc: scale = new Vector3(2.6f * r, 0.7f * r, 2.6f * r); lift = 0.3f; break;
                    case SonicHead.Cube: scale = Vector3.one * 1.7f * r; lift = 0.35f; break;
                    case SonicHead.Cone: scale = new Vector3(1.8f * r, 2f * r, 1.8f * r); lift = 0.9f; break;
                    default: scale = Vector3.one * 2f * r; lift = 0.35f; break;
                }
                var pos = f.point + up * (r * lift);
                _headGo.transform.SetPositionAndRotation(pos, rot);
                _headGo.transform.localScale = scale;
                float pulse = f.firing ? 0.5f + 0.5f * Mathf.Sin(t * 40f) : 0f;
                var fill = col; fill.a = f.firing ? 0.26f + 0.1f * pulse : 0.14f;
                _mr.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", fill); _mpb.SetColor("_Color", fill);
                _mr.SetPropertyBlock(_mpb);
                var wc = col; wc.a = f.firing ? 1f : 0.7f;
                for (int i = 0; i < _wire.Length; i++) { _wire[i].startColor = wc; _wire[i].endColor = wc; }

                // beam: wavy line from the emitter to the head
                var dir = pos - tip; float len = dir.magnitude;
                var d = len > 0.001f ? dir / len : f.cam.transform.forward;
                var perp = Vector3.Cross(d, f.cam.transform.up).normalized;
                var perp2 = Vector3.Cross(d, perp).normalized;
                float amp = f.firing ? 0.014f : 0.004f;
                for (int i = 0; i < BeamPoints; i++)
                {
                    float u = i / (float)(BeamPoints - 1);
                    float env = Mathf.Sin(u * Mathf.PI);
                    float ph = t * 34f - u * 22f;
                    _beamPts[i] = Vector3.Lerp(tip, pos, u) + (perp * Mathf.Sin(ph) + perp2 * Mathf.Cos(ph * 0.8f)) * (amp * env);
                }
                _beam.SetPositions(_beamPts);
                _beam.widthMultiplier = f.firing ? 0.045f : 0.022f;
                var bc = col; bc.a = f.firing ? 0.9f : 0.5f;
                _beam.startColor = bc; var ec = bc; ec.a *= 0.5f; _beam.endColor = ec;
            }

            // ---- fine fan (fine focus, brush) ----
            int n = fine && f.inReach && f.firing ? (f.kind == SonicToolKind.Brush ? MaxFan : 7) : 0;
            for (int i = 0; i < MaxFan; i++) if (_fan[i].gameObject.activeSelf != (i < n)) _fan[i].gameObject.SetActive(i < n);
            if (n > 0) UpdateFan(f, tip, n, col);
        }

        void SetShape(SonicHead h)
        {
            if (h == _shape) return;
            _shape = h;
            _mf.sharedMesh = SonicFxAssets.HeadMesh(h);
            _mc.sharedMesh = null; _mc.sharedMesh = _mf.sharedMesh;
            foreach (var w in _wire) w.gameObject.SetActive(false);
            int k = 0;
            void Add(Vector3[] pts, bool loop)
            {
                if (k >= _wire.Length) return;
                var w = _wire[k++]; w.gameObject.SetActive(true);
                w.loop = loop; w.positionCount = pts.Length; w.SetPositions(pts);
            }
            if (h == SonicHead.Cube)
            {
                var b = new[] { new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, -.5f, .5f), new Vector3(-.5f, -.5f, .5f) };
                var tp = new Vector3[4];
                for (int i = 0; i < 4; i++) tp[i] = b[i] + Vector3.up;
                Add(b, true); Add(tp, true);
                for (int i = 0; i < 4; i++) { if (k < _wire.Length) Add(new[] { b[i], tp[i] }, false); }
            }
            else if (h == SonicHead.Cone)
            {
                Add(Circle(1, 0.5f, 0.5f), true);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f;
                    Add(new[] { new Vector3(0f, -0.5f, 0f), new Vector3(Mathf.Cos(a) * 0.5f, 0.5f, Mathf.Sin(a) * 0.5f) }, false);
                }
            }
            else { Add(Circle(1, 0.5f, 0f), true); Add(Circle(0, 0.5f, 0f), true); Add(Circle(2, 0.5f, 0f), true); }
        }

        /// <summary>Circle of radius r. plane 1 = horizontal (XZ) at height y; 0 = XY; 2 = YZ.</summary>
        static Vector3[] Circle(int plane, float r, float y)
        {
            const int N = 28;
            var p = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f, c = Mathf.Cos(a) * r, s = Mathf.Sin(a) * r;
                p[i] = plane == 1 ? new Vector3(c, y, s) : plane == 0 ? new Vector3(c, s, 0f) : new Vector3(0f, s, c);
            }
            return p;
        }

        void UpdateFan(in Frame f, Vector3 tip, int n, Color col)
        {
            var nrm = f.normal.sqrMagnitude > 0.01f ? f.normal.normalized : Vector3.up;
            var t1 = Vector3.Cross(nrm, f.cam.transform.right); if (t1.sqrMagnitude < 0.01f) t1 = Vector3.Cross(nrm, Vector3.forward);
            t1.Normalize(); var t2 = Vector3.Cross(nrm, t1);
            float spread = Mathf.Max(0.05f, f.spread);
            bool brush = f.kind == SonicToolKind.Brush;
            Color core = brush ? Color.Lerp(col, Color.white, 0.55f) : col;
            for (int i = 0; i < n; i++)
            {
                if (Time.time >= _fanNext[i])
                {
                    _fanNext[i] = Time.time + 0.05f + 0.04f * (i % 3);
                    float a = i * 2.399f + Time.time * 2.5f;
                    float rad = spread * Mathf.Sqrt((i + 0.5f) / n) * Random.Range(0.8f, 1.1f);
                    var target = f.point + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * rad;
                    var d = target - tip; float dist = d.magnitude; d = dist > 0.001f ? d / dist : f.cam.transform.forward;
                    _fanEnd[i] = Surface(tip, d, dist + 0.6f, out var hp) ? hp : target;
                }
                var lr = _fan[i];
                lr.SetPosition(0, tip); lr.SetPosition(1, _fanEnd[i]);
                lr.widthMultiplier = brush ? 0.008f : 0.012f;
                float p = Mathf.Repeat(Time.time * 2.6f + i * 0.17f, 1f);
                float pm = Mathf.Clamp(p, 0.08f, 0.92f);
                _ck[0] = new GradientColorKey(core, 0f); _ck[1] = new GradientColorKey(Color.white, pm); _ck[2] = new GradientColorKey(core, 1f);
                _ak[0] = new GradientAlphaKey(0.3f, 0f); _ak[1] = new GradientAlphaKey(1f, pm); _ak[2] = new GradientAlphaKey(0.5f, 1f);
                _fanGrad[i].SetKeys(_ck, _ak);
                lr.colorGradient = _fanGrad[i];
            }
        }

        bool Surface(Vector3 origin, Vector3 dir, float max, out Vector3 point)
        {
            int c = Physics.RaycastNonAlloc(origin, dir, _hits, max, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; point = default;
            for (int i = 0; i < c; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.distance >= best || h.collider.gameObject.layer == 2) continue;
                if (h.collider.transform.IsChildOf(transform.root)) continue;
                if (h.collider.GetComponentInParent<BuriedArtifact>() != null) continue;
                best = h.distance; point = h.point;
            }
            return best < float.MaxValue;
        }
    }
}
