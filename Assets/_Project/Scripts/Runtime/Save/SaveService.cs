using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Save
{
    /// <summary>
    /// Versioned JSON save/load. Systems register <see cref="ISaveSection"/>s; the service captures them into a
    /// <see cref="SaveFile"/>, migrates old files forward and restores sections in RestoreOrder.
    /// Plain C#: no scene lookups, no MonoBehaviour, unit-testable with <see cref="MemorySaveStorage"/>.
    /// </summary>
    public sealed class SaveService
    {
        /// <summary>Bump when a section's format changes incompatibly, and add an ISaveMigration from the old version.</summary>
        public const int CurrentVersion = 1;

        readonly ISaveStorage _storage;
        readonly Dictionary<string, ISaveSection> _sections = new Dictionary<string, ISaveSection>();
        readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        public ISaveStorage Storage => _storage;
        public event Action<string> Saved;
        public event Action<string> Loaded;

        public SaveService(ISaveStorage storage) { _storage = storage ?? throw new ArgumentNullException(nameof(storage)); }

        public void Register(ISaveSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (_sections.TryGetValue(section.Key, out var existing) && !ReferenceEquals(existing, section))
                throw new InvalidOperationException($"Save section key '{section.Key}' is already registered");
            _sections[section.Key] = section;
        }

        public void Unregister(ISaveSection section)
        {
            if (section != null && _sections.TryGetValue(section.Key, out var s) && ReferenceEquals(s, section)) _sections.Remove(section.Key);
        }

        public void AddMigration(ISaveMigration migration) => _migrations[migration.FromVersion] = migration;

        public bool HasSave(string slot) => _storage.Exists(slot);

        /// <summary>Phase 2 M1 (New Game clears development saves): deletes exactly the named slots (and their backup /
        /// temp files on disk). Nothing else in the save folder is touched. Returns how many slots existed.</summary>
        public int DeleteSlots(IEnumerable<string> slots)
        {
            int n = 0;
            if (slots == null) return 0;
            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot)) continue;
                try { if (_storage.Delete(slot)) n++; }
                catch (Exception e) { Debug.LogWarning($"[Xenoarch] Could not delete save slot '{slot}': {e.Message}"); }
            }
            return n;
        }

        public SaveFile Capture(string slot, float playSeconds = 0f)
        {
            var file = new SaveFile
            {
                version = CurrentVersion,
                gameVersion = Application.version,
                slot = slot,
                savedUtcTicks = DateTime.UtcNow.Ticks,
                playSeconds = playSeconds
            };
            foreach (var s in Ordered()) file.Set(s.Key, s.CaptureJson());
            return file;
        }

        public bool Save(string slot, float playSeconds = 0f)
        {
            try
            {
                var file = Capture(slot, playSeconds);
                _storage.Write(slot, JsonUtility.ToJson(file, true));
                Saved?.Invoke(slot);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Xenoarch] Save '{slot}' failed: {e}");
                return false;
            }
        }

        /// <summary>Reads, parses and migrates a save without applying it.</summary>
        public bool TryRead(string slot, out SaveFile file, out string error)
        {
            file = null; error = null;
            try
            {
                if (!_storage.Exists(slot)) { error = $"No save in slot '{slot}'"; return false; }
                file = JsonUtility.FromJson<SaveFile>(_storage.Read(slot));
                if (file == null) { error = "Save file is empty or corrupt"; return false; }
                return TryMigrate(file, out error);
            }
            catch (Exception e)
            {
                error = $"Save '{slot}' could not be read: {e.Message}";
                file = null;
                return false;
            }
        }

        public bool TryMigrate(SaveFile file, out string error)
        {
            error = null;
            if (file.version > CurrentVersion) { error = $"Save version {file.version} is newer than this build ({CurrentVersion})"; return false; }
            while (file.version < CurrentVersion)
            {
                if (!_migrations.TryGetValue(file.version, out var m)) { error = $"No migration from save version {file.version}"; return false; }
                m.Migrate(file);
                file.version++;
            }
            return true;
        }

        /// <summary>Applies a (migrated) save: every registered section is reset, then restored if present.</summary>
        public void Apply(SaveFile file)
        {
            foreach (var s in Ordered())
            {
                try
                {
                    s.ResetToNewGame();
                    string json = file.Get(s.Key);
                    if (!string.IsNullOrEmpty(json)) s.RestoreJson(json);
                }
                catch (Exception e) { Debug.LogError($"[Xenoarch] Restoring section '{s.Key}' failed: {e}"); }
            }
            Loaded?.Invoke(file.slot);
        }

        public void ResetAllToNewGame()
        {
            foreach (var s in Ordered()) s.ResetToNewGame();
        }

        List<ISaveSection> Ordered()
        {
            var list = new List<ISaveSection>(_sections.Values);
            list.Sort((a, b) => a.RestoreOrder != b.RestoreOrder ? a.RestoreOrder.CompareTo(b.RestoreOrder) : string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }

    /// <summary>Convenience section for a plain serializable state object.</summary>
    public sealed class JsonStateSection<T> : ISaveSection where T : class, new()
    {
        readonly Func<T> _get; readonly Action<T> _set; readonly Func<T> _fresh;
        public string Key { get; }
        public int RestoreOrder { get; }

        /// <param name="fresh">Optional new-game value (also used for saves without this key); default new T().</param>
        public JsonStateSection(string key, int restoreOrder, Func<T> get, Action<T> set, Func<T> fresh = null)
        {
            Key = key; RestoreOrder = restoreOrder; _get = get; _set = set; _fresh = fresh;
        }

        public string CaptureJson() => JsonUtility.ToJson(_get() ?? new T());
        public void RestoreJson(string json) => _set(JsonUtility.FromJson<T>(json) ?? new T());
        public void ResetToNewGame() => _set(_fresh != null ? _fresh() : new T());
    }
}
