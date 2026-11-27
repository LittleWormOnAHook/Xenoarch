using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Per planet scene: on load every zone is switched off, then <see cref="ShuttleDirector"/> switches on only the zone
    /// for the chosen landing site (or the base zone), so the shuttle and arrival point are scene objects, not runtime
    /// spawns. Saved state to apply later (shuttle damage/cargo) has no data yet; the shuttle is always present.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class LandingZoneSet : MonoBehaviour
    {
        public readonly List<LandingZoneSlot> Slots = new List<LandingZoneSlot>();
        public LandingZoneSlot Active { get; private set; }

        void Awake()
        {
            Collect();
            foreach (var s in Slots) s.gameObject.SetActive(false);
        }

        void Collect()
        {
            Slots.Clear();
            foreach (var s in FindObjectsByType<LandingZoneSlot>(FindObjectsInactive.Include))
                if (s != null && s.gameObject.scene == gameObject.scene) Slots.Add(s);
        }

        public static LandingZoneSet In(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var s in FindObjectsByType<LandingZoneSet>(FindObjectsInactive.Include))
                if (s.gameObject.scene == scene) return s;
            return null;
        }

        /// <summary>Switch on the zone for <paramref name="siteId"/> (else the base zone, else the first) and off the rest.</summary>
        public LandingZoneSlot Activate(string siteId)
        {
            if (Slots.Count == 0) Collect();
            siteId ??= "";
            LandingZoneSlot pick = null;
            foreach (var s in Slots) if (s != null && (s.siteId ?? "") == siteId) { pick = s; break; }
            if (pick == null) foreach (var s in Slots) if (s != null && string.IsNullOrEmpty(s.siteId)) { pick = s; break; }
            if (pick == null && Slots.Count > 0) pick = Slots[0];
            return Activate(pick);
        }

        /// <summary>Switch on the zone whose siteId matches exactly. No fallback to the base or first pad.</summary>
        public LandingZoneSlot ActivateExact(string siteId)
        {
            if (Slots.Count == 0) Collect();
            siteId ??= "";
            if (string.IsNullOrEmpty(siteId)) return null;
            LandingZoneSlot pick = null;
            foreach (var s in Slots) if (s != null && (s.siteId ?? "") == siteId) { pick = s; break; }
            return pick == null ? null : Activate(pick);
        }

        /// <summary>Switch on the zone nearest <paramref name="p"/> (saved pose / Continue).</summary>
        public LandingZoneSlot ActivateNearest(Vector3 p)
        {
            LandingZoneSlot best = null; float bd = float.MaxValue;
            foreach (var s in Slots)
            {
                if (s == null) continue;
                var q = s.shuttle != null ? s.shuttle.transform.position : s.transform.position;
                float d = new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return Activate(best);
        }

        LandingZoneSlot Activate(LandingZoneSlot pick)
        {
            foreach (var s in Slots) if (s != null && s != pick) s.gameObject.SetActive(false);
            if (pick != null)
            {
                pick.gameObject.SetActive(true);
                if (pick.shuttle != null) pick.shuttle.SetActive(true);
                if (pick.spawn != null) pick.spawn.gameObject.SetActive(true);
            }
            Active = pick;
            return pick;
        }
    }
}
