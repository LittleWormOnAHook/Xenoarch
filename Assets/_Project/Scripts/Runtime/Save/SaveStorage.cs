using System;
using System.Collections.Generic;
using System.IO;

namespace DMI.Xenoarch.Save
{
    public interface ISaveStorage
    {
        bool Exists(string slot);
        string Read(string slot);
        void Write(string slot, string text);
        bool Delete(string slot);
        IEnumerable<string> ListSlots();
    }

    /// <summary>JSON files under a folder (default persistentDataPath/Saves). Writes atomically (temp file + replace).</summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        public string Folder { get; }
        public FileSaveStorage(string folder) { Folder = folder; }

        string PathFor(string slot) => Path.Combine(Folder, Sanitize(slot) + ".json");

        static string Sanitize(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("Empty save slot name");
            foreach (var c in Path.GetInvalidFileNameChars()) slot = slot.Replace(c, '_');
            return slot;
        }

        public bool Exists(string slot) => File.Exists(PathFor(slot));
        public string Read(string slot) => File.ReadAllText(PathFor(slot));

        public void Write(string slot, string text)
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor(slot), tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }

        public bool Delete(string slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public IEnumerable<string> ListSlots()
        {
            if (!Directory.Exists(Folder)) yield break;
            foreach (var f in Directory.GetFiles(Folder, "*.json")) yield return Path.GetFileNameWithoutExtension(f);
        }
    }

    /// <summary>In-memory storage for tests and tooling.</summary>
    public sealed class MemorySaveStorage : ISaveStorage
    {
        readonly Dictionary<string, string> _files = new Dictionary<string, string>();
        public bool Exists(string slot) => _files.ContainsKey(slot);
        public string Read(string slot) => _files[slot];
        public void Write(string slot, string text) => _files[slot] = text;
        public bool Delete(string slot) => _files.Remove(slot);
        public IEnumerable<string> ListSlots() => _files.Keys;
    }
}
