using DMI.Xenoarch.Core;
using DMI.Xenoarch.Economy;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// t187u: deploys / recalls the rover from the inventory. Planet and moon surfaces only (never the ship, hub or a
    /// station). One rover at a time; it lives in the surface scene and goes away with it. The item stays in the
    /// inventory; if it leaves the inventory and the ship crates (sold, dropped) the rover is recalled.
    /// t206u: owns the persistent rover fuel tank (safe default: full) and refuel helpers shared by the rover menu,
    /// the ship refuelling vendor and the refiner.
    /// Lives on the XenoarchRuntime object.
    /// </summary>
    public sealed class RoverDirector : MonoBehaviour
    {
        public static RoverDirector Instance { get; private set; }

        public const string SaveKey = "rover";
        public const float DefaultTank = 50f;
        public float FuelCapacity => DefaultTank;
        /// <summary>Credits per rover fuel unit; matches Vendor_refuelling (4 cr).</summary>
        public const float DefaultFuelPricePerUnit = 4f;

        RoverVehicle _rover; string _itemId; float _nextCheck;
        float _fuel = DefaultTank;

        public RoverVehicle Current => _rover;
        public float Fuel => _fuel;
        public float Fuel01 => FuelCapacity > 0f ? Mathf.Clamp01(_fuel / FuelCapacity) : 0f;
        public bool IsDeployed(ItemDefinition item) => _rover != null && item != null && item.Id == _itemId;

        void Awake()
        {
            Instance = this;
            var saves = XenoarchRuntime.Session != null ? XenoarchRuntime.Session.Saves : null;
            saves?.Register(new JsonStateSection<RoverState>(SaveKey, 22,
                () => new RoverState { fuel = _fuel },
                s => { _fuel = s != null && s.fuel >= 0f ? Mathf.Clamp(s.fuel, 0f, DefaultTank) : DefaultTank; },
                () => new RoverState { fuel = DefaultTank }));
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

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
            GameObject go;
            RoverVehicle.SpawningDeployed = true;
            try { go = Instantiate(vd.vehiclePrefab); }
            finally { RoverVehicle.SpawningDeployed = false; }
            go.name = vd.vehiclePrefab.name.Trim() + " (deployed)";
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var rover = go.GetComponent<RoverVehicle>();
            if (rover == null)
            {
                RoverVehicle.SpawningDeployed = true;
                try { rover = go.AddComponent<RoverVehicle>(); }
                finally { RoverVehicle.SpawningDeployed = false; }
            }
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

        public void SpendFuel(float units) => _fuel = Mathf.Max(0f, _fuel - Mathf.Max(0f, units));

        /// <summary>Adds fuel up to the cap; returns the amount actually accepted.</summary>
        public float AddFuel(float units)
        {
            float room = Mathf.Max(0f, FuelCapacity - _fuel);
            float a = Mathf.Min(room, Mathf.Max(0f, units));
            _fuel += a;
            return a;
        }

        /// <summary>Moves units from the ship tank into the rover (1:1, no credits). Returns the amount moved.</summary>
        public float TransferFromShip(float units)
        {
            var x = XenoarchRuntime.Session != null ? XenoarchRuntime.Session.Exploration : null;
            if (x == null || x.Ship == null) return 0f;
            float u = Mathf.Min(Mathf.Max(0f, units), x.Ship.fuel, FuelCapacity - _fuel);
            if (u <= 0.01f) return 0f;
            x.ConsumeFuel(u);
            return AddFuel(u);
        }

        /// <summary>Buys rover fuel with credits at <see cref="DefaultFuelPricePerUnit"/> (same as the ship vendor).</summary>
        public bool BuyFuel(float units, out string message)
        {
            message = null;
            var s = XenoarchRuntime.Session;
            if (s == null || s.Economy == null) { message = "No session."; return false; }
            float room = Mathf.Max(0f, FuelCapacity - _fuel);
            float afford = DefaultFuelPricePerUnit > 0f ? s.Economy.Credits / DefaultFuelPricePerUnit : 0f;
            float u = Mathf.Floor(Mathf.Min(units, room, afford));
            if (u < 1f) { message = room < 1f ? "Tank is full." : "Not enough credits."; return false; }
            int cost = Mathf.CeilToInt(u * DefaultFuelPricePerUnit);
            if (!s.Economy.Spend(cost, "refuel", "rover", "fuel", (int)u)) { message = "Not enough credits."; return false; }
            AddFuel(u);
            message = $"Refuelled {u:0} units for {cost} cr.";
            return true;
        }

        /// <summary>Fills the rover from fuel items in cargo (same items as ship refuel).</summary>
        public float RefuelFromCargo()
        {
            var s = XenoarchRuntime.Session;
            if (s == null || s.Materials == null) return 0f;
            return s.Materials.RefuelCustom(AddFuel);
        }

        void Update()
        {
            if (_rover == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            var s = XenoarchRuntime.Session;
            if (s != null && !string.IsNullOrEmpty(_itemId) && s.Stock.Count(_itemId) <= 0 && !_rover.Occupied) Recall();
        }

        [System.Serializable]
        public sealed class RoverState
        {
            /// <summary>-1 in an unexpected payload means "unset"; restore then uses a full tank.</summary>
            public float fuel = DefaultTank;
        }
    }
}
