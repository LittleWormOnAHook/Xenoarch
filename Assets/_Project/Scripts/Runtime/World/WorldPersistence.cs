using System;
using System.Collections.Generic;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// Player-caused changes to authored/generated world content, keyed by stable world-object ids.
    /// Phase 1A: "consumed" objects (picked-up items) never respawn. Later phases add site/excavation state sections
    /// alongside this rather than inside scene objects.
    /// </summary>
    public sealed class WorldPersistence : ISaveSection
    {
        [Serializable]
        sealed class Data { public List<string> consumed = new List<string>(); }

        readonly HashSet<string> _consumed = new HashSet<string>();
        public string Key { get; }
        public int RestoreOrder { get; }
        public event Action<string> Consumed;

        public WorldPersistence(string key, int restoreOrder) { Key = key; RestoreOrder = restoreOrder; }

        public bool IsConsumed(string worldId) => !string.IsNullOrEmpty(worldId) && _consumed.Contains(worldId);

        /// <summary>Returns false if it was already consumed (callers must then not grant rewards again).</summary>
        public bool MarkConsumed(string worldId)
        {
            if (string.IsNullOrEmpty(worldId) || !_consumed.Add(worldId)) return false;
            Consumed?.Invoke(worldId);
            return true;
        }

        public int ConsumedCount => _consumed.Count;

        public string CaptureJson()
        {
            var d = new Data();
            d.consumed.AddRange(_consumed);
            d.consumed.Sort(StringComparer.Ordinal);
            return JsonUtility.ToJson(d);
        }

        public void RestoreJson(string json)
        {
            _consumed.Clear();
            var d = JsonUtility.FromJson<Data>(json);
            if (d?.consumed != null) foreach (var id in d.consumed) if (!string.IsNullOrEmpty(id)) _consumed.Add(id);
        }

        public void ResetToNewGame() => _consumed.Clear();
    }
}
