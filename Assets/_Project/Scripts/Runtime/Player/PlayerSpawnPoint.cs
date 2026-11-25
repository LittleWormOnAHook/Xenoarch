using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>Named arrival point in a scene ("default", "airlock", "landing_pad"...).</summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        public string spawnId = Core.PlayerSpawnIds.Default;

        public static PlayerSpawnPoint Find(string spawnId)
        {
            PlayerSpawnPoint fallback = null;
            foreach (var p in FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (p.spawnId == spawnId) return p;
                if (p.spawnId == Core.PlayerSpawnIds.Default || fallback == null) fallback = p;
            }
            return fallback;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward);
        }
    }
}
