using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// PLACEHOLDER HUD (IMGUI): crosshair, interaction prompt, hold bar, inventory summary and save hints.
    /// Reads state only; replaced by the real UI layer later. Gameplay does not depend on it.
    /// </summary>
    public sealed class InteractionPromptHud : MonoBehaviour
    {
        [SerializeField] private Interactor interactor;
        GUIStyle _style;

        void Awake() { if (interactor == null) interactor = FindAnyObjectByType<Interactor>(); }

        void OnGUI()
        {
            if (UIState.IsOpen(Panels.Loading)) return;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = new Color(0.85f, 0.92f, 1f) } };
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 10, cy - 10, 20, 20), "+", _style);

            if (interactor != null && !string.IsNullOrEmpty(interactor.Prompt))
            {
                GUI.Label(new Rect(cx - 300, cy + 30, 600, 24), $"[E] {interactor.Prompt}", _style);
                if (interactor.HoldProgress01 > 0f)
                {
                    GUI.Box(new Rect(cx - 60, cy + 58, 120, 6), GUIContent.none);
                    GUI.Box(new Rect(cx - 60, cy + 58, 120 * interactor.HoldProgress01, 6), GUIContent.none);
                }
            }

            var s = XenoarchRuntime.Session;
            if (s == null) return;
            var sb = new System.Text.StringBuilder("Inventory: ");
            int shown = 0;
            foreach (var slot in s.Inventory.Slots)
            {
                if (slot.IsEmpty) continue;
                string name = s.Items.TryGetItem(slot.itemId, out var def) ? def.DisplayNameOrId : slot.itemId;
                sb.Append(shown++ > 0 ? ", " : "").Append(name).Append(slot.count > 1 ? " x" + slot.count : "");
            }
            if (shown == 0) sb.Append("(empty)");
            GUI.Label(new Rect(12, Screen.height - 52, Screen.width - 24, 20), sb.ToString());
            GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 20), "Placeholder rig (vendor FPC pending): Left/Right arrows turn.  E interact.  F5 quick save, F9 quick load.");
        }
    }
}
