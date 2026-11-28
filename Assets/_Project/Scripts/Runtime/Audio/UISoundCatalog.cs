using System;
using UnityEngine;

namespace DMI.Xenoarch.Audio
{
    /// <summary>
    /// Clip slots for <see cref="UISound"/>. Temporary defaults are Kenney Interface / Sci-Fi (CC0) already in
    /// Xenoarch_Free. Pro Sound Collection assignment is pending conversion of the Unreal .uasset pack.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Audio/UI Sound Catalog", fileName = "UISoundCatalog")]
    public sealed class UISoundCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public UISoundEvent evt;
            public AudioClip clip;
            [Tooltip("Optional extra variations; a clip is picked at random with the primary clip.")]
            public AudioClip[] extras;
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

        public AudioClip Pick(UISoundEvent evt)
        {
            var e = Get(evt);
            if (e == null) return null;
            int extra = 0;
            if (e.extras != null)
                for (int i = 0; i < e.extras.Length; i++)
                    if (e.extras[i] != null) extra++;
            int n = (e.clip != null ? 1 : 0) + extra;
            if (n <= 0) return null;
            if (n == 1) return e.clip != null ? e.clip : FirstExtra(e);
            int pick = UnityEngine.Random.Range(0, n);
            if (e.clip != null)
            {
                if (pick == 0) return e.clip;
                pick--;
            }
            if (e.extras == null) return e.clip;
            for (int i = 0; i < e.extras.Length; i++)
            {
                if (e.extras[i] == null) continue;
                if (pick == 0) return e.extras[i];
                pick--;
            }
            return e.clip;
        }

        static AudioClip FirstExtra(Entry e)
        {
            if (e.extras == null) return null;
            for (int i = 0; i < e.extras.Length; i++)
                if (e.extras[i] != null) return e.extras[i];
            return null;
        }
    }
}
