using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>Named arrival point in a scene ("default", "airlock", "landing_pad"...).</summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        public string spawnId = Core.PlayerSpawnIds.Default;

        /// <summary>Spawn by id; "a|b" tries a then b; falls back to "default" (or any spawn).</summary>
        public static PlayerSpawnPoint Find(string spawnId)
        {
            if (spawnId != null && spawnId.IndexOf('|') >= 0)
            {
                var all = FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var id in spawnId.Split('|'))
                    foreach (var p in all) if (p.spawnId == id) return p;
                spawnId = Core.PlayerSpawnIds.Default;
            }
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
