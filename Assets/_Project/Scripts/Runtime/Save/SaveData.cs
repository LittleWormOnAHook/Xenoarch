using System;
using System.Collections.Generic;

namespace DMI.Xenoarch.Save
{
    /// <summary>
    /// A system that owns one section of the save. Adapted from the ASH IASHSaveable pattern (key + JSON section +
    /// restore order); adds an explicit new-game reset so systems never rely on scene start-up order.
    /// </summary>
    public interface ISaveSection
    {
        string Key { get; }
        /// <summary>Lower restores first (core 0, world 10, later systems 20+).</summary>
        int RestoreOrder { get; }
        string CaptureJson();
        void RestoreJson(string json);
        void ResetToNewGame();
    }

    [Serializable]
    public sealed class SaveSectionRecord
    {
        public string key;
        public string json;
    }

    /// <summary>Versioned save file envelope. Sections are opaque JSON owned by their systems.</summary>
    [Serializable]
    public sealed class SaveFile
    {
        public int version;
        public string gameVersion;
        public string slot;
        public long savedUtcTicks;
        public float playSeconds;
        public List<SaveSectionRecord> sections = new List<SaveSectionRecord>();

        public string Get(string key)
        {
            foreach (var s in sections) if (s.key == key) return s.json;
            return null;
        }

        public void Set(string key, string json)
        {
            foreach (var s in sections) if (s.key == key) { s.json = json; return; }
            sections.Add(new SaveSectionRecord { key = key, json = json });
        }
    }

    /// <summary>Upgrades a save from <see cref="FromVersion"/> to FromVersion + 1.</summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        void Migrate(SaveFile file);
    }
}
