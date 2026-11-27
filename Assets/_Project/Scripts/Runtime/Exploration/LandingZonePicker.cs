using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Finds semi-flat ground for the shuttle near a target (dig site): searches rings around the target, outside every
    /// dig grid (+ clearance), and keeps the candidate whose footprint has a low slope, a small height step and no
    /// obstacles; nearest/flattest wins. Pure maths over a height function so it runs in edit mode (setup places the
    /// <see cref="LandingZone"/> markers) and at runtime (sites without a marker).
    /// </summary>
    public static class LandingZonePicker
    {
        [Serializable]
        public struct Rules
        {
            [Tooltip("Max ground slope under the shuttle (degrees).")] public float maxSlope;
            [Tooltip("Max height difference across the footprint (m).")] public float maxStep;
            [Tooltip("Shuttle footprint radius from its pivot (m).")] public float footprint;
            [Tooltip("Extra gap between the footprint and any dig grid (m).")] public float gridClearance;
            [Tooltip("Furthest the shuttle may land from the target (m).")] public float maxDistance;
            public float ringStep;
            public int angles;
            public static Rules Default => new Rules { maxSlope = 15f, maxStep = 1.5f, footprint = 8f, gridClearance = 4f, maxDistance = 90f, ringStep = 3f, angles = 20 };
        }

        public struct Result
        {
            public bool found, relaxed;
            public Vector3 position;
            public float yaw, slope, step, distance;
            public override string ToString() => found ? $"({position.x:0.0}, {position.y:0.0}, {position.z:0.0}) {distance:0} m from target, slope {slope:0.0} deg, step {step:0.00} m{(relaxed ? " (relaxed rules)" : "")}" : "no landing zone found";
        }

        /// <summary>Ground height + normal at x/z; false when off the terrain.</summary>
        public delegate bool HeightFn(float x, float z, out float y, out Vector3 normal);

        public static HeightFn ForTerrain(Terrain t)
        {
            return (float x, float z, out float y, out Vector3 n) =>
            {
                y = 0f; n = Vector3.up;
                if (t == null || t.terrainData == null) return false;
                var tp = t.transform.position; var size = t.terrainData.size;
                float u = (x - tp.x) / size.x, v = (z - tp.z) / size.z;
                if (u < 0f || v < 0f || u > 1f || v > 1f) return false;
                if (IsHole(t, u, v)) return false;
                y = tp.y + t.SampleHeight(new Vector3(x, 0f, z));
                n = t.terrainData.GetInterpolatedNormal(u, v);
                return true;
            };
        }

        /// <summary>Terrain if the point is on it and not a hole, otherwise a downward raycast (mesh / leftover colliders).</summary>
        public static HeightFn ForWorld(Terrain t, Transform ignore = null)
        {
            var terr = ForTerrain(t);
            return (float x, float z, out float y, out Vector3 n) =>
            {
                if (terr(x, z, out y, out n)) return true;
                y = 0f; n = Vector3.up;
                var origin = new Vector3(x, 800f, z);
                var hits = Physics.RaycastAll(origin, Vector3.down, 1600f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                    if (Vector3.Angle(h.normal, Vector3.up) > 75f) continue;
                    y = h.point.y; n = h.normal;
                    return true;
                }
                return false;
            };
        }

        public static bool IsHole(Terrain t, float u, float v)
        {
            if (t == null || t.terrainData == null) return false;
            int res = t.terrainData.holesResolution;
            if (res <= 0) return false;
            int hx = Mathf.Clamp(Mathf.FloorToInt(u * res), 0, res - 1);
            int hz = Mathf.Clamp(Mathf.FloorToInt(v * res), 0, res - 1);
            return t.terrainData.IsHole(hx, hz);
        }

        /// <summary>Nearest valid ground around <paramref name="around"/>, or false if nothing is within maxDist.</summary>
        public static bool FindOnGround(HeightFn height, Vector3 around, float maxDist, out Vector3 pos)
        {
            pos = around;
            if (height == null) return false;
            if (height(around.x, around.z, out float y, out _)) { pos = new Vector3(around.x, y, around.z); return true; }
            for (float d = 4f; d <= Mathf.Max(8f, maxDist); d += 4f)
            {
                int n = Mathf.Max(8, (int)(d * 0.6f));
                for (int i = 0; i < n; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / n;
                    float x = around.x + Mathf.Sin(a) * d, z = around.z + Mathf.Cos(a) * d;
                    if (height(x, z, out y, out _)) { pos = new Vector3(x, y, z); return true; }
                }
            }
            return false;
        }

        /// <param name="target">Dig site centre (or spawn point for the base zone).</param>
        /// <param name="avoid">Circles the footprint must stay out of (every dig grid in the scene, incl. the target's).</param>
        /// <param name="blocked">Optional obstacle test: ground centre + radius, true = something is in the way.</param>
        public static Result Pick(Vector3 target, IList<(Vector3 centre, float radius)> avoid, Rules r, HeightFn height, Func<Vector3, float, bool> blocked = null, float yawOffset = 0f)
        {
            var res = Search(target, avoid, r, height, blocked, yawOffset, 1f);
            if (res.found) return res;
            var far = r; far.maxDistance *= 2f; // same rules, further out
            res = Search(target, avoid, far, height, blocked, yawOffset, 1f);
            if (res.found) return res;
            var loose = far; loose.maxSlope *= 1.5f; loose.maxStep *= 2f; // last resort: flattest ground found
            res = Search(target, avoid, loose, height, blocked, yawOffset, 4f);
            res.relaxed = res.found;
            return res;
        }

        static Result Search(Vector3 target, IList<(Vector3 centre, float radius)> avoid, Rules r, HeightFn height, Func<Vector3, float, bool> blocked, float yawOffset, float flatWeight)
        {
            var best = new Result(); float bestScore = float.MaxValue;
            float R = Mathf.Max(1f, r.footprint);
            float start = 0f;
            if (avoid != null) foreach (var a in avoid) if ((Flat(a.centre) - Flat(target)).sqrMagnitude < 0.01f) start = Mathf.Max(start, a.radius);
            start += R + r.gridClearance;
            int angles = Mathf.Max(6, r.angles);
            float step = Mathf.Max(1f, r.ringStep);
            int ring = 0;
            for (float d = start; d <= Mathf.Max(start, r.maxDistance); d += step, ring++)
            {
                for (int k = 0; k < angles; k++)
                {
                    float ang = (k + (ring & 1) * 0.5f) * Mathf.PI * 2f / angles;
                    var c = new Vector3(target.x + Mathf.Sin(ang) * d, 0f, target.z + Mathf.Cos(ang) * d);
                    if (!Clear(c, R + r.gridClearance, avoid)) continue;
                    if (!Measure(c, R, height, out float y, out float slope, out float st)) continue;
                    if (slope > r.maxSlope || st > r.maxStep) continue;
                    c.y = y;
                    float score = d + (slope * 1.0f + st * 8f) * flatWeight;
                    if (score >= bestScore) continue;
                    if (blocked != null && blocked(c, R)) continue;
                    bestScore = score;
                    var to = target - c;
                    best = new Result { found = true, position = c, slope = slope, step = st, distance = d, yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + yawOffset };
                }
                if (best.found && flatWeight <= 1f && d > best.distance + 3f * step) break; // nothing nearer can beat it any more
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static bool Clear(Vector3 c, float pad, IList<(Vector3 centre, float radius)> avoid)
        {
            if (avoid == null) return true;
            foreach (var a in avoid)
            {
                float min = a.radius + pad;
                if ((Flat(a.centre) - c).sqrMagnitude < min * min) return false;
            }
            return true;
        }

        /// <summary>Centre height, slope at the centre and the max height difference over 17 footprint samples.</summary>
        public static bool Measure(Vector3 c, float R, HeightFn height, out float y, out float slope, out float step)
        {
            y = 0f; slope = 90f; step = float.MaxValue;
            if (!height(c.x, c.z, out y, out var n)) return false;
            float lo = y, hi = y, maxSlope = Vector3.Angle(n, Vector3.up);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                for (int ring = 1; ring <= 2; ring++)
                {
                    float rr = R * ring * 0.5f;
                    if (!height(c.x + Mathf.Sin(a) * rr, c.z + Mathf.Cos(a) * rr, out float h, out var hn)) return false;
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                    if (ring == 1) maxSlope = Mathf.Max(maxSlope, Vector3.Angle(hn, Vector3.up));
                }
            }
            slope = maxSlope; step = hi - lo;
            return true;
        }

        /// <summary>Shuttle mesh bounds in the root's local space (works on inactive instances and prefab contents).</summary>
        public static Bounds LocalBounds(Transform root)
        {
            bool any = false; var b = new Bounds();
            var toLocal = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled) continue;
                var mb = mf.sharedMesh.bounds; var m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        public static float FootprintOf(Bounds local) =>
            Mathf.Max(Mathf.Abs(local.min.x), Mathf.Abs(local.max.x), Mathf.Abs(local.min.z), Mathf.Abs(local.max.z));
    }
}
