using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>References the six held-tool prefabs. Lives at Resources/Tools/HeldToolCatalog.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Player/Held Tool Catalog", fileName = "HeldToolCatalog")]
    public sealed class HeldToolCatalog : ScriptableObject
    {
        public GameObject probeWand;
        public GameObject scannerPistol;
        public GameObject entrenchingShovel;
        public GameObject precisionDual;
        public GameObject brushStubby;
        public GameObject rockDrill;

        public GameObject PrefabFor(string itemId)
        {
            return itemId switch
            {
                HeldToolMeshFactory.ProbeId => probeWand,
                HeldToolMeshFactory.ScannerId => scannerPistol,
                HeldToolMeshFactory.ShovelId => entrenchingShovel,
                HeldToolMeshFactory.PrecisionId => precisionDual,
                HeldToolMeshFactory.BrushId => brushStubby,
                HeldToolMeshFactory.DrillId => rockDrill,
                _ => null
            };
        }

        public static HeldToolCatalog Load() => Resources.Load<HeldToolCatalog>(HeldToolMeshFactory.CatalogResource);
    }
}
