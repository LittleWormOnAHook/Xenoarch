using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Inventory;
using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>Look-at port on the landed shuttle or a nearby rover: E opens that vehicle's stash (same slot UI).</summary>
    public sealed class VehicleStoragePort : MonoBehaviour, IInteractable
    {
        public bool shuttle;
        public Transform InteractionTransform => transform;
        public float GetPriority(in InteractionContext c) => c.Session != null ? 1.1f : -1f;
        public string GetPrompt(in InteractionContext c) => shuttle ? "Open shuttle storage" : "Open rover storage";
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            var cargo = c.Session?.VehicleCargo ?? VehicleCargo.Current;
            if (cargo == null) return false;
            VehicleStorageTerminal.Show(shuttle ? cargo.Shuttle : cargo.Rover, shuttle ? "Shuttle storage" : "Rover storage");
            return true;
        }

        /// <summary>Adds a trigger-only child port so looking at the hull still boards, while the side box opens storage.
        /// The collider is a trigger so it cannot push the vehicle (shuttle bounce work is separate).</summary>
        public static VehicleStoragePort Ensure(Transform host, bool shuttle, Vector3 localPos, Vector3 size)
        {
            if (host == null) return null;
            string name = shuttle ? "ShuttleStoragePort" : "RoverStoragePort";
            var existing = host.Find(name);
            if (existing != null) return existing.GetComponent<VehicleStoragePort>();
            var go = new GameObject(name);
            go.transform.SetParent(host, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = Vector3.zero;
            var port = go.AddComponent<VehicleStoragePort>();
            port.shuttle = shuttle;
            return port;
        }
    }

    /// <summary>Keeps storage ports attached to the current landed shuttle and deployed rover.</summary>
    public sealed class VehicleCargoPorts : MonoBehaviour
    {
        static VehicleCargoPorts _i;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_i != null) return;
            var go = new GameObject("Xenoarch Vehicle Cargo Ports");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<VehicleCargoPorts>();
        }

        void LateUpdate()
        {
            var sh = VehicleCargo.ShuttleTransform();
            if (sh != null) VehicleStoragePort.Ensure(sh, true, new Vector3(-2.2f, 1.0f, 0f), new Vector3(1.4f, 1.6f, 1.8f));
            var rv = VehicleCargo.RoverTransform();
            if (rv != null) VehicleStoragePort.Ensure(rv, false, new Vector3(0f, 0.9f, -2.0f), new Vector3(1.6f, 1.4f, 1.2f));
        }
    }
}
