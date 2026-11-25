using DMI.Xenoarch.Core;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using NUnit.Framework;
using UnityEngine;

namespace DMI.Xenoarch.Tests
{
    public class FoundationTests
    {
        ItemRegistry _reg;
        XenoarchConfig _config;

        [SetUp]
        public void SetUp()
        {
            var stack = ScriptableObject.CreateInstance<ItemDefinition>();
            stack.SetIdForTooling("item.test.stack"); stack.maxStack = 10;
            var unique = ScriptableObject.CreateInstance<ItemDefinition>();
            unique.SetIdForTooling("item.test.unique"); unique.uniqueInstance = true;
            _reg = ScriptableObject.CreateInstance<ItemRegistry>();
            _reg.SetEntries(new[] { stack, unique });
            _config = ScriptableObject.CreateInstance<XenoarchConfig>();
            _config.itemRegistry = _reg; _config.inventoryCapacity = 4;
        }

        [Test]
        public void StableIds_Validate()
        {
            Assert.IsTrue(StableId.IsValidDefinitionId("item.tool.brush"));
            Assert.IsFalse(StableId.IsValidDefinitionId("Item Tool"));
            Assert.AreEqual(0, _reg.Validate().Count);
        }

        [Test]
        public void Inventory_StacksAndCapacity()
        {
            var inv = new InventoryModel(_reg, 2);
            Assert.AreEqual(5, inv.Add("item.test.stack", 25)); // 2 slots x 10
            Assert.AreEqual(20, inv.Count("item.test.stack"));
            Assert.AreEqual(7, inv.Remove("item.test.stack", 7));
            Assert.AreEqual(13, inv.Count("item.test.stack"));
        }

        [Test]
        public void Inventory_UniqueInstancesNeverDuplicate()
        {
            var inv = new InventoryModel(_reg, 4);
            Assert.IsTrue(inv.AddInstance("item.test.unique", "a"));
            Assert.IsFalse(inv.AddInstance("item.test.unique", "a"));
            var data = inv.Capture();
            data.slots[1] = data.slots[0].Clone(); // corrupt save with a duplicate
            inv.Restore(data);
            Assert.AreEqual(1, inv.Count("item.test.unique"));
        }

        [Test]
        public void Save_RoundTrip_NoDuplication()
        {
            var session = new GameSession(_config, _reg, new MemorySaveStorage());
            session.StartNewGame();
            session.Inventory.Add("item.test.stack", 3);
            session.Inventory.AddInstance("item.test.unique", "u1");
            session.World.MarkConsumed("wo.a");
            Assert.IsTrue(session.Saves.Save("t"));
            session.StartNewGame();
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(session.Saves.TryRead("t", out var f, out var err), err);
                session.Saves.Apply(f);
            }
            Assert.AreEqual(3, session.Inventory.Count("item.test.stack"));
            Assert.AreEqual(1, session.Inventory.Count("item.test.unique"));
            Assert.IsTrue(session.World.IsConsumed("wo.a"));
        }

        [Test]
        public void Save_RefusesNewerVersion()
        {
            var svc = new SaveService(new MemorySaveStorage());
            Assert.IsFalse(svc.TryMigrate(new SaveFile { version = SaveService.CurrentVersion + 1 }, out _));
        }
    }
}
