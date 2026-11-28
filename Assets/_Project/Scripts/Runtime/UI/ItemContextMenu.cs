using System;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Ship;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>Shared right-click menu for inventory, storage and the hotbar (Inspect, Move, Split, Drop, Destroy).</summary>
    public static class ItemContextMenu
    {
        public sealed class Request
        {
            public GameSession Session;
            public VisualElement Host;
            public ItemStack Stack;
            public InventoryModel Inventory;
            public int Index = -1;
            public bool FromHotbar;
            public Action Remove;          // take the whole stack (drop / destroy)
            public Action Refresh;
            public string MoveLabel;
            public Action OnMove;
            public string Warning;
            public Action ExtraBuild;      // optional: puzzle Combine, etc.  (called with the menu as current? we'll pass menu)
            public Action<VisualElement> BuildExtra;
        }

        public static VisualElement Show(Request req, Vector2 screenPos)
        {
            if (req?.Host == null || req.Stack == null || req.Stack.IsEmpty || req.Session == null) return null;
            Hide(req.Host);
            var s = req.Session;
            var st = req.Stack;
            var d = s.Items.TryGetItem(st.itemId, out var def) ? def : null;
            string name = d != null ? def.DisplayNameOrId : st.itemId;
            var art = !string.IsNullOrEmpty(st.instanceId) ? s.Artifacts.Get(st.instanceId) : null;
            var dropGate = s.Eligibility != null ? s.Eligibility.Check(art, ArtifactAction.Discard) : Eligibility.Allowed;
            var destroyGate = s.Eligibility != null ? s.Eligibility.Check(art, ArtifactAction.Destroy) : Eligibility.Allowed;

            var menu = new VisualElement { name = "xa-item-ctx" };
            menu.AddToClassList("xa-ctx");
            menu.style.position = Position.Absolute;
            menu.style.minWidth = 260;

            var title = new Label(name + (st.count > 1 ? $"  x{st.count}" : ""));
            title.AddToClassList("xa-term__h");
            menu.Add(title);

            if (!string.IsNullOrEmpty(req.Warning))
            {
                var w = new Label(req.Warning); w.AddToClassList("xa-term__warn"); menu.Add(w);
            }
            req.BuildExtra?.Invoke(menu);

            Btn(menu, "Inspect", () =>
            {
                Hide(req.Host);
                ItemInspector.Open(st);
            });

            if (req.OnMove != null)
            {
                Btn(menu, string.IsNullOrEmpty(req.MoveLabel) ? "Move" : req.MoveLabel, () =>
                {
                    Hide(req.Host);
                    req.OnMove();
                    req.Refresh?.Invoke();
                });
            }

            if (!req.FromHotbar && req.Inventory != null)
            {
                bool moveIsShip = req.OnMove != null && !string.IsNullOrEmpty(req.MoveLabel)
                    && (req.MoveLabel.IndexOf("Hold", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || req.MoveLabel.IndexOf("crate", System.StringComparison.OrdinalIgnoreCase) >= 0);
                foreach (var dest in VehicleCargo.Destinations(st, req.Inventory))
                {
                    if (moveIsShip && dest.Id == "ship") continue;
                    var destOpt = dest;
                    Btn(menu, destOpt.Label, () =>
                    {
                        Hide(req.Host);
                        var cur = req.Index >= 0 && req.Index < req.Inventory.Slots.Count
                            ? req.Inventory.Slots[req.Index].Clone() : st.Clone();
                        int n = VehicleCargo.Send(req.Inventory, destOpt.Inventory, cur);
                        if (n <= 0) Toast.Show($"{destOpt.Label} is full.");
                        else Toast.Show($"{destOpt.Label}: {n} sent.");
                        req.Refresh?.Invoke();
                    });
                }
            }

            bool canSplit = !req.FromHotbar && req.Inventory != null && req.Index >= 0
                && st.count > 1 && d != null && d.IsStackable && d.EffectiveMaxStack > 1
                && (req.Inventory.FreeSlots > 0 || HasMergeRoom(req.Inventory, req.Index, d));
            if (st.count > 1 && d != null && d.IsStackable)
            {
                if (canSplit)
                    Btn(menu, "Split", () => { Hide(req.Host); ShowSplit(req, d, name); });
                else
                {
                    var dim = new Label("Split (no free slot)"); dim.AddToClassList("xa-term__dim"); menu.Add(dim);
                }
            }

            bool vehicleOut = d is VehicleItemDefinition vdef && RoverDirector.Instance is RoverDirector rd && rd.IsDeployed(vdef);
            if (vehicleOut)
            {
                var vw = new Label("Recall the vehicle before dropping or destroying it.");
                vw.AddToClassList("xa-term__warn"); menu.Add(vw);
            }
            else
            {
                var dropStack = st.Clone();
                if (dropGate.IsBlocked)
                {
                    var l = new Label("Drop: " + dropGate.Message); l.AddToClassList("xa-term__warn"); menu.Add(l);
                }
                else
                {
                    Btn(menu, "Drop", () =>
                    {
                        Hide(req.Host);
                        void Go() => DoDrop(dropStack, req.Remove, name, req.Refresh);
                        if (dropGate.IsWarning) MenuScreens.Confirm(req.Host, dropGate.Message + $"\n\nDrop {name} in front of you?", Go);
                        else Go();
                    });
                }

                if (destroyGate.IsBlocked)
                {
                    var l = new Label("Destroy: " + destroyGate.Message); l.AddToClassList("xa-term__warn"); menu.Add(l);
                }
                else
                {
                    Btn(menu, "Destroy", () =>
                    {
                        Hide(req.Host);
                        string q = (destroyGate.IsWarning ? destroyGate.Message + "\n\n" : "")
                                   + $"Destroy {name}" + (st.count > 1 ? $" x{st.count}" : "") + "? This cannot be undone.";
                        MenuScreens.Confirm(req.Host, q, () =>
                        {
                            req.Remove?.Invoke();
                            Toast.Show($"Destroyed {name}.");
                            req.Refresh?.Invoke();
                        });
                    });
                }
            }

            Btn(menu, "Cancel", () => Hide(req.Host)).AddToClassList("xa-term__btn--ghost");
            req.Host.Add(menu);
            var local = req.Host.WorldToLocal(screenPos);
            menu.style.left = local.x; menu.style.top = local.y;
            menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (menu.parent == null) return;
                var b = req.Host.contentRect;
                menu.style.left = Mathf.Clamp(local.x, 0f, Mathf.Max(0f, b.width - menu.layout.width));
                menu.style.top = Mathf.Clamp(local.y, 0f, Mathf.Max(0f, b.height - menu.layout.height));
            });
            if (!req.Host.ClassListContains("xa-item-ctx-hooked"))
            {
                req.Host.AddToClassList("xa-item-ctx-hooked");
                req.Host.RegisterCallback<PointerDownEvent>(e =>
                {
                    var m = req.Host.Q("xa-item-ctx");
                    if (m != null && !m.worldBound.Contains(e.position)) Hide(req.Host);
                });
            }
            return menu;
        }

        static bool HasMergeRoom(InventoryModel inv, int index, ItemDefinition def)
        {
            var src = inv.Slots[index];
            int max = def.EffectiveMaxStack;
            for (int i = 0; i < inv.Slots.Count; i++)
            {
                if (i == index) continue;
                var o = inv.Slots[i];
                if (!o.IsEmpty && o.itemId == src.itemId && o.SameMeta(src) && o.count < max) return true;
            }
            return false;
        }

        static void ShowSplit(Request req, ItemDefinition d, string name)
        {
            var st = req.Stack;
            int max = st.count - 1;
            int amount = Mathf.Max(1, st.count / 2);
            var panel = new VisualElement { name = "xa-item-ctx" };
            panel.AddToClassList("xa-ctx");
            panel.AddToClassList("xa-split");
            panel.style.position = Position.Absolute;
            panel.style.left = 40; panel.style.top = 80;
            panel.style.minWidth = 340;
            var h = new Label($"Split {name}"); h.AddToClassList("xa-term__h"); panel.Add(h);
            var count = new Label(amount.ToString());
            count.AddToClassList("xa-term__text");
            count.style.unityFontStyleAndWeight = FontStyle.Bold;
            count.style.unityTextAlign = TextAnchor.MiddleCenter;
            count.style.fontSize = 22;
            count.style.marginTop = 8; count.style.marginBottom = 4;
            panel.Add(count);
            var slider = new SliderInt(1, max) { value = amount, showInputField = false };
            slider.style.marginBottom = 8;
            slider.RegisterValueChangedCallback(e => { amount = e.newValue; count.text = amount.ToString(); });
            panel.Add(slider);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            panel.Add(row);
            Btn(row, "Split", () =>
            {
                Hide(req.Host);
                if (req.Inventory != null && req.Inventory.SplitAt(req.Index, amount))
                    Toast.Show($"Split {amount} {name}.");
                else Toast.Show("Couldn't split: no free slot.");
                req.Refresh?.Invoke();
            });
            Btn(row, "Cancel", () => Hide(req.Host)).AddToClassList("xa-term__btn--ghost");
            req.Host.Add(panel);
        }

        static void DoDrop(ItemStack st, Action remove, string name, Action refresh)
        {
            if (!DroppedItemPickup.SpawnInFrontOfPlayer(st, XenoarchRuntime.Session?.Items))
            {
                Toast.Show("Can't drop that here.");
                return;
            }
            remove?.Invoke();
            Toast.Show("Dropped " + name + (st.count > 1 ? $" x{st.count}" : "") + ".");
            refresh?.Invoke();
        }

        public static void Hide(VisualElement host)
        {
            if (host == null) return;
            var m = host.Q("xa-item-ctx");
            m?.RemoveFromHierarchy();
        }

        static Button Btn(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("xa-term__btn");
            b.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(b);
            return b;
        }
    }
}
