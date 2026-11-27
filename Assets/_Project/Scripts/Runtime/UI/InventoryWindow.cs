using System.Linq;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Crafting;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Materials;
using MaterialDefinition = DMI.Xenoarch.Materials.MaterialDefinition;
using DMI.Xenoarch.Ship;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Vehicles;
using DMI.Xenoarch.Exploration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Player inventory (I or Tab): backpack slot grid with icons, item details, ship crate summary, and crafting
    /// (ASH-style: recipes by category, have/need per ingredient, timed craft). Crafting takes ingredients from the
    /// backpack first, then the three ship crates; products go to the backpack (overflow into a crate).
    /// </summary>
    public sealed class InventoryWindow : TerminalWindow
    {
        protected override string PanelId => "player.inventory";
        string _selItem; string _selRecipe; CraftCategory? _filter; string _message;
        int _closedFrame = -1; float _nextRefresh; bool _hooked;
        bool _confirmShuttle;
        bool _confirmRover;

        void Awake() { terminalName = "Inventory"; }

        protected override void Update()
        {
            bool wasOpen = IsOpen;
            base.Update();
            var s = Session;
            if (wasOpen && IsOpen && !JournalReader.Blocking && Time.unscaledTime - _openTime > 0.2f && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) Close();
            if (s == null) return;
            if (!_hooked)
            {
                _hooked = true;
                s.Crafting.Crafted += r => { Toast.Show(r.IsUpgrade ? $"Installed: {r.Title}" : $"Crafted {r.outputAmount}x {s.Crafting.Name(r.outputItemId)}"); _message = null; Refresh(); };
                s.Crafting.Failed += (r, why) => { _message = $"{r?.Title}: {why}"; Refresh(); };
                s.Journals.Changed += Refresh; // NEW tags clear as notes are read
            }
            if (IsOpen || UIState.BlocksGameplay || Time.frameCount == _closedFrame) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene == SceneNames.MainMenu || scene == SceneNames.Bootstrap || Scenes.SceneLoader.Busy) return;
            var kb = Keyboard.current;
            if (kb != null && kb.iKey.wasPressedThisFrame) Open(); // t246u: Tab tap opens it via ToolWheelHud (hold = wheel)
        }

        float _openTime;
        protected override void OnOpened() => _openTime = Time.unscaledTime;
        protected override void OnClosed() => _closedFrame = Time.frameCount;

        protected override void Tick()
        {
            if (Session.Crafting.Busy && Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }
            else if (IsOpen && Time.unscaledTime >= _nextRefresh &&
                ((ShuttleDirector.Instance != null && ShuttleDirector.Instance.EmergencyCooldownRemaining > 0f)
                 || (RoverDirector.Instance != null && RoverDirector.Instance.EmergencyCooldownRemaining > 0f)))
            { _nextRefresh = Time.unscaledTime + 0.5f; Refresh(); }
        }

        ItemDefinition Def(string id) => id != null && Session.Items.TryGetItem(id, out var d) ? d : null;

        protected override void Build(VisualElement body)
        {
            _menu = null;
            if (!_menuHooked) { _menuHooked = true; body.RegisterCallback<PointerDownEvent>(e => { if (_menu != null && !_menu.worldBound.Contains(e.position)) HideMenu(); }); }
            var s = Session; var inv = s.Inventory;
            Status.text = $"Backpack {inv.Capacity - inv.FreeSlots}/{inv.Capacity}   |   {s.Economy.Credits:N0} cr   |   {s.Economy.RankName} ({s.Economy.Score})   |   Ship storage level {s.Exploration.Ship.holdLevel + 1}" + (s.AboardShip ? "   |   Aboard: ship workshop available" : "");
            Hint.text = "I / Tab / Esc: close   |   Right-click an item to drop or destroy   |   Hotbar items do not use backpack space";

            // ---------------- backpack grid + ship crates
            var left = ScrollCol(body, 0, true);
            Text(left, "BACKPACK", "xa-term__h");
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = 84 * 6; // six 80px slots with 4px gutters, so 29 slots land on a clean last row of five
            left.Add(grid);
            for (int i = 0; i < inv.Slots.Count; i++) grid.Add(Tile(inv.Slots[i], i));
            Text(left, "Drag items to rearrange, onto the hotbar, or onto a crate below. Right-click to drop or destroy.", "xa-term__dim");

            // Hotbar (keys 1-8). Slot 1 is the scanner; drag tools/items onto 2-8, drag off to clear.
            Text(left, "HOTBAR", "xa-term__h");
            var hb = new VisualElement(); hb.style.flexDirection = FlexDirection.Row; hb.style.flexWrap = Wrap.Wrap; left.Add(hb);
            for (int k = 0; k < Hotbar.Size; k++) hb.Add(HotbarTile(k));
            DragDrop.Target(grid, p => p.kind == "hotbar" || p.kind == "backpack", p => { if (p.kind == "hotbar") { TakeOffHotbar(p.index); Refresh(); } });

            Text(left, "SHIP STORAGE", "xa-term__h");
            foreach (var kv in s.Holds)
            {
                var h = kv.Value; int used = h.Capacity - h.FreeSlots;
                var top = h.Slots.Where(x => !x.IsEmpty).GroupBy(x => x.itemId).Select(g => $"{Def(g.Key)?.DisplayNameOrId ?? g.Key} x{g.Sum(x => x.count)}").Take(4);
                var line = Text(left, $"{kv.Key} crate: {used}/{h.Capacity} slots" + (used > 0 ? $"  ({string.Join(", ", top)}{(h.Slots.Count(x => !x.IsEmpty) > 4 ? ", ..." : "")})" : "") + "   [drop here]", "xa-term__dim");
                var role = kv.Key; var hold = h;
                DragDrop.Target(line, p => p.kind == "backpack", p => DropToCrate(role, hold, p));
            }

            Text(left, "EMERGENCY CALLS", "xa-term__h");
            if (_confirmShuttle)
            {
                Text(left, "Call the shuttle to land on valid ground near you?", "xa-term__dim");
                Btn(left, "Confirm emergency shuttle call", () =>
                {
                    _confirmShuttle = false;
                    var d = ShuttleDirector.Instance;
                    if (d == null) Toast.Show("Couldn't call the shuttle.", 3f);
                    else { d.CallEmergency(out var msg); Toast.Show(string.IsNullOrEmpty(msg) ? "Couldn't call the shuttle." : msg, 3f); }
                    Refresh();
                });
                Btn(left, "Cancel", () => { _confirmShuttle = false; Refresh(); }).AddToClassList("xa-term__btn--ghost");
            }
            else if (_confirmRover)
            {
                Text(left, "Call the rover to drive to you (or deploy it nearby if it is not out)?", "xa-term__dim");
                Btn(left, "Confirm emergency rover call", () =>
                {
                    _confirmRover = false;
                    var d = RoverDirector.Instance;
                    if (d == null) Toast.Show("Couldn't call the rover.", 3f);
                    else { d.CallEmergency(out var msg); Toast.Show(string.IsNullOrEmpty(msg) ? "Couldn't call the rover." : msg, 3f); }
                    Refresh();
                });
                Btn(left, "Cancel", () => { _confirmRover = false; Refresh(); }).AddToClassList("xa-term__btn--ghost");
            }
            else
            {
                string shuttleLabel = "Emergency Shuttle Call";
                bool shuttleOn = true;
                var shuttle = ShuttleDirector.Instance;
                if (!s.EmergencyShuttleUnlocked) { shuttleLabel = "Emergency Shuttle Call (locked)"; shuttleOn = false; }
                else if (s.AboardShip) { shuttleLabel = "Emergency Shuttle Call (aboard ship)"; shuttleOn = false; }
                else if (shuttle == null) { shuttleLabel = "Emergency Shuttle Call (unavailable)"; shuttleOn = false; }
                else if (shuttle.EmergencyCooldownRemaining > 0.5f) { shuttleLabel = $"Emergency Shuttle Call ({shuttle.EmergencyCooldownRemaining:0}s)"; shuttleOn = false; }
                Btn(left, shuttleLabel, () => { _confirmShuttle = true; _confirmRover = false; Refresh(); }, shuttleOn);

                string roverLabel = "Emergency Rover Call";
                bool roverOn = true;
                var rover = RoverDirector.Instance;
                if (!s.EmergencyRoverUnlocked) { roverLabel = "Emergency Rover Call (locked)"; roverOn = false; }
                else if (!RoverDirector.OnPlanetSurface(out _)) { roverLabel = "Emergency Rover Call (surface only)"; roverOn = false; }
                else if (rover == null) { roverLabel = "Emergency Rover Call (unavailable)"; roverOn = false; }
                else if (rover.EmergencyCooldownRemaining > 0.5f) { roverLabel = $"Emergency Rover Call ({rover.EmergencyCooldownRemaining:0}s)"; roverOn = false; }
                Btn(left, roverLabel, () => { _confirmRover = true; _confirmShuttle = false; Refresh(); }, roverOn);
            }

            // Journals: survey notes picked up along the way (click to read; prev / next inside).
            var journals = s.Journals.Collected.ToList();
            int unread = s.Journals.UnreadCount;
            Text(left, $"JOURNALS ({journals.Count}{(unread > 0 ? $", {unread} unread" : "")})", "xa-term__h");
            if (journals.Count == 0) Text(left, "No journals yet. Survey crews left datapads near landing zones and dig sites.", "xa-term__dim");
            var ids = journals.Select(j => j.Id).ToList();
            foreach (var j in journals)
            {
                var id = j.Id; bool read = s.Journals.IsRead(id);
                var b = Btn(left, $"{(read ? "" : "NEW  ")}{j.title}  ({j.category})", () => JournalReader.Show(id, ids));
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (!read) { b.style.borderLeftWidth = 4; b.style.borderLeftColor = XenoarchPalette.Highlight; }
            }

            // ---------------- details
            var mid = ScrollCol(body, 1);
            Text(mid, "DETAILS", "xa-term__h");
            var d = Def(_selItem);
            if (d == null) Text(mid, "Select an item.", "xa-term__dim");
            else
            {
                if (d.icon != null) { var big = new VisualElement(); big.style.width = 120; big.style.height = 120; XenoarchIcons.ApplyItem(big, d); big.style.marginBottom = 6; mid.Add(big); }
                Text(mid, d.DisplayNameOrId.ToUpperInvariant(), "xa-term__h");
                Text(mid, $"{d.category}   |   value {d.baseValue}   |   mass {d.mass:0.##}");
                if (!string.IsNullOrEmpty(d.description)) Text(mid, d.description, "xa-term__dim");
                int bp = inv.Count(d.Id), all = s.Stock.Count(d.Id);
                Text(mid, $"Backpack {bp}   |   Ship crates {all - bp}");
                if (d is DMI.Xenoarch.Vehicles.VehicleItemDefinition && DMI.Xenoarch.Vehicles.RoverDirector.Instance is DMI.Xenoarch.Vehicles.RoverDirector rd)
                {
                    // t187u: vehicles deploy from the backpack on planet / moon surfaces only.
                    if (rd.IsDeployed(d)) Btn(mid, "Recall vehicle", () => { rd.Recall(); Refresh(); });
                    else
                    {
                        bool can = rd.CanDeploy(d, out var why);
                        Btn(mid, can ? "Deploy" : $"Deploy\n{why}", () =>
                        {
                            if (rd.Deploy(d, out var r)) { Toast.Show($"{d.DisplayNameOrId} deployed. Rear door: E to open, E to enter."); Close(); }
                            else { Toast.Show(r); Refresh(); }
                        }, can);
                    }
                }
                if (d is MaterialDefinition m)
                {
                    if (m.fuelValue > 0f) Text(mid, $"Ship fuel: {m.fuelValue:0.#} per item (Refiner: Refuel)");
                    if (m.reagentValue > 0) Text(mid, $"Lab reagent: {m.reagentValue} per item");
                    var uses = s.Materials.Recipes.Where(r => r.inputs.Any(i => i.itemId == m.Id)).Select(r => r.displayName).ToList();
                    if (uses.Count > 0) Text(mid, $"Refiner: {string.Join(", ", uses)}");
                    var crafts = s.Crafting.Recipes.Where(r => s.Crafting.Known(r) && r.ingredients.Any(i => i.itemId == m.Id)).Select(r => r.Title).ToList();
                    if (crafts.Count > 0) Text(mid, $"Crafting: {string.Join(", ", crafts)}");
                }
            }

            // ---------------- crafting
            var right = ScrollCol(body, 2);
            var c = s.Crafting;
            Text(right, "CRAFTING", "xa-term__h");
            var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; tabs.style.flexWrap = Wrap.Wrap; right.Add(tabs);
            void Tab(string label, CraftCategory? cat)
            {
                var b = Btn(tabs, label, () => { _filter = cat; Refresh(); });
                b.style.width = StyleKeyword.Auto; b.style.paddingLeft = b.style.paddingRight = 8; b.style.marginRight = 4;
                if (_filter == cat) b.AddToClassList("xa-term__row--sel");
            }
            Tab("All", null);
            foreach (CraftCategory cat in System.Enum.GetValues(typeof(CraftCategory))) if (c.Recipes.Any(r => r.category == cat && c.Known(r))) Tab(cat.ToString(), cat);

            if (c.Busy)
            {
                Text(right, $"Crafting: {c.Current.Title}");
                Bar(right, c.Progress01);
                Btn(right, "Cancel", () => { c.Cancel(); Refresh(); }).AddToClassList("xa-term__btn--ghost");
            }
            foreach (var r in c.Recipes.Where(r => c.Known(r) && (_filter == null || r.category == _filter)))
            {
                var why = c.MissingReason(r);
                var id = r.Id;
                var row = new Button(() => { _selRecipe = id; Refresh(); }) { text = "" };
                row.AddToClassList("xa-term__row");
                if (id == _selRecipe) row.AddToClassList("xa-term__row--sel");
                row.Add(new Label((why == null ? "" : "  ") + r.Title) { style = { color = why == null ? XenoarchPalette.Highlight : XenoarchPalette.SecondaryText } });
                right.Add(row);
                if (id != _selRecipe) continue;
                if (!string.IsNullOrEmpty(r.description)) Text(right, r.description, "xa-term__dim");
                Text(right, "REQUIRES (backpack + ship crates)", "xa-term__dim");
                foreach (var i in r.ingredients)
                {
                    int have = s.Stock.Count(i.itemId);
                    string src = have >= i.count ? "" : Source(s, i.itemId);
                    Text(right, $"  {c.Name(i.itemId)}  {have}/{i.count}" + (string.IsNullOrEmpty(src) ? "" : $"   ({src})"), have >= i.count ? "xa-term__text" : "xa-term__warn");
                }
                if (r.IsUpgrade)
                {
                    int lvl = c.UpgradeLevel(r.upgradeId), max = c.UpgradeMax(r.upgradeId);
                    Text(right, $"Upgrade: {r.Title}   level {lvl} -> {r.upgradeLevel} (max {max})", "xa-term__dim");
                    string fx = UpgradeEffect(s, r.upgradeId, lvl, r.upgradeLevel);
                    if (!string.IsNullOrEmpty(fx)) Text(right, fx, "xa-term__good");
                }
                else Text(right, $"Makes: {r.outputAmount}x {c.Name(r.outputItemId)}   |   {r.craftSeconds:0.#} s" + (r.station == CraftStation.Ship ? "   |   ship workshop" : ""), "xa-term__dim");
                Btn(right, why == null ? "Craft" : why, () => { _message = null; c.TryCraft(r); Refresh(); }, why == null);
            }
            if (!string.IsNullOrEmpty(_message)) Text(right, _message, "xa-term__warn");
        }

        VisualElement _menu; bool _menuHooked;

        /// <summary>Right-click menu: Drop, Destroy, and Combine when the item is a puzzle piece. Available whenever
        /// the item can be selected, not only when the backpack is full.</summary>
        void ShowItemMenu(ItemStack st, System.Action remove, Vector2 screenPos)
        {
            HideMenu();
            if (st == null || st.IsEmpty || remove == null) return;
            _selItem = st.itemId;
            var s = Session;
            var d = Def(st.itemId);
            string name = d != null ? d.DisplayNameOrId : st.itemId;
            var art = !string.IsNullOrEmpty(st.instanceId) ? s.Artifacts.Get(st.instanceId) : null;
            var group = art != null ? s.Puzzles.GroupOf(art) : null;
            var dropGate = s.Eligibility != null ? s.Eligibility.Check(art, ArtifactAction.Discard) : Eligibility.Allowed;
            var destroyGate = s.Eligibility != null ? s.Eligibility.Check(art, ArtifactAction.Destroy) : Eligibility.Allowed;
            bool vehicleOut = d is VehicleItemDefinition vd && RoverDirector.Instance is RoverDirector rd && rd.IsDeployed(vd);

            _menu = new VisualElement();
            _menu.AddToClassList("xa-ctx");
            _menu.style.position = Position.Absolute;
            _menu.style.minWidth = 280;
            Text(_menu, name + (st.count > 1 ? $"  x{st.count}" : ""), "xa-term__h");
            if (group != null)
            {
                var puz = s.Puzzles; var spec = ArtifactPuzzleService.Spec(group);
                int found = puz.Pieces(group).Count;
                Text(_menu, $"{spec?.title ?? group} piece   ({found} found)", "xa-term__dim");
                bool can = puz.CanCombine(group);
                string label = puz.IsSolved(group) ? "View reconstructed star map" : "Combine with other artifact";
                string inst = st.instanceId, g = group;
                Btn(_menu, can ? label : "Combine (needs 2 puzzle pieces)", () => { HideMenu(); OpenPuzzle(g, inst); }, can);
            }
            if (vehicleOut) Text(_menu, "Recall the vehicle before dropping or destroying it.", "xa-term__warn");
            else
            {
                var dropStack = st.Clone();
                if (dropGate.IsBlocked) Text(_menu, "Drop: " + dropGate.Message, "xa-term__warn");
                else Btn(_menu, "Drop", () =>
                {
                    HideMenu();
                    void Go() => DoDrop(dropStack, remove, name);
                    if (dropGate.IsWarning) MenuScreens.Confirm(Body, dropGate.Message + $"\n\nDrop {name} in front of you?", Go);
                    else Go();
                });
                if (destroyGate.IsBlocked) Text(_menu, "Destroy: " + destroyGate.Message, "xa-term__warn");
                else Btn(_menu, "Destroy", () =>
                {
                    HideMenu();
                    string q = (destroyGate.IsWarning ? destroyGate.Message + "\n\n" : "") + $"Destroy {name}" + (st.count > 1 ? $" x{st.count}" : "") + "? This cannot be undone.";
                    MenuScreens.Confirm(Body, q, () => { remove(); Toast.Show($"Destroyed {name}."); Refresh(); });
                });
            }
            Btn(_menu, "Cancel", HideMenu).AddToClassList("xa-term__btn--ghost");
            Body.Add(_menu);
            var local = Body.WorldToLocal(screenPos);
            _menu.style.left = local.x; _menu.style.top = local.y;
            _menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_menu == null) return;
                var b = Body.contentRect;
                _menu.style.left = Mathf.Clamp(local.x, 0f, Mathf.Max(0f, b.width - _menu.layout.width));
                _menu.style.top = Mathf.Clamp(local.y, 0f, Mathf.Max(0f, b.height - _menu.layout.height));
            });
        }

        void DoDrop(ItemStack st, System.Action remove, string name)
        {
            if (!DroppedItemPickup.SpawnInFrontOfPlayer(st, Session.Items)) { Toast.Show("Can't drop that here."); return; }
            remove();
            Toast.Show("Dropped " + name + (st.count > 1 ? $" x{st.count}" : "") + ".");
            Refresh();
        }

        void HideMenu() { if (_menu != null) { _menu.RemoveFromHierarchy(); _menu = null; } }

        void OpenPuzzle(string group, string instanceId)
        {
            var w = GetComponent<ArtifactPuzzleWindow>();
            if (w == null) w = gameObject.AddComponent<ArtifactPuzzleWindow>();
            Close();
            w.Back = () => { w.Back = null; Open(); };
            w.Show(group, instanceId);
        }

        /// <summary>Where a missing ingredient comes from: a Refiner recipe or a crafting recipe that makes it.</summary>
        static string Source(GameSession s, string itemId)
        {
            var refine = s.Materials.Recipes.FirstOrDefault(rr => rr.outputs.Any(o => o.itemId == itemId));
            if (refine != null) return $"Refiner: {refine.displayName}";
            var craft = s.Crafting.Recipes.FirstOrDefault(cr => cr.outputItemId == itemId && s.Crafting.Known(cr));
            if (craft != null) return $"craft: {craft.Title}";
            return s.Materials.Name(itemId) != itemId ? "dig it at a site" : null;
        }

        /// <summary>What an upgrade actually changes, from the same tables the game uses.</summary>
        static string UpgradeEffect(GameSession s, string upgradeId, int from, int to)
        {
            var ship = s.Config.starterShip; var sc = s.Config.surfaceScanner;
            switch (upgradeId)
            {
                case GameSession.UpgradeHandheldScanner when sc != null:
                    float r0 = Mathf.Max(sc.range, DMI.Xenoarch.Galaxy.ScannerStats.Pick(sc.rangeByLevel, from, sc.range)), r1 = Mathf.Max(sc.range, DMI.Xenoarch.Galaxy.ScannerStats.Pick(sc.rangeByLevel, to, sc.range));
                    return $"Scan range {r0:0} -> {r1:0} m, accuracy {Mathf.Min(0.95f, sc.accuracy + 0.06f * from):P0} -> {Mathf.Min(0.95f, sc.accuracy + 0.06f * to):P0}, depth {sc.maxDepth + 0.5f * from:0.#} -> {sc.maxDepth + 0.5f * to:0.#} m";
                case GameSession.UpgradeOrbitalScanner when ship != null:
                    return $"Orbital scan radius {DMI.Xenoarch.Galaxy.ScannerStats.Pick(ship.orbitalScanRadiusByLevel, from, 400f):0} -> {DMI.Xenoarch.Galaxy.ScannerStats.Pick(ship.orbitalScanRadiusByLevel, to, 400f):0} m";
                case GameSession.UpgradeHold when ship != null && ship.holdSlotsByLevel != null && ship.holdSlotsByLevel.Length > 0:
                    return $"Crate slots {ship.holdSlotsByLevel[Mathf.Clamp(from, 0, ship.holdSlotsByLevel.Length - 1)]} -> {ship.holdSlotsByLevel[Mathf.Clamp(to, 0, ship.holdSlotsByLevel.Length - 1)]}";
            }
            return null;
        }

        bool IsAboardShip() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.StartsWith("Ship_");

        void DropToCrate(DMI.Xenoarch.Ship.StorageRole role, InventoryModel hold, DragDrop.Payload p)
        {
            var inv = Session.Inventory;
            if (p.index < 0 || p.index >= inv.Slots.Count || inv.Slots[p.index].IsEmpty) return;
            var st = inv.Slots[p.index];
            var d = Def(st.itemId);
            if (!IsAboardShip()) { Toast.Show("Crates are aboard the ship"); return; }
            if (d != null && !ShipHold.Accepts(role, d.category)) { Toast.Show($"The {role} crate does not take that"); return; }
            if (ShipHold.Transfer(inv, hold, st.Clone()) == 0) Toast.Show($"{role} crate is full");
            Refresh();
        }

        /// <summary>Hotbar slot dropped on the backpack: the stored stack returns to a backpack slot. Ghost assignments
        /// (default tools not yet owned) just clear the shortcut.</summary>
        void TakeOffHotbar(int index, int preferredIndex = -1)
        {
            var bar = Session.Hotbar; string id = bar.At(index);
            if (index <= 0 || id == null) return;
            bool hadStack = bar.StackAt(index) != null;
            if (!bar.TryReturnTo(index, Session.Inventory, preferredIndex))
            {
                Toast.Show("Backpack is full. Drop or destroy an item first.");
                return;
            }
            string name = Def(id)?.DisplayNameOrId ?? id;
            Toast.Show(hadStack
                ? $"{name} returned to the backpack."
                : Session.Inventory.Count(id) > 0
                    ? $"{name} taken off the hotbar. It is still in your backpack: drag it back onto any slot."
                    : $"{name} taken off the hotbar. You don't have one yet: craft it aboard the ship, then drag it from the backpack onto the hotbar.", 4f);
        }

        VisualElement HotbarTile(int k)
        {
            var bar = Session.Hotbar; string id = bar.At(k);
            var d = Def(id);
            var t = new VisualElement(); t.AddToClassList("xa-item-slot");
            t.style.width = 80; t.style.height = 80; t.style.marginRight = 4; t.style.marginBottom = 4;
            t.style.paddingLeft = t.style.paddingRight = t.style.paddingTop = t.style.paddingBottom = 0;
            t.style.backgroundColor = XenoarchPalette.PanelDark;
            t.style.borderTopWidth = t.style.borderBottomWidth = t.style.borderLeftWidth = t.style.borderRightWidth = 1;
            var bc = k == 0 ? XenoarchPalette.A(XenoarchPalette.Secondary, 0.25f) : XenoarchPalette.A(XenoarchPalette.Primary, 0.6f);
            t.style.borderTopColor = t.style.borderBottomColor = t.style.borderLeftColor = t.style.borderRightColor = bc;
            if (d != null && d.icon != null)
            {
                var img = new VisualElement { pickingMode = PickingMode.Ignore }; img.style.flexGrow = 1; XenoarchIcons.ApplyItem(img, d);
                if (Session.Inventory.Count(id) <= 0) img.style.opacity = 0.35f;
                t.Add(img);
            }
            else if (id != null) t.Add(new Label(d != null ? d.DisplayNameOrId : id) { pickingMode = PickingMode.Ignore, style = { fontSize = 9, whiteSpace = WhiteSpace.Normal } });
            var key = new Label((k + 1).ToString()) { pickingMode = PickingMode.Ignore };
            key.style.position = Position.Absolute; key.style.left = 3; key.style.top = 1; key.style.fontSize = 11; key.style.color = XenoarchPalette.Highlight;
            t.Add(key);
            bool owned = id != null && Session.Inventory.Count(id) > 0;
            t.tooltip = k == 0 ? "1: handheld scanner (fixed)" : id != null ? $"{k + 1}: {(d != null ? d.DisplayNameOrId : id)}" + (owned ? " (drag off to clear, right-click to drop or destroy)" : " (not owned yet: craft it aboard the ship)") : $"{k + 1}: empty (drag an item here)";
            if (k > 0 && bar.StackAt(k) != null)
            {
                int slot = k;
                t.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 1) return;
                    e.StopPropagation();
                    var held = Session.Hotbar.StackAt(slot);
                    if (held == null) return;
                    ShowItemMenu(held.Clone(), () => Session.Hotbar.Clear(slot), e.position);
                });
            }
            if (k > 0)
            {
                if (id != null) Drag.Source(t, () => new DragDrop.Payload { kind = "hotbar", index = k, itemId = id, icon = d != null ? d.icon : null });
                DragDrop.Target(t, p => p.kind == "backpack" || p.kind == "hotbar", p =>
                {
                    if (p.kind == "hotbar") bar.Swap(p.index, k);
                    else if (p.kind == "backpack")
                    {
                        if (!bar.AssignFrom(k, Session.Inventory, p.index))
                            Toast.Show("Backpack is full; couldn't swap the hotbar item back.");
                    }
                    Refresh();
                });
            }
            else DragDrop.Target(t, p => true, p => Toast.Show("Slot 1 is the scanner"));
            return t;
        }

        VisualElement Tile(ItemStack st, int index)
        {
            var t = new VisualElement();
            var inv0 = Session.Inventory;
            string itemId = st.itemId; bool empty = st.IsEmpty;
            Drag.Source(t, () => empty ? null : new DragDrop.Payload { kind = "backpack", index = index, itemId = itemId, container = inv0, icon = Def(itemId)?.icon });
            t.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || Drag.Dragging || empty) return;
                _selItem = itemId;
                Refresh();
            });
            DragDrop.Target(t, p => p.kind == "backpack" || p.kind == "hotbar", p =>
            {
                if (p.kind == "backpack") inv0.Swap(p.index, index);
                else TakeOffHotbar(p.index, index);
                Refresh();
            });
            t.AddToClassList("xa-item-slot");
            t.style.width = 80; t.style.height = 80; t.style.marginRight = 4; t.style.marginBottom = 4;
            t.style.paddingLeft = t.style.paddingRight = t.style.paddingTop = t.style.paddingBottom = 0;
            t.style.backgroundColor = XenoarchPalette.PanelDark;
            bool sel = !st.IsEmpty && st.itemId == _selItem;
            t.style.borderTopWidth = t.style.borderBottomWidth = t.style.borderLeftWidth = t.style.borderRightWidth = 1;
            var bc = sel ? XenoarchPalette.Highlight : XenoarchPalette.A(XenoarchPalette.Secondary, 0.4f);
            t.style.borderTopColor = t.style.borderBottomColor = t.style.borderLeftColor = t.style.borderRightColor = bc;
            if (st.IsEmpty) return t;
            var d = Def(st.itemId);
            if (d != null && d.icon != null)
            {
                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.style.flexGrow = 1; XenoarchIcons.ApplyItem(img, d);
                t.Add(img);
            }
            else t.Add(new Label(d != null ? d.DisplayNameOrId : st.itemId) { pickingMode = PickingMode.Ignore, style = { fontSize = 10, whiteSpace = WhiteSpace.Normal } });
            var art = !string.IsNullOrEmpty(st.instanceId) ? Session.Artifacts.Get(st.instanceId) : null;
            var group = art != null ? Session.Puzzles.GroupOf(art) : null;
            if (group != null)
            {
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.style.position = Position.Absolute; mark.style.left = 4; mark.style.top = 4; mark.style.width = 8; mark.style.height = 8; mark.style.backgroundColor = XenoarchPalette.Primary;
                t.Add(mark);
            }
            int slot = index;
            t.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1 || Session.Inventory.Slots[slot].IsEmpty) return;
                e.StopPropagation();
                var cur = Session.Inventory.Slots[slot];
                _selItem = cur.itemId;
                ShowItemMenu(cur.Clone(), () => Session.Inventory.TakeAt(slot), e.position);
            });
            if (st.count > 1)
            {
                var n = new Label(st.count.ToString()) { pickingMode = PickingMode.Ignore };
                n.style.position = Position.Absolute; n.style.right = 3; n.style.bottom = 1; n.style.fontSize = 12; n.style.color = Color.white;
                t.Add(n);
            }
            t.tooltip = (d != null ? d.DisplayNameOrId : st.itemId) + (group != null ? "  (puzzle piece)" : "") + "  (right-click: drop or destroy)";
            return t;
        }
    }
}
