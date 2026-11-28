using System;
using UnityEngine;

namespace DMI.Xenoarch.Audio
{
    /// <summary>
    /// Clip slots for <see cref="UISound"/>. <see cref="Entry.clip"/> holds the committed Kenney CC0 fallback
    /// (always valid in a fresh clone). <see cref="Entry.vendorClip"/> holds a Pro Sound Collection clip when
    /// that pack is extracted locally; missing vendor refs are skipped so a clone without the pack still plays Kenney.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Audio/UI Sound Catalog", fileName = "UISoundCatalog")]
    public sealed class UISoundCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public UISoundEvent evt;
            [Tooltip("Kenney CC0 fallback, committed with the repo.")]
            public AudioClip clip;
            [Tooltip("Optional extra Kenney (or other committed) variations.")]
            public AudioClip[] extras;
            [Tooltip("Preferred Pro Sound Collection clip. May be missing on a clone that does not have the pack.")]
            public AudioClip vendorClip;
            [Tooltip("Optional extra Pro Sound variations. Null slots are ignored.")]
            public AudioClip[] vendorExtras;
            [Range(0f, 1f)] public float volume = 1f;
            public Vector2 pitch = new Vector2(0.97f, 1.03f);
            [Tooltip("Minimum seconds between plays of this event (unscaled). Hover/slider/typing use this to avoid spam.")]
            public float cooldown;
        }

        public Entry[] entries = Array.Empty<Entry>();

        [Header("Master")]
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Mute all UI sounds while a loading screen is up.")]
        public bool muteWhileLoading = true;

        public Entry Get(UISoundEvent evt)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && entries[i].evt == evt) return entries[i];
            return null;
        }

        /// <summary>Prefers a present vendor (Pro Sound) clip; otherwise the Kenney fallback.</summary>
        public AudioClip Pick(UISoundEvent evt)
        {
            var e = Get(evt);
            if (e == null) return null;
            var vendor = PickFrom(e.vendorClip, e.vendorExtras);
            return vendor != null ? vendor : PickFrom(e.clip, e.extras);
        }

        static AudioClip PickFrom(AudioClip primary, AudioClip[] extras)
        {
            int extra = 0;
            if (extras != null)
                for (int i = 0; i < extras.Length; i++)
                    if (extras[i] != null) extra++;
            int n = (primary != null ? 1 : 0) + extra;
            if (n <= 0) return null;
            if (n == 1) return primary != null ? primary : First(extras);
            int pick = UnityEngine.Random.Range(0, n);
            if (primary != null)
            {
                if (pick == 0) return primary;
                pick--;
            }
            if (extras == null) return primary;
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i] == null) continue;
                if (pick == 0) return extras[i];
                pick--;
            }
            return primary;
        }

        static AudioClip First(AudioClip[] extras)
        {
            if (extras == null) return null;
            for (int i = 0; i < extras.Length; i++)
                if (extras[i] != null) return extras[i];
            return null;
        }
    }
}
