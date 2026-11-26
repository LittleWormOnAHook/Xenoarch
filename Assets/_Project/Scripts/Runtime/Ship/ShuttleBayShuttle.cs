using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>The shuttle parked in the ship's bay: E opens its flight menu (map of the current planet's landing zones
    /// and scanned sites, then Take Shuttle).</summary>
    public sealed class ShuttleBayShuttle : MonoBehaviour, IInteractable
    {
        public Transform InteractionTransform => transform;
        ShuttleLaunchWindow _window;
        static ExplorationService X => XenoarchRuntime.Session?.Exploration;

        [Tooltip("Gap (m) between the shuttle's side and the return spawn.")] public float spawnGap = 1.0f;

        void Awake()
        {
            foreach (var p in FindObjectsByType<Player.PlayerSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (p.spawnId == ShipTravel.ShipSpawnShuttleBay) return; // a placed one wins
            if (!FindSpawn(out var pos, out var rot)) { Debug.LogWarning("[Xenoarch] Bay shuttle: no clear floor beside it for the return spawn; returns use the airlock."); return; }
            var go = new GameObject("Spawn_shuttle_bay (runtime)");
            SceneManagerMove(go);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<Player.PlayerSpawnPoint>().spawnId = ShipTravel.ShipSpawnShuttleBay;
        }

        void SceneManagerMove(GameObject go) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);

        bool Mine(Collider c) => c != null && c.transform.IsChildOf(transform);

        /// <summary>Clear floor beside the shuttle: right, left, behind, front; facing the shuttle.</summary>
        bool FindSpawn(out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var rs = GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return false;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized; var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float ex(Vector3 d) => Mathf.Abs(d.x) * b.extents.x + Mathf.Abs(d.z) * b.extents.z;
            var c = b.center;
            foreach (var d in new[] { right, -right, -fwd, fwd })
                for (float extra = 0f; extra <= 1.5f; extra += 0.5f)
                {
                    var p = c + d * (ex(d) + spawnGap + extra);
                    if (!Floor(new Vector3(p.x, b.min.y + 1.2f, p.z), b.min.y - 1.5f, out var floor)) continue;
                    if (!Clear(floor)) continue;
                    pos = floor + Vector3.up * 0.05f;
                    var look = Vector3.ProjectOnPlane(c - pos, Vector3.up);
                    rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                    return true;
                }
            return false;
        }

        bool Floor(Vector3 from, float minY, out Vector3 point)
        {
            point = default; float best = float.MinValue;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, from.y - minY, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(h.collider) && h.normal.y > 0.7f && h.point.y > best) { best = h.point.y; point = h.point; }
            return best > float.MinValue;
        }

        bool Clear(Vector3 floor)
        {
            foreach (var col in Physics.OverlapCapsule(floor + Vector3.up * 0.45f, floor + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(col)) return false;
            return true;
        }

        public float GetPriority(in InteractionContext c) => X != null && (_window == null || !_window.IsOpen) ? 1f : -1f;
        public string GetPrompt(in InteractionContext c)
        {
            var b = X?.CurrentBody;
            return ShipTravel.UsesShuttle(b) ? $"Shuttle: fly to {b.displayName}" : "Shuttle: no landing zone in range";
        }
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            if (X == null || !ShipTravel.UsesShuttle(X.CurrentBody)) { UI.Toast.Show("Shuttle: travel to a planet or moon first (Navigation Terminal)."); return false; }
            if (_window == null)
            {
                var go = new GameObject("Shuttle Flight Menu");
                go.transform.SetParent(transform, false);
                _window = go.AddComponent<ShuttleLaunchWindow>();
                _window.terminalName = "Shuttle";
            }
            _window.Open();
            return _window.IsOpen;
        }
    }
}
