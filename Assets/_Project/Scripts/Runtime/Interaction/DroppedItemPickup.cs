using DMI.Xenoarch.Audio;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>
    /// Temporary world pickup for an item the player dropped from the inventory. Reuses the same interact prompt as
    /// placed pickups, but is not a once-per-playthrough <see cref="WorldObjectId"/> object (it can be taken again).
    /// Runtime-only: leaving the scene leaves the drop behind.
    /// </summary>
    public sealed class DroppedItemPickup : MonoBehaviour, IInteractable
    {
        public string itemId;
        public int count = 1;
        public string instanceId;

        public Transform InteractionTransform => transform;

        public float GetPriority(in InteractionContext c) => !string.IsNullOrEmpty(itemId) && c.Session != null ? 1f : -1f;

        public string GetPrompt(in InteractionContext c)
        {
            if (string.IsNullOrEmpty(itemId) || c.Session == null) return null;
            string name = c.Session.Items != null && c.Session.Items.TryGetItem(itemId, out var d) ? d.DisplayNameOrId : itemId;
            return "Take " + name + (count > 1 ? $" x{count}" : "");
        }

        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            var s = c.Session;
            if (s == null || string.IsNullOrEmpty(itemId) || count <= 0) return false;
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (!s.Inventory.AddInstance(itemId, instanceId))
                {
                    OverflowPrompt.Offer(new ItemStack(itemId, 1, instanceId),
                        "Backpack is full. Send this item to the ship, or stash it in a nearby shuttle or rover.",
                        sent => { if (sent > 0 && this != null) Destroy(gameObject); });
                    return false;
                }
                UISound.Play(UISoundEvent.Pickup);
                Destroy(gameObject);
                return true;
            }
            int left = s.Inventory.Add(itemId, count);
            if (left == count)
            {
                OverflowPrompt.Offer(new ItemStack(itemId, count),
                    "Backpack is full. Send this item to the ship, or stash it in a nearby shuttle or rover.",
                    sent =>
                    {
                        if (this == null) return;
                        count -= sent;
                        if (count <= 0) Destroy(gameObject);
                    });
                return false;
            }
            if (left > 0) { count = left; UISound.Play(UISoundEvent.Pickup); return true; }
            UISound.Play(UISoundEvent.Pickup);
            Destroy(gameObject);
            return true;
        }

        /// <summary>Spawns the drop in front of the player. Returns false if there is no player or the stack is empty.</summary>
        public static bool SpawnInFrontOfPlayer(ItemStack stack, IItemLookup items)
        {
            if (stack == null || stack.IsEmpty) return false;
            var rig = PlayerRigLocator.Find();
            if (rig == null || rig.Root == null) return false;
            var origin = rig.Root.position + rig.Root.forward * 1.6f + Vector3.up * 1.2f;
            var pos = origin;
            if (Physics.Raycast(origin + Vector3.up, Vector3.down, out var hit, 8f))
                pos = hit.point + Vector3.up * 0.18f;

            string name = items != null && items.TryGetItem(stack.itemId, out var def) ? def.DisplayNameOrId : stack.itemId;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Dropped " + name;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material.color = new Color(0.55f, 0.82f, 1f);
            var p = go.AddComponent<DroppedItemPickup>();
            p.itemId = stack.itemId;
            p.count = Mathf.Max(1, stack.count);
            p.instanceId = stack.instanceId;
            return true;
        }
    }
}
