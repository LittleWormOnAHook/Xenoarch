using System;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    public enum SonicSfx { Equip, BeamLoop, FanLoop, Change, Overheat, Empty, Jar }

    /// <summary>
    /// Sound slots for the sonic tools. Same pattern as the UI sound catalogs: a committed fallback clip (Kenney CC0, or none
    /// for loops, which fall back to a generated hum) plus a Pro Sound Collection vendorClip that is gitignored; a missing
    /// vendor reference is skipped. Built by Tools > Xenoarch > Setup > Sonic Tools.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Sonic Tool Audio", fileName = "SonicToolAudio")]
    public sealed class SonicToolAudioCatalog : ScriptableObject
    {
        public const string Resource = "Audio/SonicToolAudio";

        [Serializable]
        public sealed class Entry
        {
            public SonicSfx id;
            public AudioClip clip;
            public AudioClip vendorClip;
            public AudioClip[] vendorExtras;
            [Range(0f, 1f)] public float volume = 0.6f;
        }

        public Entry[] entries = new Entry[0];

        public AudioClip Pick(SonicSfx id, out float volume)
        {
            volume = 0.6f;
            if (entries == null) return null;
            foreach (var e in entries)
            {
                if (e == null || e.id != id) continue;
                volume = e.volume;
                if (e.vendorClip != null)
                {
                    if (e.vendorExtras != null && e.vendorExtras.Length > 0)
                    {
                        int k = UnityEngine.Random.Range(0, e.vendorExtras.Length + 1);
                        if (k > 0 && e.vendorExtras[k - 1] != null) return e.vendorExtras[k - 1];
                    }
                    return e.vendorClip;
                }
                return e.clip;
            }
            return null;
        }
    }
}
