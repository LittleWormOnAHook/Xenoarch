using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>Placeholder for ship/hub stations whose systems arrive in later phases. Logs and shows a prompt only.</summary>
    public sealed class StationPlaceholderInteractable : MonoBehaviour, IInteractable
    {
        public string stationName = "Station";
        public string plannedPhase = "Phase 1B";

        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => 0.5f;
        public string GetPrompt(in InteractionContext c) => $"{stationName} (offline: {plannedPhase})";
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            Debug.Log($"[Xenoarch] {stationName}: not implemented until {plannedPhase}");
            return true;
        }
    }
}
