using System;
using System.Collections.Generic;
using DMI.Xenoarch.Save;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Objectives
{
    public enum NoticeKind { Discovery, Research, Recovery, Objective, System, Note }

    [Serializable]
    public sealed class CaptainsLogEntry
    {
        public float time;
        public NoticeKind kind;
        public string id;
        public string text;
        public bool playerNote;
    }

    [Serializable]
    public sealed class CaptainsLogState
    {
        public bool oldestFirst;
        public List<CaptainsLogEntry> entries = new List<CaptainsLogEntry>();
    }

    /// <summary>
    /// Phase 2 M2 Captain's Log. Newest-first by default, with an Oldest First option, player notes, and
    /// deduplication of system entries by id (and of identical player-note text). Saved as log.captain.
    /// </summary>
    public sealed class CaptainsLogService
    {
        public const string Key = "log.captain";
        public CaptainsLogState State { get; private set; } = new CaptainsLogState();
        public Func<float> Clock = () => 0f;
        public event Action Changed;

        public CaptainsLogService(SaveService saves)
        {
            saves?.Register(new JsonStateSection<CaptainsLogState>(Key, 26, () => State, v => { State = v ?? new CaptainsLogState(); Changed?.Invoke(); }));
        }

        public bool OldestFirst
        {
            get => State.oldestFirst;
            set { if (State.oldestFirst == value) return; State.oldestFirst = value; Changed?.Invoke(); }
        }

        public IEnumerable<CaptainsLogEntry> Ordered()
        {
            var e = State.entries;
            if (e == null || e.Count == 0) yield break;
            if (State.oldestFirst) { for (int i = 0; i < e.Count; i++) yield return e[i]; }
            else { for (int i = e.Count - 1; i >= 0; i--) yield return e[i]; }
        }

        public bool HasId(string id)
        {
            if (string.IsNullOrEmpty(id) || State.entries == null) return false;
            foreach (var e in State.entries) if (e != null && e.id == id) return true;
            return false;
        }

        public bool HasNoteText(string text)
        {
            if (string.IsNullOrEmpty(text) || State.entries == null) return false;
            foreach (var e in State.entries)
                if (e != null && e.playerNote && string.Equals(e.text, text, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Adds an entry. System events with a duplicate id, and player notes with identical text, are skipped.</summary>
        public bool Add(NoticeKind kind, string id, string text, bool playerNote = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (playerNote)
            {
                if (HasNoteText(text)) return false;
                kind = NoticeKind.Note;
            }
            else if (!string.IsNullOrEmpty(id) && HasId(id)) return false;

            if (State.entries == null) State.entries = new List<CaptainsLogEntry>();
            State.entries.Add(new CaptainsLogEntry
            {
                time = Clock(),
                kind = kind,
                id = id ?? "",
                text = text,
                playerNote = playerNote,
            });
            Changed?.Invoke();
            return true;
        }
    }

    /// <summary>
    /// Phase 2 M2 notification pipeline: deduped Captain's Log entry plus a toast. Systems post here instead of
    /// calling Toast directly, so the same event is never logged twice.
    /// </summary>
    public sealed class NotificationService
    {
        readonly CaptainsLogService _log;
        public event Action<CaptainsLogEntry> Posted;

        public NotificationService(CaptainsLogService log) { _log = log; }

        public bool Post(NoticeKind kind, string id, string text, float toastSeconds = 5f)
        {
            if (_log == null || !_log.Add(kind, id, text)) return false;
            if (toastSeconds > 0f) Toast.Show(text, toastSeconds);
            Posted?.Invoke(_log.State.entries[_log.State.entries.Count - 1]);
            return true;
        }

        /// <summary>Player-authored Captain's Log note. No toast. False if the same text is already in the log.</summary>
        public bool AddNote(string text) => _log != null && _log.Add(NoticeKind.Note, null, text, true);
    }
}