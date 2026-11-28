using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>Optional: put on a device (or a parent) to set how close the eye must be to use it. Without it the device
    /// uses its own <see cref="IInteractable.MaxInteractDistance"/> (default <see cref="DefaultDistance"/> m).</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionReach : MonoBehaviour
    {
        public const float DefaultDistance = 2f;
        [Tooltip("Max distance (m) from the eye to this device's geometry.")]
        [Range(0.5f, 6f)] public float maxDistance = DefaultDistance;
    }
}
