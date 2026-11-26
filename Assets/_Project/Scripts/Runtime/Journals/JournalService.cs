using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DMI.Xenoarch.Journals
{
    [Serializable] public sealed class JournalState { public string id; public bool read; public float collectedAt; }
    [Serializable] public sealed class JournalData { public List<JournalState> collected = new List<JournalState>(); }

    /// <summary>Collected journals (in pickup order) and their read flags. Saved under <see cref="Key"/>; older saves start empty.</summary>
    public sealed class JournalService
    {
        public const string Key = "journals.collected";
        public const string ResourceFolder = "Journals";
        public JournalData Data { get; private set; } = new JournalData();
        public event Action Changed;
        public Func<float> Clock;

        static JournalEntry[] _all;
        /// <summary>Every entry in Resources/Journals, by order then id.</summary>
        public static JournalEntry[] All => _all ??= Resources.LoadAll<JournalEntry>(ResourceFolder).Where(e => e != null && !string.IsNullOrEmpty(e.Id)).OrderBy(e => e.order).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
        public static JournalEntry Find(string id) { foreach (var e in All) if (e.Id == id) return e; return null; }

        public JournalService(Save.SaveService saves) { saves?.Register(new Section(this)); }

        public bool Has(string id) => Data.collected.Any(c => c.id == id);
        public bool IsRead(string id) => Data.collected.Any(c => c.id == id && c.read);
        public int UnreadCount => Data.collected.Count(c => !c.read);
        public IEnumerable<JournalEntry> Collected => Data.collected.Select(c => Find(c.id)).Where(e => e != null);

        /// <summary>Adds the entry (once). Returns true when it is new.</summary>
        public bool Collect(string id)
        {
            if (string.IsNullOrEmpty(id) || Has(id)) return false;
            Data.collected.Add(new JournalState { id = id, collectedAt = Clock != null ? Clock() : 0f });
            Changed?.Invoke();
            return true;
        }

        public void MarkRead(string id)
        {
            var s = Data.collected.FirstOrDefault(c => c.id == id);
            if (s == null || s.read) return;
            s.read = true; Changed?.Invoke();
        }

        sealed class Section : Save.ISaveSection
        {
            readonly JournalService _s;
            public Section(JournalService s) { _s = s; }
            public string Key => JournalService.Key;
            public int RestoreOrder => 20;
            public string CaptureJson() => JsonUtility.ToJson(_s.Data);
            public void RestoreJson(string json) { _s.Data = JsonUtility.FromJson<JournalData>(json) ?? new JournalData(); _s.Changed?.Invoke(); }
            public void ResetToNewGame() { _s.Data = new JournalData(); _s.Changed?.Invoke(); }
        }
    }
}
