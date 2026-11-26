using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>Up / Down Console: E (with a hand reach) calls or sends the shuttle bay lift.</summary>
    public sealed class ShuttleLiftConsole : MonoBehaviour, IInteractable
    {
        public ShuttleLift lift;
        public Transform InteractionTransform => transform;

        void Awake() { if (lift == null) lift = FindFirstObjectByType<ShuttleLift>(); }

        public float GetPriority(in InteractionContext c) => lift != null && !lift.Moving ? 1f : -1f;
        public string GetPrompt(in InteractionContext c)
        {
            if (lift == null) return "Lift console (no platform)";
            bool atBottom = transform.position.y < lift.transform.position.y - 1f || (lift.IsDown && transform.position.y < lift.transform.position.y + 1f);
            return lift.Current == ShuttleLift.State.Up ? (atBottom ? "Call platform down" : "Lower platform to the shuttle bay")
                 : lift.IsDown ? (atBottom ? "Raise platform" : "Call platform up") : "Platform moving";
        }
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            if (lift == null || lift.Moving) return false;
            var arms = c.Rig != null ? c.Rig.Root.GetComponentInChildren<FirstPersonArms>() : null;
            var b = GetComponentInChildren<Renderer>();
            var target = b != null ? b.bounds.center + Vector3.up * b.bounds.extents.y * 0.6f : transform.position + Vector3.up;
            System.Action go = () => { if (lift.Current == ShuttleLift.State.Up) lift.GoDown(); else lift.GoUp(); };
            if (arms != null) arms.Reach(target, go); else go();
            return true;
        }
    }
}
