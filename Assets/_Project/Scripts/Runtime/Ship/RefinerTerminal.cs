using System.Linq;
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
            if (_slotsRoot != null) BuildSlots(_slotsRoot);
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

            var recipes = ScrollCol(body, 0, true);
            Text(recipes, "RECIPES", "xa-term__h");
            if (m.Recipes.Count == 0) Text(recipes, "No recipes installed.");
            if (string.IsNullOrEmpty(_selectedId) && m.Recipes.Count > 0) _selectedId = m.Recipes[0].Id;
            foreach (var r in m.Recipes)
            {
                string lack = IngredientNote(r, 1);
                var btn = Btn(recipes, $"{r.displayName}: {Inputs(r, 1)} -> {Outputs(r, 1)} ({r.seconds:0} s)" + (lack != null ? $"\n{lack}" : ""),
                    () => { _selectedId = r.Id; Refresh(); });
                if (r.Id == _selectedId)
                {
                    btn.style.borderLeftWidth = 3;
                    btn.style.borderLeftColor = XenoarchPalette.Highlight;
                }
                else btn.AddToClassList("xa-term__btn--ghost");
            }

            var selected = m.Recipe(_selectedId);
            Text(recipes, "AMOUNT TO REFINE", "xa-term__h");
            if (selected == null) Text(recipes, "Select a recipe.");
            else BuildAmount(recipes, m, selected);

            var mid = ScrollCol(body, 1);
            Text(mid, "REFINER", "xa-term__h");
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
            int n = 0;
            var stock = Session.Stock;
            foreach (var id in stock.Ids().ToList())
            {
                var md = m.Material(id);
                if (md == null) continue;
                n++;
                string use = md.fuelValue > 0f ? $"fuel {md.fuelValue:0.#}" : md.reagentValue > 0 ? $"reagent {md.reagentValue}" : md.refined ? "refined" : "refine";
                int bp = inv.Count(id), allStock = stock.Count(id);
                Text(cargoCol, $"{md.DisplayNameOrId} x{allStock}  ({use})" + (allStock > bp ? $"  [{allStock - bp} in crates]" : ""));
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
            var m = Session.Materials;
            for (int slot = 0; slot < MaterialsService.RefinerSlots; slot++)
            {
                var block = new VisualElement();
                if (slot > 0) block.style.marginTop = 8;
                host.Add(block);
                if (slot >= m.Jobs.Count)
                {
                    Text(block, $"SLOT {slot + 1}  IDLE", "xa-term__h");
                    Text(block, "Empty.", "xa-term__dim");
                    continue;
                }

                var job = m.Jobs[slot];
                var r = m.Recipe(job.recipeId);
                int n = job.Batches;
                int uid = job.uid;
                Text(block, $"SLOT {slot + 1}  {(job.done ? "BATCH READY" : "REFINING")}", "xa-term__h");
                Text(block, r != null ? $"{r.displayName} ×{n}: {Outputs(r, n)}" : "?");
                Bar(block, job.elapsed / Mathf.Max(0.01f, job.duration));
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
                    Text(block, $"{Mathf.Max(0f, job.duration - job.elapsed):0} s left");
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
            b.style.minWidth = 52;
            b.style.width = StyleKeyword.Auto;
            b.style.marginTop = 2;
            b.style.marginBottom = 2;
            b.style.marginRight = 6;
            b.style.paddingLeft = 10;
            b.style.paddingRight = 10;
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
