using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// TEMPORARY stand-in until the vendor first-person controller is installed ("vendor FPC pending").
    /// A static capsule with a head camera. It deliberately has no locomotion: the only debug control is
    /// Left/Right arrow to turn 45 degrees so the interaction test stations placed around it can be targeted.
    /// Replace with the vendor prefab + an IPlayerRig adapter; delete this component then.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceholderPlayerRig : MonoBehaviour, IPlayerRig
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private float debugTurnStep = 45f;

        public Transform Root => transform;
        public Camera ViewCamera => viewCamera != null ? viewCamera : (viewCamera = GetComponentInChildren<Camera>());
        public bool InputEnabled { get; private set; } = true;

        public void SetInputEnabled(bool enabled) => InputEnabled = enabled;

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            var cc = GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (cc) cc.enabled = true;
        }

        private void Update()
        {
            if (!InputEnabled || UI.UIState.BlocksGameplay) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame) transform.Rotate(0f, -debugTurnStep, 0f);
            if (kb.rightArrowKey.wasPressedThisFrame) transform.Rotate(0f, debugTurnStep, 0f);
        }
    }
}
