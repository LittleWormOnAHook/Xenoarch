using System;
using System.Collections.Generic;

namespace DMI.Xenoarch.Inventory
{
    /// <summary>Saved hotbar assignment (t108u). Empty list = default layout.</summary>
    [Serializable]
    public sealed class HotbarData
    {
        public List<string> slots = new List<string>();
    }

    /// <summary>
    /// Hotbar: 8 slots on keys 1-8 holding item ids (a reference to a backpack item, not a separate stack). Slot 1 is
    /// the handheld scanner (fixed: 1 / hold 1 is the scanner and refined scanner). Slots 2-8 are assigned by dragging
    /// items from the inventory; a dig tool on slot N is equipped with key N. The default layout matches the old fixed
    /// keys (2 shovel, 3 precision, 4 brush, 5 drill), so older saves behave as before.
    /// </summary>
    public sealed class Hotbar
    {
        public const int Size = 8;
        public const string Key = "player.hotbar";
        public HotbarData Data = new HotbarData();
        public event Action Changed;
        readonly Func<string> _scannerId;
        readonly Func<IReadOnlyList<string>> _defaults;

        public Hotbar(Func<string> scannerId, Func<IReadOnlyList<string>> defaultTools)
        {
            _scannerId = scannerId; _defaults = defaultTools;
        }

        public bool IsDefault => Data == null || Data.slots == null || Data.slots.Count == 0;

        /// <summary>Item id on slot i (0 = key 1), or null.</summary>
        public string At(int i)
        {
            if (i < 0 || i >= Size) return null;
            if (i == 0) return _scannerId?.Invoke();
            if (IsDefault) { var d = _defaults?.Invoke(); return d != null && i - 1 < d.Count ? d[i - 1] : null; }
            return i < Data.slots.Count ? NullIfEmpty(Data.slots[i]) : null;
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        void Materialise()
        {
            if (!IsDefault) { while (Data.slots.Count < Size) Data.slots.Add(""); return; }
            var l = new List<string>(Size);
            for (int i = 0; i < Size; i++) l.Add(At(i) ?? "");
            Data = new HotbarData { slots = l };
        }

        /// <summary>Put an item on slot i (1..7; slot 0 is the scanner). Removes it from any other slot. False if refused.</summary>
        public bool Assign(int i, string itemId)
        {
            if (i <= 0 || i >= Size || string.IsNullOrEmpty(itemId)) return false;
            Materialise();
            for (int k = 1; k < Size; k++) if (Data.slots[k] == itemId) Data.slots[k] = "";
            Data.slots[i] = itemId;
            Changed?.Invoke();
            return true;
        }

        public void Clear(int i)
        {
            if (i <= 0 || i >= Size) return;
            Materialise();
            Data.slots[i] = "";
            Changed?.Invoke();
        }

        /// <summary>Swap two slots (1..7).</summary>
        public void Swap(int a, int b)
        {
            if (a <= 0 || b <= 0 || a >= Size || b >= Size || a == b) return;
            Materialise();
            (Data.slots[a], Data.slots[b]) = (Data.slots[b], Data.slots[a]);
            Changed?.Invoke();
        }

        public void Set(HotbarData d) { Data = d ?? new HotbarData(); Changed?.Invoke(); }
    }
}
