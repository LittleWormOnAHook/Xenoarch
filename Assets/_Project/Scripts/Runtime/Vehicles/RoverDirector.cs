using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// t187u: deploys / recalls the rover from the inventory. Planet and moon surfaces only (never the ship, hub or a
    /// station). One rover at a time; it lives in the surface scene and goes away with it. The item stays in the
    /// inventory; if it leaves the inventory and the ship crates (sold, dropped) the rover is recalled.
    /// Lives on the XenoarchRuntime object.
    /// </summary>
    public sealed class RoverDirector : MonoBehaviour
    {
        public static RoverDirector Instance { get; private set; }
        RoverVehicle _rover; string _itemId; float _nextCheck;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public bool IsDeployed(ItemDefinition item) => _rover != null && item != null && item.Id == _itemId;
        public RoverVehicle Current => _rover;

        public static bool OnPlanetSurface(out string why)
        {
            why = null;
            var s = XenoarchRuntime.Session;
            string scene = SceneManager.GetActiveScene().name;
            var body = s != null && s.Exploration != null && s.Exploration.Galaxy != null ? s.Exploration.Galaxy.FindBodyByScene(scene) : null;
            bool surface = body != null && scene != SceneNames.ShipStarter && scene != SceneNames.TradeHubSol
                && (body.kind == CelestialKind.Planet || body.kind == CelestialKind.Moon || body.kind == CelestialKind.DwarfPlanet);
            if (!surface) why = "Only on a planet or moon surface.";
            return surface;
        }

        public bool CanDeploy(ItemDefinition item, out string why)
        {
            why = null;
            var vd = item as VehicleItemDefinition;
            if (vd == null || vd.vehiclePrefab == null) { why = "No vehicle prefab assigned."; return false; }
            if (!OnPlanetSurface(out why)) return false;
            if (_rover != null) { why = "A rover is already deployed."; return false; }
            var s = XenoarchRuntime.Session;
            if (s == null || s.Inventory.Count(vd.Id) <= 0) { why = "Must be in your backpack."; return false; }
            var p = FindAnyObjectByType<FirstPersonController>();
            if (p == null || p.Seated) { why = "Stand on the ground first."; return false; }
            return true;
        }

        public bool Deploy(ItemDefinition item, out string why)
        {
            if (!CanDeploy(item, out why)) return false;
            var vd = (VehicleItemDefinition)item;
            var p = FindAnyObjectByType<FirstPersonController>();
            var go = Instantiate(vd.vehiclePrefab);
            go.name = vd.vehiclePrefab.name.Trim() + " (deployed)";
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var rover = go.GetComponent<RoverVehicle>() ?? go.AddComponent<RoverVehicle>();
            rover.Init();
            if (!rover.PlaceNear(p.transform, vd.deployDistance)) { Destroy(go); why = "No flat ground in front of you."; return false; }
            _rover = rover; _itemId = vd.Id;
            Debug.Log($"[Xenoarch] Rover: deployed {vd.Id} at {go.transform.position}");
            return true;
        }

        public void Recall()
        {
            if (_rover == null) return;
            if (_rover.Occupied) { UI.Toast.Show("Get out of the rover first."); return; }
            Destroy(_rover.gameObject);
            _rover = null; _itemId = null;
        }

        void Update()
        {
            if (_rover == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            var s = XenoarchRuntime.Session;
            if (s != null && !string.IsNullOrEmpty(_itemId) && s.Stock.Count(_itemId) <= 0 && !_rover.Occupied) Recall();
        }
    }
}
