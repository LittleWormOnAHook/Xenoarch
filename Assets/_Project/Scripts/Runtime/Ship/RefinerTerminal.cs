using System.Linq;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Materials;
using DMI.Xenoarch.UI;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship refiner: turns raw planet materials into fuel cells and lab reagents (up to four timed jobs at once, with
    /// an amount control per start; data from RefineRecipe assets), refuels the ship from fuel items in cargo, and lists
    /// the materials aboard.
    /// </summary>
    public sealed class RefinerTerminal : TerminalWindow
    {
        protected override string PanelId => "terminal.refiner";
        string _message;
        float _nextRefresh;
        string _selectedId;
        int _amount = 1;
        VisualElement _slotsRoot;
        string _baySig;
        string _selMat;
        readonly System.Collections.Generic.List<VisualElement> _matTiles = new System.Collections.Generic.List<VisualElement>();
        readonly System.Collections.Generic.List<(Label lbl, VisualElement fill, int uid)> _bayLive = new System.Collections.Generic.List<(Label, VisualElement, int)>();

        protected override void OnOpened() => Session.Materials.RefineCompleted += OnDone;
        protected override void OnClosed() { if (Session != null) Session.Materials.RefineCompleted -= OnDone; }
        void OnDone(RefineRecipe r, int batches)
        {
            int n = Mathf.Max(1, batches);
            _message = r != null ? $"Batch done: {Outputs(r, n)}" : "Batch done.";
            Refresh();
        }

        protected override void Tick()
        {
            if (!Session.Materials.Busy || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            UpdateStatus();
            if (_slotsRoot == null) return;
            // Only rebuild the bays when a job starts / finishes / is cancelled (and never mid-click): otherwise update
            // the countdown text and bar in place, so Cancel / Collect buttons keep their hover state and every click lands.
            if (BaySig() != _baySig)
            {
                if (!UIClickKit.Busy) BuildSlots(_slotsRoot);
                return;
            }
            var m = Session.Materials;
            foreach (var (lbl, fill, uid) in _bayLive)
            {
                foreach (var j in m.Jobs)
                {
                    if (j.uid != uid) continue;
                    lbl.text = $"{Mathf.Max(0f, j.duration - j.elapsed):0} s left · x{j.Batches}";
                    fill.style.width = Length.Percent(Mathf.Clamp01(j.elapsed / Mathf.Max(0.01f, j.duration)) * 100f);
                    break;
                }
            }
        }

        string BaySig()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var j in Session.Materials.Jobs) sb.Append(j.uid).Append(j.done ? 'd' : 'r').Append(',');
            return sb.ToString();
        }

        string Inputs(RefineRecipe r, int n) => string.Join(" + ", r.inputs.Select(i => $"{i.count * n} {Session.Materials.Name(i.itemId)}"));
        string Outputs(RefineRecipe r, int n) => string.Join(" + ", r.outputs.Select(o => $"{o.count * n} {Session.Materials.Name(o.itemId)}"));

        void UpdateStatus()
        {
            var m = Session.Materials;
            int done = 0, run = 0;
            foreach (var j in m.Jobs) { if (j.done) done++; else run++; }
            Status.text = done > 0 ? "BATCH READY" : run > 0 ? $"{run}/{MaterialsService.RefinerSlots} REFINING" : "IDLE";
        }

        protected override void Build(VisualElement body)
        {
            var m = Session.Materials; var x = Session.Exploration; var inv = Session.Inventory;
            UpdateStatus();
            Hint.text = "E: select   |   Amount   |   Enter: start   |   Esc: close";

            var recipes = ScrollCol(body, 0, true);
            var rh = new VisualElement(); rh.style.flexDirection = FlexDirection.Row; rh.style.justifyContent = Justify.SpaceBetween; recipes.Add(rh);
            Text(rh, "RECIPES", "xa-term__h");
            Text(rh, $"{m.Recipes.Count} INSTALLED", "xa-term__dim");
            if (m.Recipes.Count == 0) Text(recipes, "No recipes installed.");
            if (string.IsNullOrEmpty(_selectedId) && m.Recipes.Count > 0) _selectedId = m.Recipes[0].Id;
            foreach (var r in m.Recipes)
            {
                var rec = r;
                string lack = IngredientNote(rec, 1);
                var row = new Button(() => { _selectedId = rec.Id; Refresh(); });
                row.AddToClassList("xa-recipe");
                if (rec.Id == _selectedId) row.AddToClassList("xa-recipe--sel");
                var outDef = rec.outputs.Count > 0 && Session.Items.TryGetItem(rec.outputs[0].itemId, out var od) ? od : null;
                var iconWrap = ItemSlotUI.Create(80, rec.Id == _selectedId);
                ItemSlotUI.Paint(iconWrap, outDef, rec.outputs.Count > 0 ? new ItemStack(rec.outputs[0].itemId, rec.outputs[0].count) : new ItemStack(), rec.Id == _selectedId);
                iconWrap.pickingMode = PickingMode.Ignore;
                row.Add(iconWrap);
                row.style.alignItems = Align.Center;
                row.style.minHeight = 96;
                if (outDef != null)
                {
                    // Left-click selects the recipe; right-click inspects what it makes (docked inspector).
                    var outStack = new ItemStack(outDef.Id, 1);
                    row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) { e.StopPropagation(); ItemInspector.Open(outStack, e.position); } });
                }
                var info = new VisualElement(); info.style.flexGrow = 1; info.style.marginLeft = 10;
                var nm = new Label((rec.displayName ?? rec.Id).ToUpperInvariant()); nm.AddToClassList("xa-recipe__name"); info.Add(nm);
                var io = new VisualElement(); io.style.flexDirection = FlexDirection.Row; io.style.alignItems = Align.Center; io.style.flexWrap = Wrap.Wrap;
                void AddAmt(ItemAmount a)
                {
                    Session.Items.TryGetItem(a.itemId, out var d);
                    var chip = new VisualElement(); chip.style.flexDirection = FlexDirection.Row; chip.style.alignItems = Align.Center; chip.style.marginRight = 10; chip.style.marginTop = 2; chip.style.marginBottom = 2;
                    chip.Add(ItemSlotUI.Icon(d, 32));
                    var lab = new Label("x" + a.count); lab.AddToClassList("xa-term__dim"); lab.style.fontSize = 14; lab.style.marginLeft = 4; chip.Add(lab);
                    if (d != null) UIClickKit.InspectOnRightClick(chip, () => new ItemStack(d.Id, 1));
                    chip.pickingMode = PickingMode.Position; lab.pickingMode = PickingMode.Ignore;
                    io.Add(chip);
                }
                foreach (var inp in rec.inputs) AddAmt(inp);
                var arrow = new Label("→"); arrow.style.color = XenoarchPalette.Highlight; arrow.style.marginRight = 6; io.Add(arrow);
                foreach (var o in rec.outputs) AddAmt(o);
                var sec = new Label($"{rec.seconds:0} s"); sec.AddToClassList("xa-term__dim"); sec.style.marginLeft = 8; sec.style.fontSize = 12; io.Add(sec);
                info.Add(io);
                if (lack != null) { var w = new Label(lack); w.AddToClassList("xa-term__warn"); w.style.fontSize = 12; info.Add(w); }
                row.Add(info);
                recipes.Add(row);
            }

            var selected = m.Recipe(_selectedId);
            Text(recipes, "AMOUNT TO REFINE", "xa-term__h");
            if (selected == null) Text(recipes, "Select a recipe.");
            else BuildAmount(recipes, m, selected);

            var mid = ScrollCol(body, 1);
            _slotsRoot = new VisualElement();
            mid.Add(_slotsRoot);
            BuildSlots(_slotsRoot);
            if (!string.IsNullOrEmpty(_message)) Text(mid, _message);

            Text(mid, "SHIP FUEL", "xa-term__h");
            Text(mid, $"{x.Ship.fuel:0.#} / {x.Ship.fuelCapacity:0} units");
            Bar(mid, x.Ship.fuel / Mathf.Max(1f, x.Ship.fuelCapacity));
            float cargo = m.FuelInCargo();
            Text(mid, $"Fuel in cargo: {cargo:0.#} units", "xa-term__dim");
            Btn(mid, "Refuel ship", () => { float a = m.Refuel(x); _message = a > 0f ? $"Refuelled +{a:0.#}." : "Nothing to refuel with (or tank full)."; Refresh(); },
                cargo > 0f && x.Ship.fuel < x.Ship.fuelCapacity - 0.01f);

            var rover = RoverDirector.Instance;
            if (rover != null)
            {
                Text(mid, "ROVER FUEL", "xa-term__h");
                Text(mid, $"{rover.Fuel:0.#} / {rover.FuelCapacity:0} units");
                Bar(mid, rover.Fuel01);
                Btn(mid, "Refuel rover from cargo", () => { float a = rover.RefuelFromCargo(); _message = a > 0f ? $"Rover refuelled +{a:0.#}." : "Nothing to refuel with (or tank full)."; Refresh(); },
                    cargo > 0f && rover.Fuel < rover.FuelCapacity - 0.01f);
                Btn(mid, "Transfer 10 from ship", () => { float a = rover.TransferFromShip(10f); _message = a > 0f ? $"Transferred {a:0.#} to the rover." : "Nothing to transfer (or tank full)."; Refresh(); },
                    x.Ship.fuel > 0.5f && rover.Fuel < rover.FuelCapacity - 0.01f);
                Btn(mid, "Fill rover from ship", () => { float a = rover.TransferFromShip(rover.FuelCapacity); _message = a > 0f ? $"Transferred {a:0.#} to the rover." : "Nothing to transfer (or tank full)."; Refresh(); },
                    x.Ship.fuel > 0.5f && rover.Fuel < rover.FuelCapacity - 0.01f);
            }

            var cargoCol = ScrollCol(body, 2);
            Text(cargoCol, "MATERIALS ABOARD", "xa-term__h");
            Text(cargoCol, "Backpack + ship crates. Inputs come from the backpack first; products go to the backpack, then a crate.", "xa-term__dim");
            var matRow = new VisualElement(); matRow.style.flexDirection = FlexDirection.Row; matRow.style.flexWrap = Wrap.Wrap; cargoCol.Add(matRow);
            int n = 0;
            _matTiles.Clear();
            var stock = Session.Stock;
            foreach (var id in stock.Ids().ToList())
            {
                var md = m.Material(id);
                if (md == null) continue;
                n++;
                int allStock = stock.Count(id);
                bool isSel = id == _selMat;
                var tile = ItemSlotUI.Create(80, isSel);
                ItemSlotUI.Paint(tile, md, new ItemStack(id, allStock), isSel, ResourceGroups.GroupLabel(md) + "\nLeft-click: select   Right-click: inspect");
                string matId = id; int matCount = allStock;
                _matTiles.Add(tile);
                tile.userData = matId;
                tile.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1) { e.StopPropagation(); ItemInspector.Open(new ItemStack(matId, matCount), e.position); return; }
                    if (e.button != 0) return;
                    _selMat = matId;
                    foreach (var t in _matTiles) ItemSlotUI.SetSelected(t, (string)t.userData == matId);
                    if (ItemInspector.IsOpen) ItemInspector.Open(new ItemStack(matId, matCount), e.position);
                });
                matRow.Add(tile);
            }
            if (n == 0) Text(cargoCol, "None. Collect deposits on planet surfaces (hold E).");
            Text(cargoCol, $"Lab reagents: {m.ReagentUnits()} units", "xa-term__dim");
        }

        void BuildAmount(VisualElement recipes, MaterialsService m, RefineRecipe selected)
        {
            int all = m.AffordableBatches(selected);
            int max = m.MaxFittingBatches(selected);
            if (all > 0) _amount = Mathf.Clamp(_amount, 1, all);
            else _amount = 1;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            recipes.Add(row);

            var count = new Label(_amount.ToString());
            count.AddToClassList("xa-term__text");
            count.style.unityFontStyleAndWeight = FontStyle.Bold;
            count.style.marginLeft = 6;
            count.style.marginRight = 6;
            count.style.minWidth = 28;
            count.style.unityTextAlign = TextAnchor.MiddleCenter;

            Label summary = null, fit = null, warn = null, whyLbl = null;
            Button minus = null, plus = null, start = null;

            void SetAmount(int value, bool syncSlider, SliderInt sl)
            {
                int v = all > 0 ? Mathf.Clamp(value, 1, all) : 1;
                _amount = v;
                count.text = v.ToString();
                if (summary != null)
                    summary.text = $"{v} batch{(v == 1 ? "" : "es")}: {Inputs(selected, v)} -> {Outputs(selected, v)} ({selected.seconds * v:0} s)";
                if (warn != null) warn.style.display = (all > 0 && v > max) ? DisplayStyle.Flex : DisplayStyle.None;
                if (minus != null) minus.SetEnabled(all > 0 && v > 1);
                if (plus != null) plus.SetEnabled(all > 0 && v < all);
                if (start != null)
                {
                    bool can = m.CanRefine(selected, v, out var why);
                    start.SetEnabled(can);
                    if (whyLbl != null)
                    {
                        whyLbl.text = !can && !string.IsNullOrEmpty(why) ? why : "";
                        whyLbl.style.display = whyLbl.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                }
                if (syncSlider && sl != null && sl.value != v) sl.SetValueWithoutNotify(v);
            }

            CompactBtn(row, "Min", () => { SetAmount(1, true, row.userData as SliderInt); }, all > 0);
            minus = CompactBtn(row, "−", () => { SetAmount(_amount - 1, true, row.userData as SliderInt); }, all > 0 && _amount > 1);
            row.Add(count);
            plus = CompactBtn(row, "+", () => { SetAmount(_amount + 1, true, row.userData as SliderInt); }, all > 0 && _amount < all);
            CompactBtn(row, "Max", () => { SetAmount(Mathf.Max(1, max), true, row.userData as SliderInt); }, max > 0);
            CompactBtn(row, "All", () => { SetAmount(Mathf.Max(1, all), true, row.userData as SliderInt); }, all > 0);

            var slider = new SliderInt(1, Mathf.Max(1, all)) { value = _amount, showInputField = false };
            slider.SetEnabled(all > 0);
            slider.style.marginTop = 2;
            slider.style.marginBottom = 4;
            slider.style.minHeight = 18;
            row.userData = slider;
            slider.RegisterValueChangedCallback(e => SetAmount(e.newValue, false, slider));
            recipes.Add(slider);
            StyleSlider(slider);

            summary = Text(recipes, $"{_amount} batch{(_amount == 1 ? "" : "es")}: {Inputs(selected, _amount)} -> {Outputs(selected, _amount)} ({selected.seconds * _amount:0} s)", "xa-term__dim");
            fit = Text(recipes, $"Min 1 · Max (fits) {max} · All (stock) {all}", "xa-term__dim");
            warn = Text(recipes, "Cargo may not have room for all outputs; collect when there's space.", "xa-term__warn");
            warn.style.display = (all > 0 && _amount > max) ? DisplayStyle.Flex : DisplayStyle.None;

            bool canStart = m.CanRefine(selected, _amount, out var startWhy);
            start = Btn(recipes, "Start refine", () =>
            {
                _message = m.StartRefine(selected, _amount, out var w) ? $"Started {_amount} batch{(_amount == 1 ? "" : "es")}." : w;
                Refresh();
            }, canStart);
            whyLbl = Text(recipes, !canStart && !string.IsNullOrEmpty(startWhy) ? startWhy : "", "xa-term__dim");
            whyLbl.style.display = whyLbl.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        string IngredientNote(RefineRecipe r, int batches)
        {
            var m = Session.Materials;
            foreach (var i in r.inputs)
            {
                int need = i.count * batches, have = m.Pool.Count(i.itemId);
                if (have < need) return $"Needs {need} {m.Name(i.itemId)} (have {have}).";
            }
            return null;
        }

        void BuildSlots(VisualElement host)
        {
            host.Clear();
            _bayLive.Clear();
            _baySig = BaySig();
            var m = Session.Materials;
            Text(host, "REFINER BAYS", "xa-term__h");
            for (int slot = 0; slot < MaterialsService.RefinerSlots; slot++)
            {
                var block = new VisualElement();
                block.AddToClassList("xa-refiner-bay");
                host.Add(block);
                if (slot >= m.Jobs.Count)
                {
                    block.AddToClassList("xa-refiner-bay--empty");
                    Text(block, $"BAY {slot + 1}  ·  EMPTY", "xa-term__dim");
                    continue;
                }

                var job = m.Jobs[slot];
                var r = m.Recipe(job.recipeId);
                int n = job.Batches;
                int uid = job.uid;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                block.Add(row);
                var outDef = r != null && r.outputs.Count > 0 && Session.Items.TryGetItem(r.outputs[0].itemId, out var od) ? od : null;
                var icon = ItemSlotUI.Create(80, job.done);
                ItemSlotUI.Paint(icon, outDef, r != null && r.outputs.Count > 0 ? new ItemStack(r.outputs[0].itemId, r.outputs[0].count * n) : new ItemStack(), job.done, "Right-click: inspect");
                if (outDef != null) UIClickKit.InspectOnRightClick(icon, () => new ItemStack(outDef.Id, 1));
                row.Add(icon);
                var info = new VisualElement(); info.style.flexGrow = 1; info.style.marginLeft = 10; row.Add(info);
                var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.justifyContent = Justify.SpaceBetween;
                Text(head, r != null ? r.displayName.ToUpperInvariant() : "?", "xa-term__h");
                if (job.done)
                {
                    var badge = new Label("COLLECT"); badge.AddToClassList("xa-inspect__chip"); badge.style.color = XenoarchPalette.Highlight; head.Add(badge);
                }
                info.Add(head);
                if (job.done) Text(info, $"Ready · x{n}", "xa-term__dim");
                Label timeLbl = null;
                if (!job.done) timeLbl = Text(info, $"{Mathf.Max(0f, job.duration - job.elapsed):0} s left · x{n}", "xa-term__dim");
                var barFill = Bar(info, job.elapsed / Mathf.Max(0.01f, job.duration));
                if (timeLbl != null) _bayLive.Add((timeLbl, barFill, uid));
                if (job.done)
                {
                    Text(block, "Finished. Cargo is full: make room, then collect.", "xa-term__warn");
                    Btn(block, "Collect", () =>
                    {
                        _message = m.TryDeliverUid(uid) ? "Collected." : "Still no room in cargo.";
                        Refresh();
                    });
                }
                else
                {
                    Btn(block, "Cancel (returns inputs)", () =>
                    {
                        _message = m.CancelRefineUid(uid) ? "Cancelled. Inputs returned." : "Could not cancel.";
                        Refresh();
                    }).AddToClassList("xa-term__btn--ghost");
                }
            }
        }

        static Button CompactBtn(VisualElement parent, string text, System.Action onClick, bool enabled)
        {
            var b = Btn(parent, text, onClick, enabled);
            b.style.flexGrow = 0;
            b.style.flexShrink = 0;
            b.style.minWidth = 76;
            b.style.minHeight = 46;
            b.style.width = StyleKeyword.Auto;
            b.style.marginTop = 3;
            b.style.marginBottom = 3;
            b.style.marginRight = 8;
            b.style.paddingLeft = 14;
            b.style.paddingRight = 14;
            return b;
        }

        static void StyleSlider(SliderInt slider)
        {
            slider.schedule.Execute(() =>
            {
                var tracker = slider.Q("unity-tracker");
                if (tracker != null)
                {
                    tracker.style.backgroundColor = XenoarchPalette.PanelDeep;
                    tracker.style.height = 10;
                }
                var dragger = slider.Q("unity-dragger");
                if (dragger != null)
                {
                    dragger.style.backgroundColor = XenoarchPalette.Bar;
                    dragger.style.width = 14;
                    dragger.style.height = 14;
                    dragger.style.borderTopLeftRadius = 2;
                    dragger.style.borderTopRightRadius = 2;
                    dragger.style.borderBottomLeftRadius = 2;
                    dragger.style.borderBottomRightRadius = 2;
                }
            });
        }
    }
}
