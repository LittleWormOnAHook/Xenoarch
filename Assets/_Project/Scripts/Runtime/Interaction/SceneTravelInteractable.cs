using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>
    /// DEBUG/foundation hook: travel to a scene + spawn id. Used by the Phase 1A placeholder scenes to exercise scene
    /// management and fades. Phase 1B replaces ship-side use with the navigation terminal and destination data.
    /// </summary>
    public sealed class SceneTravelInteractable : MonoBehaviour, IInteractable
    {
        public string targetScene;
        public string targetSpawnId = PlayerSpawnIds.Default;
        public string prompt = "Travel";

        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => string.IsNullOrEmpty(targetScene) ? -1f : 1f;
        public string GetPrompt(in InteractionContext c) => prompt;
        public float GetHoldDuration(in InteractionContext c) => 0.4f;

        public bool TryInteract(in InteractionContext c)
        {
            var rt = XenoarchRuntime.Instance;
            if (rt == null) return false;
            // Going back to the ship from a planet/station always lands at the airlock (same path as ShipTravel).
            if (targetScene == SceneNames.ShipStarter && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != SceneNames.ShipStarter)
                return DMI.Xenoarch.Galaxy.ShipTravel.ReturnToShip();
            return rt.Travel(targetScene, targetSpawnId);
        }
    }
}
