using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// The only thing Xenoarch gameplay systems know about the player controller.
    /// Vendor first-person controller (pending, chosen by Anthony): add a small MonoBehaviour adapter on the vendor
    /// player prefab that implements this interface and forwards to the vendor API. Interaction, inventory, save and
    /// scene management never reference the vendor package directly.
    /// </summary>
    public interface IPlayerRig
    {
        /// <summary>Root transform that moves with the player (position/yaw are saved from here).</summary>
        Transform Root { get; }
        /// <summary>First-person view camera (interaction ray, UI anchoring).</summary>
        Camera ViewCamera { get; }
        bool InputEnabled { get; }
        /// <summary>Freeze/unfreeze movement + look (menus, terminals, scene transitions).</summary>
        void SetInputEnabled(bool enabled);
        /// <summary>Hard-move the player (spawn, load). Must reset controller velocity/grounding state.</summary>
        void Teleport(Vector3 position, Quaternion rotation);
    }

    public static class PlayerRigLocator
    {
        /// <summary>Finds the first active IPlayerRig in the loaded scenes (one player per scene by design).</summary>
        public static IPlayerRig Find()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (mb is IPlayerRig rig && mb.isActiveAndEnabled) return rig;
            return null;
        }
    }
}
