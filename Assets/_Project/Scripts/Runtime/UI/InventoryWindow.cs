using System.Linq;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Crafting;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Materials;
using MaterialDefinition = DMI.Xenoarch.Materials.MaterialDefinition;
using DMI.Xenoarch.Ship;
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

        void Awake() { terminalName = "Inventory"; }

        protected override void Update()
        {
            bool wasOpen = IsOpen;
            base.Update();
            var s = Session;
            if (wasOpen && IsOpen && Time.unscaledTime - _openTime > 0.2f && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) Close();
            if (s == null) return;
            if (!_hooked)
            {
                _hooked = true;
                s.Crafting.Crafted += r => { Toast.Show(r.IsUpgrade ? $"Installed: {r.Title}" : $"Crafted {r.outputAmount}x {s.Crafting.Name(r.outputItemId)}"); _message = null; Refresh(); };
                s.Crafting.Failed += (r, why) => { _message = $"{r?.Title}: {why}"; Refresh(); };
            }
            if (IsOpen || UIState.BlocksGameplay || Time.frameCount == _closedFrame) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene == SceneNames.MainMenu || scene == SceneNames.Bootstrap || Scenes.SceneLoader.Busy) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) Open();
        }

        float _openTime;
        protected override void OnOpened() => _openTime = Time.unscaledTime;
        protected override void OnClosed() => _closedFrame = Time.frameCount;

        protected override void Tick()
        {
            if (Session.Crafting.Busy && Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }
        }

        ItemDefinition Def(string id) => id != null && Session.Items.TryGetItem(id, out var d) ? d : null;

        protected override void Build(VisualElement body)
        {
            _menu = null;
            if (!_menuHooked) { _menuHooked = true; body.RegisterCallback<PointerDownEvent>(e => { if (_menu != null && !_menu.worldBound.Contains(e.position)) HideMenu(); }); }
            var s = Session; var inv = s.Inventory;
            Status.text = $"Backpack {inv.Capacity - inv.FreeSlots}/{inv.Capacity}   |   Ship storage level {s.Exploration.Ship.holdLevel + 1}" + (s.AboardShip ? "   |   Aboard: ship workshop available" : "");
            Hint.text = "I / Tab / Esc: close   |   Move items in and out of the ship crates at the crates";

            // ---------------- backpack grid + ship crates
            var left = ScrollCol(body, 0, true);
            Text(left, "BACKPACK", "xa-term__h");
            var grid = new VisualElement(); grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; left.Add(grid);
            foreach (var st in inv.Slots) grid.Add(Tile(st));
            Text(left, "SHIP STORAGE", "xa-term__h");
            foreach (var kv in s.Holds)
            {
                var h = kv.Value; int used = h.Capacity - h.FreeSlots;
                var top = h.Slots.Where(x => !x.IsEmpty).GroupBy(x => x.itemId).Select(g => $"{Def(g.Key)?.DisplayNameOrId ?? g.Key} x{g.Sum(x => x.count)}").Take(4);
                Text(left, $"{kv.Key} crate: {used}/{h.Capacity} slots" + (used > 0 ? $"  ({string.Join(", ", top)}{(h.Slots.Count(x => !x.IsEmpty) > 4 ? ", ..." : "")})" : ""), "xa-term__dim");
            }

            // ---------------- details
            var mid = ScrollCol(body, 1);
            Text(mid, "DETAILS", "xa-term__h");
            var d = Def(_selItem);
            if (d == null) Text(mid, "Select an item.", "xa-term__dim");
            else
            {
                if (d.icon != null) { var big = new VisualElement(); big.style.width = 96; big.style.height = 96; big.style.backgroundImage = new StyleBackground(d.icon); big.style.marginBottom = 6; mid.Add(big); }
                Text(mid, d.DisplayNameOrId.ToUpperInvariant(), "xa-term__h");
                Text(mid, $"{d.category}   |   value {d.baseValue}   |   mass {d.mass:0.##}");
                if (!string.IsNullOrEmpty(d.description)) Text(mid, d.description, "xa-term__dim");
                int bp = inv.Count(d.Id), all = s.Stock.Count(d.Id);
                Text(mid, $"Backpack {bp}   |   Ship crates {all - bp}");
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

        /// <summary>Right-click menu for a puzzle piece.</summary>
        void ShowPieceMenu(string instanceId, string group, Vector2 screenPos)
        {
            HideMenu();
            var s = Session; var p = s.Puzzles; var spec = ArtifactPuzzleService.Spec(group);
            _menu = new VisualElement();
            _menu.style.position = Position.Absolute; _menu.style.minWidth = 300;
            _menu.style.backgroundColor = XenoarchPalette.PanelDeep;
            _menu.style.paddingLeft = _menu.style.paddingRight = _menu.style.paddingTop = _menu.style.paddingBottom = 6;
            _menu.style.borderTopWidth = _menu.style.borderBottomWidth = _menu.style.borderLeftWidth = _menu.style.borderRightWidth = 1;
            _menu.style.borderTopColor = _menu.style.borderBottomColor = _menu.style.borderLeftColor = _menu.style.borderRightColor = XenoarchPalette.Highlight;
            int found = p.Pieces(group).Count;
            Text(_menu, $"{spec?.title ?? group} piece   ({found} found)", "xa-term__dim");
            bool can = p.CanCombine(group);
            string label = p.IsSolved(group) ? "View reconstructed star map" : "Combine with other artifact";
            Btn(_menu, can ? label : "Combine (needs 2 puzzle pieces)", () => { HideMenu(); OpenPuzzle(group, instanceId); }, can);
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

        VisualElement Tile(ItemStack st)
        {
            var t = new Button(() => { if (!st.IsEmpty) { _selItem = st.itemId; Refresh(); } }) { text = "" };
            t.style.width = 64; t.style.height = 64; t.style.marginRight = 4; t.style.marginBottom = 4;
            t.style.paddingLeft = t.style.paddingRight = t.style.paddingTop = t.style.paddingBottom = 4;
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
                img.style.flexGrow = 1; img.style.backgroundImage = new StyleBackground(d.icon);
                if (d is MaterialDefinition m) img.style.unityBackgroundImageTintColor = Color.Lerp(Color.white, m.tint, 0.35f);
                t.Add(img);
            }
            else t.Add(new Label(d != null ? d.DisplayNameOrId : st.itemId) { pickingMode = PickingMode.Ignore, style = { fontSize = 10, whiteSpace = WhiteSpace.Normal } });
            // Puzzle pieces: right-click menu (Combine with other artifact).
            var art = !string.IsNullOrEmpty(st.instanceId) ? Session.Artifacts.Get(st.instanceId) : null;
            var group = art != null ? Session.Puzzles.GroupOf(art) : null;
            if (group != null)
            {
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.style.position = Position.Absolute; mark.style.left = 4; mark.style.top = 4; mark.style.width = 8; mark.style.height = 8; mark.style.backgroundColor = XenoarchPalette.Primary;
                t.Add(mark);
                string inst = st.instanceId, item = st.itemId;
                t.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { e.StopPropagation(); _selItem = item; ShowPieceMenu(inst, group, e.position); } });
            }
            if (st.count > 1)
            {
                var n = new Label(st.count.ToString()) { pickingMode = PickingMode.Ignore };
                n.style.position = Position.Absolute; n.style.right = 3; n.style.bottom = 1; n.style.fontSize = 12; n.style.color = Color.white;
                t.Add(n);
            }
            t.tooltip = (d != null ? d.DisplayNameOrId : st.itemId) + (group != null ? "  (puzzle piece: right-click)" : "");
            return t;
        }
    }
}
