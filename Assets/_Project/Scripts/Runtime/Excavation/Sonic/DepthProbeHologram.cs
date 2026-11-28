using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Hologram scan for the top Depth Probe tier: a faint wire cylinder of the scanned volume under the probed point, a
    /// sweeping scan ring, and a pulsing wire blob plus a plumb line for each tracked object, drawn through the ground.
    /// Shows where and how deep, never what it is (same information as the readout). Created and driven by
    /// <see cref="DepthProbeTool"/>.
    /// </summary>
    public sealed class DepthProbeHologram : MonoBehaviour
    {
        public struct Item { public Vector3 surface, buried; public Color color; }

        const int Seg = 28;
        GameObject _root;
        LineRenderer _sweep;
        readonly List<LineRenderer> _volume = new List<LineRenderer>();   // 3 rings + 4 verticals
        readonly List<LineRenderer[]> _blobs = new List<LineRenderer[]>();   // per track: 3 circles + plumb line
        Vector3 _centre; float _range, _depth; int _count;
        bool _on;
        static readonly Color Teal = new Color(0.41f, 0.875f, 0.84f);

        void OnDestroy() { if (_root != null) Destroy(_root); }

        LineRenderer Line(string name, bool loop, int points, float width)
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(_root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = loop; lr.positionCount = points; lr.widthMultiplier = width;
            lr.sharedMaterial = SonicFxAssets.BeamMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
            return lr;
        }

        void Build()
        {
            if (_root != null) return;
            _root = new GameObject("Depth Probe Hologram");
            for (int i = 0; i < 3; i++) _volume.Add(Line("Ring" + i, true, Seg, 0.01f));
            for (int i = 0; i < 4; i++) _volume.Add(Line("Edge" + i, false, 2, 0.01f));
            _sweep = Line("Sweep", true, Seg, 0.02f);
        }

        public void Hide()
        {
            _on = false;
            if (_root != null) _root.SetActive(false);
        }

        public void Show(Vector3 centre, float range, float maxDepth, IList<Item> items)
        {
            Build();
            _root.SetActive(true); _on = true;
            _centre = centre; _range = Mathf.Max(0.5f, range); _depth = Mathf.Max(0.5f, maxDepth);
            _count = items.Count;
            while (_blobs.Count < items.Count)
            {
                var set = new LineRenderer[4];
                for (int k = 0; k < 3; k++) set[k] = Line("Blob" + _blobs.Count + "_" + k, true, Seg, 0.012f);
                set[3] = Line("Plumb" + _blobs.Count, false, 2, 0.008f);
                _blobs.Add(set);
            }
            for (int i = 0; i < _blobs.Count; i++)
            {
                bool on = i < items.Count;
                foreach (var l in _blobs[i]) l.gameObject.SetActive(on);
                if (!on) continue;
                var it = items[i];
                float r = Mathf.Lerp(0.16f, 0.32f, Mathf.Clamp01((it.surface.y - it.buried.y) / _depth));
                Circle(_blobs[i][0], it.buried, r, 0); Circle(_blobs[i][1], it.buried, r, 1); Circle(_blobs[i][2], it.buried, r, 2);
                _blobs[i][3].SetPosition(0, it.surface); _blobs[i][3].SetPosition(1, it.buried + Vector3.up * r);
                var c = it.color; c.a = 0.85f;
                foreach (var l in _blobs[i]) { l.startColor = c; l.endColor = c; }
            }
            // scanned volume: rings at the surface, mid and max depth, plus four edges
            for (int i = 0; i < 3; i++)
            {
                float f = i / 2f;
                var cc = new Vector3(_centre.x, _centre.y - _depth * f, _centre.z);
                Circle(_volume[i], cc, _range * (1f - 0.18f * f), 1);
                var col = Teal; col.a = 0.35f - 0.1f * f;
                _volume[i].startColor = col; _volume[i].endColor = col;
            }
            for (int e = 0; e < 4; e++)
            {
                float a = e * Mathf.PI * 0.5f;
                _volume[3 + e].SetPosition(0, _centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _range);
                _volume[3 + e].SetPosition(1, new Vector3(_centre.x, _centre.y - _depth, _centre.z) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _range * 0.82f);
                var col = Teal; col.a = 0.22f;
                _volume[3 + e].startColor = col; _volume[3 + e].endColor = col;
            }
        }

        static void Circle(LineRenderer lr, Vector3 c, float r, int plane)
        {
            for (int i = 0; i < lr.positionCount; i++)
            {
                float a = i / (float)lr.positionCount * Mathf.PI * 2f, x = Mathf.Cos(a) * r, y = Mathf.Sin(a) * r;
                lr.SetPosition(i, c + (plane == 1 ? new Vector3(x, 0f, y) : plane == 0 ? new Vector3(x, y, 0f) : new Vector3(0f, y, x)));
            }
        }

        void Update()
        {
            if (!_on || _root == null) return;
            // sweep ring runs from the surface down to the scan depth, fading as it goes
            float u = Mathf.Repeat(Time.time * 0.8f, 1f);
            var cc = new Vector3(_centre.x, _centre.y - _depth * u, _centre.z);
            Circle(_sweep, cc, _range * (1f - 0.18f * u), 1);
            var col = Teal; col.a = (1f - u) * 0.9f;
            _sweep.startColor = col; _sweep.endColor = col;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 6f);
            for (int i = 0; i < _count && i < _blobs.Count; i++)
                foreach (var l in _blobs[i]) { var c = l.startColor; c.a = pulse; l.startColor = c; l.endColor = c; }
        }
    }
}
