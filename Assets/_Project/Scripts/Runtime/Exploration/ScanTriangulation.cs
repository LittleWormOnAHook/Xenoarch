using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Handheld-scanner localisation (code polish pass). Every pulse that reads a site adds an observation point; the
    /// site's "fix" quality (0..1) grows with the number of DISTINCT scan positions and, most of all, with the angle
    /// between them as seen from the site (triangulation). Re-pulsing from the same spot adds very little. The fix
    /// shrinks the scanner's bearing / distance / depth error and, once triangulated, pinpoints a site from range.
    /// Session-only (cleared on scene load): nothing is saved, so save data is unchanged.
    /// </summary>
    public static class ScanTriangulation
    {
        /// <summary>Scans closer than this (m) to an earlier scan of the same site count as a repeat.</summary>
        public const float MinBaseline = 3f;
        /// <summary>Weight of a repeat scan from (nearly) the same position.</summary>
        public const float RepeatWeight = 0.2f;
        /// <summary>Angle (deg) between two scan positions, seen from the site, that counts as full triangulation.</summary>
        public const float FullAngle = 75f;
        /// <summary>Fix quality at which a site in range is pinpointed without walking onto it.</summary>
        public const float PinpointFix = 0.78f;

        public sealed class Fix
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public float effectiveScans, angle, quality;
            public bool lastWasRepeat;
        }

        static readonly Dictionary<string, Fix> _fixes = new Dictionary<string, Fix>();
        static string _scene;

        public static void ResetIfSceneChanged(string scene) { if (_scene != scene) { _fixes.Clear(); _scene = scene; } }
        public static void Clear() => _fixes.Clear();

        public static Fix Get(string siteId) => siteId != null && _fixes.TryGetValue(siteId, out var f) ? f : null;
        public static float Quality(string siteId) => Get(siteId)?.quality ?? 0f;

        /// <summary>Records a pulse at <paramref name="from"/> that read the site at <paramref name="target"/>.</summary>
        public static Fix Observe(string siteId, Vector3 from, Vector3 target)
        {
            if (!_fixes.TryGetValue(siteId, out var f)) _fixes[siteId] = f = new Fix();
            from.y = 0f; target.y = 0f;
            bool repeat = false;
            foreach (var p in f.points) if ((p - from).sqrMagnitude < MinBaseline * MinBaseline) { repeat = true; break; }
            f.lastWasRepeat = repeat;
            f.effectiveScans += repeat ? RepeatWeight : 1f;
            if (!repeat) f.points.Add(from);
            f.angle = MaxAngle(f.points, target);
            f.quality = Evaluate(f.effectiveScans, f.angle);
            return f;
        }

        /// <summary>Fix quality from the effective scan count and the widest angle between scan positions.</summary>
        public static float Evaluate(float effectiveScans, float angleDeg)
        {
            float count = 1f - Mathf.Exp(-Mathf.Max(0f, effectiveScans) / 3f);       // 1 scan .28, 2 .49, 3 .63, 5 .81
            float spread = Mathf.Clamp01(angleDeg / FullAngle);
            return Mathf.Clamp01(0.45f * count + 0.55f * spread * Mathf.Sqrt(count));
        }

        /// <summary>Widest horizontal angle (deg, 0..180) between any two scan positions as seen from the target.</summary>
        public static float MaxAngle(List<Vector3> points, Vector3 target)
        {
            float best = 0f;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                {
                    Vector3 a = points[i] - target, b = points[j] - target;
                    if (a.sqrMagnitude < 0.01f || b.sqrMagnitude < 0.01f) continue;
                    best = Mathf.Max(best, Vector3.Angle(a, b));
                }
            return best;
        }

        /// <summary>Error multiplier for readings: 1 with no fix, down to 0.15 at a full fix.</summary>
        public static float ErrorScale(float quality) => 1f - 0.85f * Mathf.Clamp01(quality);

        /// <summary>Short guidance for the scanner panel.</summary>
        public static string Advice(Fix f)
        {
            if (f == null) return null;
            if (f.quality >= PinpointFix) return "Fix locked: triangulated.";
            if (f.lastWasRepeat) return "Same spot: move 3 m+ to the side and pulse again.";
            if (f.points.Count < 2) return "Move to the side and pulse again to triangulate.";
            if (f.angle < FullAngle * 0.5f) return "Circle wider around the signal for a better angle.";
            return "One more angle tightens the fix.";
        }
    }
}
