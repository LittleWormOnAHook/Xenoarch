using DMI.Xenoarch.Items;
using DMI.Xenoarch.World;
using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>
    /// World pickup that grants an item once per playthrough. Requires a <see cref="WorldObjectId"/>; once taken it is
    /// recorded in WorldPersistence and stays gone after reloads (no respawn, no duplication).
    /// </summary>
    [RequireComponent(typeof(WorldObjectId))]
    public sealed class ItemPickupInteractable : MonoBehaviour, IInteractable
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;

        WorldObjectId _id;
        public Transform InteractionTransform => transform;

        void Awake() => _id = GetComponent<WorldObjectId>();

        void Start()
        {
            var session = Core.XenoarchRuntime.Session;
            if (session != null && session.World.IsConsumed(_id.Id)) gameObject.SetActive(false);
        }

        public float GetPriority(in InteractionContext c) => item != null && c.Session != null ? 1f : -1f;
        public string GetPrompt(in InteractionContext c) => item != null ? $"Take {item.DisplayNameOrId}" + (count > 1 ? $" x{count}" : "") : null;
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            var s = c.Session;
            if (s == null || item == null || s.World.IsConsumed(_id.Id)) return false;
            int left = s.Inventory.Add(item.Id, count);
            if (left == count) { Debug.Log("[Xenoarch] Inventory full"); return false; }
            if (left > 0) { count = left; return true; } // partial pickup: object stays with the remainder (not persisted in 1A)
            s.World.MarkConsumed(_id.Id);
            Debug.Log($"[Xenoarch] Picked up {item.Id} x{count} ({_id.Id})");
            gameObject.SetActive(false);
            return true;
        }
    }
}
