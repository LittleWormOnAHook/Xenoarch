using System.Linq;
using DMI.Xenoarch.Materials;
using DMI.Xenoarch.UI;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship refiner (Mk I): turns raw planet materials into fuel cells and lab reagents (one timed batch at a time, data
    /// from RefineRecipe assets), refuels the ship from fuel items in cargo, and lists the materials aboard.
    /// </summary>
    public sealed class RefinerTerminal : TerminalWindow
    {
        protected override string PanelId => "terminal.refiner";
        string _message;
        float _nextRefresh;

        protected override void OnOpened() => Session.Materials.RefineCompleted += OnDone;
        protected override void OnClosed() { if (Session != null) Session.Materials.RefineCompleted -= OnDone; }
        void OnDone(RefineRecipe r) { _message = $"Batch done: {Outputs(r)}"; Refresh(); }

        protected override void Tick()
        {
            if (Session.Materials.Busy && Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.25f; Refresh(); }
        }

        string Inputs(RefineRecipe r) => string.Join(" + ", r.inputs.Select(i => $"{i.count} {Session.Materials.Name(i.itemId)}"));
        string Outputs(RefineRecipe r) => string.Join(" + ", r.outputs.Select(o => $"{o.count} {Session.Materials.Name(o.itemId)}"));

        protected override void Build(VisualElement body)
        {
            var m = Session.Materials; var x = Session.Exploration; var inv = Session.Inventory;
            Status.text = m.Busy ? (m.State.job.done ? "BATCH READY" : "REFINING") : "IDLE";

            var recipes = ScrollCol(body, 0, true);
            Text(recipes, "RECIPES", "xa-term__h");
            if (m.Recipes.Count == 0) Text(recipes, "No recipes installed.");
            foreach (var r in m.Recipes)
            {
                bool can = m.CanRefine(r, out var why);
                Btn(recipes, $"{r.displayName}: {Inputs(r)} -> {Outputs(r)} ({r.seconds:0} s)" + (can ? "" : $"\n{why}"),
                    () => { _message = m.StartRefine(r, out var w) ? null : w; Refresh(); }, can);
            }

            var mid = ScrollCol(body, 1);
            Text(mid, "REFINER", "xa-term__h");
            if (m.Busy)
            {
                var r = m.Recipe(m.State.job.recipeId);
                Text(mid, r != null ? $"{r.displayName}: {Outputs(r)}" : "?");
                Bar(mid, m.State.job.elapsed / Mathf.Max(0.01f, m.State.job.duration));
                if (m.State.job.done)
                {
                    Text(mid, "Finished. Cargo is full: make room, then collect.", "xa-term__warn");
                    Btn(mid, "Collect", () => { _message = m.TryDeliver() ? "Collected." : "Still no room in cargo."; Refresh(); });
                }
                else
                {
                    Text(mid, $"{Mathf.Max(0f, m.State.job.duration - m.State.job.elapsed):0} s left");
                    Btn(mid, "Cancel (returns inputs)", () => { m.CancelRefine(); Refresh(); }).AddToClassList("xa-term__btn--ghost");
                }
            }
            else Text(mid, "Idle. One batch at a time (Mk I refiner).");
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
                int bp = inv.Count(id), all = stock.Count(id);
                Text(cargoCol, $"{md.DisplayNameOrId} x{all}  ({use})" + (all > bp ? $"  [{all - bp} in crates]" : ""));
            }
            if (n == 0) Text(cargoCol, "None. Collect deposits on planet surfaces (hold E).");
            Text(cargoCol, $"Lab reagents: {m.ReagentUnits()} units", "xa-term__dim");
        }
    }
}
