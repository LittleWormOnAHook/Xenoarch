using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>Interact = E / gamepad west button, via an Input System action created in code (no asset dependency).</summary>
    public sealed class InputSystemInteractionInput : MonoBehaviour, IInteractionInput
    {
        InputAction _interact;

        public bool InteractPressed => _interact != null && _interact.WasPressedThisFrame();
        public bool InteractHeld => _interact != null && _interact.IsPressed();

        void Awake()
        {
            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");
        }

        void OnEnable() => _interact?.Enable();
        void OnDisable() => _interact?.Disable();
        void OnDestroy() => _interact?.Dispose();
    }
}
