using System;
using System.Collections.Generic;
using DMI.Xenoarch.Audio;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    [Serializable]
    public sealed class Waypoint
    {
        public string id, bodyId, name, siteId;
        public float x, z;
        public Vector2 Xz => new Vector2(x, z);
        public bool IsCustom => string.IsNullOrEmpty(siteId);
    }

    [Serializable]
    public sealed class WaypointData
    {
        public List<Waypoint> points = new List<Waypoint>();
        public string trackedId;
    }

    /// <summary>
    /// t310u: per-planet waypoints (up to 8 each), one tracked at a time. Saved as "world.waypoints".
    /// Points stay in the save when you leave a body and come back when you return; only the on-screen set changes.
    /// </summary>
    public sealed class WaypointService
    {
        public const string SaveKey = "world.waypoints";
        public const int MaxPerBody = 8;
        public const string IdPrefix = "wp.";

        WaypointData _data = new WaypointData();
        public event Action Changed;

        public WaypointService(SaveService saves)
        {
            saves?.Register(new JsonStateSection<WaypointData>(SaveKey, 28, () => _data, v => { _data = v ?? new WaypointData(); Changed?.Invoke(); }));
        }

        public WaypointData Data => _data;
        public string TrackedId => _data.trackedId;

        public bool IsTracked(string id) => !string.IsNullOrEmpty(id) && id == _data.trackedId;

        public Waypoint Tracked
        {
            get
            {
                if (string.IsNullOrEmpty(_data.trackedId)) return null;
                return Find(_data.trackedId);
            }
        }

        public Waypoint Find(string id)
        {
            if (string.IsNullOrEmpty(id) || _data.points == null) return null;
            foreach (var w in _data.points) if (w != null && w.id == id) return w;
            return null;
        }

        public Waypoint FindForSite(string bodyId, string siteId)
        {
            if (string.IsNullOrEmpty(siteId)) return null;
            foreach (var w in OnBody(bodyId)) if (w.siteId == siteId) return w;
            return null;
        }

        public IEnumerable<Waypoint> OnBody(string bodyId)
        {
            if (_data.points == null || string.IsNullOrEmpty(bodyId)) yield break;
            foreach (var w in _data.points) if (w != null && w.bodyId == bodyId) yield return w;
        }

        public int CountOn(string bodyId)
        {
            int n = 0;
            foreach (var _ in OnBody(bodyId)) n++;
            return n;
        }

        public Waypoint TrackedOn(string bodyId)
        {
            var t = Tracked;
            return t != null && t.bodyId == bodyId ? t : null;
        }

        public Waypoint AddCustom(string bodyId, Vector2 xz, string name = null)
        {
            if (string.IsNullOrEmpty(bodyId)) return null;
            if (CountOn(bodyId) >= MaxPerBody) return null;
            int n = CountOn(bodyId) + 1;
            var w = new Waypoint
            {
                id = NextId(),
                bodyId = bodyId,
                name = string.IsNullOrEmpty(name) ? $"Waypoint {n}" : name,
                x = xz.x,
                z = xz.y
            };
            _data.points.Add(w);
            if (string.IsNullOrEmpty(_data.trackedId)) _data.trackedId = w.id;
            Changed?.Invoke();
            UISound.Play(UISoundEvent.MapPing);
            return w;
        }

        /// <summary>Places (or reuses) a waypoint on a named site / cache / landing, and tracks it.</summary>
        public Waypoint AddOrTrackSite(string bodyId, string siteId, string name, Vector2 xz)
        {
            var existing = FindForSite(bodyId, siteId);
            if (existing != null) { Track(existing.id); UISound.Play(UISoundEvent.MapPing); return existing; }
            if (CountOn(bodyId) >= MaxPerBody) return null;
            var w = new Waypoint
            {
                id = NextId(),
                bodyId = bodyId,
                name = string.IsNullOrEmpty(name) ? "Waypoint" : name,
                siteId = siteId ?? "",
                x = xz.x,
                z = xz.y
            };
            _data.points.Add(w);
            _data.trackedId = w.id;
            Changed?.Invoke();
            UISound.Play(UISoundEvent.MapPing);
            return w;
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id) || _data.points == null) return false;
            int i = _data.points.FindIndex(w => w != null && w.id == id);
            if (i < 0) return false;
            _data.points.RemoveAt(i);
            if (_data.trackedId == id) _data.trackedId = FirstId(null);
            Changed?.Invoke();
            return true;
        }

        public void ClearBody(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId) || _data.points == null) return;
            bool removedTracked = false;
            for (int i = _data.points.Count - 1; i >= 0; i--)
            {
                var w = _data.points[i];
                if (w == null || w.bodyId != bodyId) continue;
                if (w.id == _data.trackedId) removedTracked = true;
                _data.points.RemoveAt(i);
            }
            if (removedTracked) _data.trackedId = FirstId(null);
            Changed?.Invoke();
        }

        public void Track(string id)
        {
            if (Find(id) == null) return;
            if (_data.trackedId == id) return;
            _data.trackedId = id;
            Changed?.Invoke();
        }

        string FirstId(string _)
        {
            foreach (var w in _data.points) if (w != null && !string.IsNullOrEmpty(w.id)) return w.id;
            return "";
        }

        static string NextId() => IdPrefix + Guid.NewGuid().ToString("N").Substring(0, 10);

        public static bool IsId(string id) => !string.IsNullOrEmpty(id) && id.StartsWith(IdPrefix);

        /// <summary>Absolute bearing in degrees (0 = north / +z) and a short compass label (N, NE, …).</summary>
        public static string BearingLabel(Vector2 from, Vector2 to, out float degrees)
        {
            var d = to - from;
            if (d.sqrMagnitude < 0.01f) { degrees = 0f; return "—"; }
            degrees = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            if (degrees < 0f) degrees += 360f;
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int i = Mathf.RoundToInt(degrees / 45f) & 7;
            return names[i];
        }
    }
}
