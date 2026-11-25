using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Items
{
    public interface IItemLookup
    {
        bool TryGetItem(string itemId, out ItemDefinition definition);
    }

    [CreateAssetMenu(menuName = "Xenoarch/Items/Item Registry", fileName = "ItemRegistry")]
    public sealed class ItemRegistry : DefinitionRegistry<ItemDefinition>, IItemLookup
    {
        public bool TryGetItem(string itemId, out ItemDefinition definition) => TryGet(itemId, out definition);
    }
}
