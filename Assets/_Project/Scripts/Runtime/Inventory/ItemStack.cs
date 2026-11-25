using System;
using UnityEngine;

namespace DMI.Xenoarch.Inventory
{
    /// <summary>One inventory slot. Empty slots have an empty itemId (JsonUtility cannot store null list entries).</summary>
    [Serializable]
    public sealed class ItemStack
    {
        public string itemId;
        public int count;
        [Tooltip("Set only for unique-instance items (artifacts, recovered technology).")]
        public string instanceId;

        public bool IsEmpty => string.IsNullOrEmpty(itemId) || count <= 0;

        public ItemStack() { }
        public ItemStack(string itemId, int count, string instanceId = null)
        {
            this.itemId = itemId; this.count = count; this.instanceId = instanceId;
        }

        public void Clear() { itemId = null; count = 0; instanceId = null; }
        public ItemStack Clone() => new ItemStack(itemId, count, instanceId);
    }
}
