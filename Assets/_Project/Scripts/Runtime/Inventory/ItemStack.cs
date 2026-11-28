using System;
using UnityEngine;

namespace DMI.Xenoarch.Inventory
{
    /// <summary>One inventory slot. Empty slots have an empty itemId (JsonUtility cannot store null list entries).
    /// originBodyId, originSiteId and grade are optional; missing fields in older saves stay empty / 0.</summary>
    [Serializable]
    public sealed class ItemStack
    {
        public string itemId;
        public int count;
        [Tooltip("Set only for unique-instance items (artifacts, recovered technology).")]
        public string instanceId;
        [Tooltip("Optional body id where this stack was gathered. Empty on older saves.")]
        public string originBodyId;
        [Tooltip("Optional dig site or node id. Empty on older saves.")]
        public string originSiteId;
        [Tooltip("0 = typical / unspecified. Otherwise 0-1 purity or grade. Missing in older saves.")]
        public float grade;

        public bool IsEmpty => string.IsNullOrEmpty(itemId) || count <= 0;

        public ItemStack() { }
        public ItemStack(string itemId, int count, string instanceId = null)
        {
            this.itemId = itemId; this.count = count; this.instanceId = instanceId;
        }

        public void Clear()
        {
            itemId = null; count = 0; instanceId = null;
            originBodyId = null; originSiteId = null; grade = 0f;
        }

        public ItemStack Clone()
        {
            return new ItemStack
            {
                itemId = itemId, count = count, instanceId = instanceId,
                originBodyId = originBodyId, originSiteId = originSiteId, grade = grade
            };
        }

        public void Assign(ItemStack src)
        {
            if (src == null) { Clear(); return; }
            itemId = src.itemId; count = src.count; instanceId = src.instanceId;
            originBodyId = src.originBodyId; originSiteId = src.originSiteId; grade = src.grade;
        }

        public bool SameMeta(ItemStack other)
        {
            if (other == null) return false;
            return string.Equals(originBodyId ?? "", other.originBodyId ?? "")
                && string.Equals(originSiteId ?? "", other.originSiteId ?? "")
                && Mathf.Abs(grade - other.grade) < 0.0001f;
        }
    }
}
