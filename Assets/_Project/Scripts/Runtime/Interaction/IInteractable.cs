using DMI.Xenoarch.Core;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>
    /// Anything the player can use with Interact. Contract adapted from ASH IASHUsable (itself from Genesis IWorldUsable):
    /// priority (negative = unavailable), prompt, hold duration (0 = tap) and TryInteract.
    /// </summary>
    public interface IInteractable
    {
        float GetPriority(in InteractionContext context);
        string GetPrompt(in InteractionContext context);
        float GetHoldDuration(in InteractionContext context);
        bool TryInteract(in InteractionContext context);
        Transform InteractionTransform { get; }
        /// <summary>How close (m) the eye must be to this device's geometry to use it. Default 2 m. Override in a class,
        /// or add an <see cref="InteractionReach"/> component to the object (no scene object has to be edited).</summary>
        float MaxInteractDistance => InteractionReach.DefaultDistance;
    }

    /// <summary>Snapshot passed to interactables. Controller-agnostic: only the IPlayerRig adapter and the session.</summary>
    public readonly struct InteractionContext
    {
        public IPlayerRig Rig { get; }
        public GameSession Session { get; }
        public Ray ViewRay { get; }
        public float Range { get; }

        public InteractionContext(IPlayerRig rig, GameSession session, Ray viewRay, float range)
        {
            Rig = rig; Session = session; ViewRay = viewRay; Range = range;
        }
    }

    /// <summary>Source of the Interact button. Default: <see cref="InputSystemInteractionInput"/>; a vendor controller may supply its own.</summary>
    public interface IInteractionInput
    {
        bool InteractPressed { get; }
        bool InteractHeld { get; }
    }
}
