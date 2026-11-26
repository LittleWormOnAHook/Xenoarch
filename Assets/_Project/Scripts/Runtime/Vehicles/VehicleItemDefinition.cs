using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// t187u: an inventory item that deploys a drivable vehicle on a planet or moon surface (the Grasshopper rover).
    /// The item stays in the backpack while the vehicle is out (Deploy / Recall in the inventory DETAILS panel), so a save
    /// never loses it; the deployed vehicle itself is not saved and goes away when you leave the surface.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Items/Vehicle Item", fileName = "Item_vehicle_")]
    public sealed class VehicleItemDefinition : ItemDefinition
    {
        [Tooltip("Vehicle prefab (Assets/_Project/Prefabs/Vehicles and Ships).")] public GameObject vehiclePrefab;
        [Tooltip("How far in front of the player the vehicle is set down (m).")] public float deployDistance = 14f;
    }
}
