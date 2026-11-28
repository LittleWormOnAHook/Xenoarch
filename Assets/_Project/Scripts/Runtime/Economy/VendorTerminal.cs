using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.UI;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Economy
{
    /// <summary>
    /// Phase 1D vendor window (data: <see cref="VendorDefinition"/>): sell from the backpack (artifacts at their appraised
    /// value, valuables and materials at their base value) x the vendor's rate + a notoriety bonus; buy stock (rank-gated,
    /// optional one-off); refuel the ship for credits. Every deal is logged in the wallet.
    /// </summary>
    public sealed class VendorTerminal : TerminalWindow
    {
        public VendorDefinition vendor;
        protected override string PanelId => "vendor." + (vendor != null ? vendor.Id : "none");
        string _message;

        public override string GetPrompt(in DMI.Xenoarch.Interaction.InteractionContext c) => $"Trade: {terminalName}";

        EconomyService Eco => Session.Economy;
        ItemDefinition Def(string id) => id != null && Session.Items.TryGetItem(id, out var d) ? d : null;
        float Rate => vendor.buyMultiplier * (1f + vendor.bonusPerRank * Eco.Rank);

        /// <summary>What this vendor pays for one of the stack (0 = not interested).</summary>
        public int OfferFor(ItemStack st)
        {
            if (vendor == null || st.IsEmpty) return 0;
            var d = Def(st.itemId);
            if (d == null || !vendor.buys.Contains(d.category)) return 0;
            if (!string.IsNullOrEmpty(st.instanceId))
            {
                var a = Session.Artifacts.Get(st.instanceId);
                if (a != null) return Mathf.Max(1, Mathf.RoundToInt(Session.Artifacts.Value(a) * Rate));
            }
            return Mathf.Max(d.baseValue > 0 ? 1 : 0, Mathf.RoundToInt(d.baseValue * Rate));
        }

        string Name(ItemStack st)
        {
            if (!string.IsNullOrEmpty(st.instanceId)) { var a = Session.Artifacts.Get(st.instanceId); if (a != null) return Session.Artifacts.DisplayName(a) + $" ({a.condition * 100f:0}%)"; }
            var d = Def(st.itemId); return d != null ? d.DisplayNameOrId : st.itemId;
        }

        /// <summary>Instance waiting for a second click after an eligibility warning (Phase 2 M1).</summary>
        string _confirmInstance;

        void Sell(ItemStack st, int count)
        {
            int each = OfferFor(st);
            if (each <= 0) return;
            var inv = Session.Inventory;
            if (!string.IsNullOrEmpty(st.instanceId))
            {
                var a = Session.Artifacts.Get(st.instanceId);
                string label = Name(st), inst = st.instanceId, itemId = st.itemId;
                if (a != null)
                {
                    // Phase 2 M1: the artifact service checks the eligibility gate; the UI cannot bypass it.
                    bool confirmed = _confirmInstance == inst;
                    if (!Session.Artifacts.TrySell(a, confirmed, () => inv.RemoveInstance(inst), out var verdict))
                    {
                        if (verdict.IsWarning) { _confirmInstance = inst; _message = $"WARNING: {verdict.Message}\nClick sell again to confirm."; }
                        else { _confirmInstance = null; _message = verdict.Message; Toast.Show(verdict.Message, 5f); }
                        Refresh();
                        return;
                    }
                    _confirmInstance = null;
                    Eco.Award(Eco.Settings.artifactSold, "sold:" + a.instanceId);
                }
                else if (!inv.RemoveInstance(inst)) return;
                Eco.Earn(each, "sell", vendor.Id, itemId, inst, 1);
                _message = $"Sold {label} for {each} cr.";
            }
            else
            {
                int n = Mathf.Min(count, inv.Count(st.itemId));
                int got = inv.Remove(st.itemId, n);
                if (got <= 0) return;
                Eco.Earn(each * got, "sell", vendor.Id, st.itemId, null, got);
                _message = $"Sold {got}x {Name(st)} for {each * got} cr.";
            }
            Refresh();
        }

        void Buy(VendorStock s)
        {
            var d = Def(s.itemId);
            if (d == null || !Eco.CanAfford(s.price)) return;
            if (Session.Inventory.Add(s.itemId, 1) > 0)
            {
                var stock = s; var def = d;
                OverflowPrompt.OfferRoute(new ItemStack(stock.itemId, 1),
                    "Backpack is full. Send this purchase to the ship, or stash it in a nearby shuttle or rover.",
                    dest =>
                    {
                        if (VehicleCargo.Send(null, dest.Inventory, new ItemStack(stock.itemId, 1), fromWorld: true) <= 0)
                        { Toast.Show($"{dest.Label} is full."); return; }
                        Eco.Spend(stock.price, "buy", vendor.Id, stock.itemId, 1);
                        if (stock.unique && !Eco.Wallet.boughtUnique.Contains(vendor.Id + ":" + stock.itemId)) Eco.Wallet.boughtUnique.Add(vendor.Id + ":" + stock.itemId);
                        Toast.Show($"Bought {def.DisplayNameOrId} for {stock.price} cr. Sent to the {(dest.Id == "ship" ? "ship" : dest.Id)}.");
                        Refresh();
                    });
                _message = "Backpack full.";
                Refresh();
                return;
            }
            Eco.Spend(s.price, "buy", vendor.Id, s.itemId, 1);
            if (s.unique && !Eco.Wallet.boughtUnique.Contains(vendor.Id + ":" + s.itemId)) Eco.Wallet.boughtUnique.Add(vendor.Id + ":" + s.itemId);
            _message = $"Bought {d.DisplayNameOrId} for {s.price} cr.";
            Refresh();
        }

        int UpgradePrice(VendorUpgrade u, int level) => u.priceByLevel == null || u.priceByLevel.Length == 0 ? 0 : u.priceByLevel[Mathf.Min(level, u.priceByLevel.Length - 1)];

        void BuyUpgrade(VendorUpgrade u)
        {
            int lvl = Session.UpgradeLevel(u.upgradeId), price = UpgradePrice(u, lvl);
            if (price <= 0 || !Eco.CanAfford(price) || lvl >= Session.UpgradeMax(u.upgradeId)) return;
            if (!Session.ApplyUpgrade(u.upgradeId)) { _message = "Upgrade could not be fitted."; Refresh(); return; }
            Eco.Spend(price, "upgrade", vendor.Id, u.upgradeId, 1);
            _message = $"Fitted {u.label} level {lvl + 2} for {price} cr.";
            Refresh();
        }

        void RefuelRover(float units)
        {
            var d = RoverDirector.Instance;
            if (d == null) { _message = "No rover systems."; Refresh(); return; }
            float room = Mathf.Max(0f, d.FuelCapacity - d.Fuel);
            float afford = vendor.fuelPricePerUnit > 0f ? Eco.Credits / vendor.fuelPricePerUnit : 0f;
            float u = Mathf.Floor(Mathf.Min(units, room, afford));
            if (u < 1f) { _message = room < 1f ? "Rover tank is full." : "Not enough credits."; Refresh(); return; }
            int cost = Mathf.CeilToInt(u * vendor.fuelPricePerUnit);
            if (!Eco.Spend(cost, "refuel", vendor.Id, "rover.fuel", (int)u)) return;
            d.AddFuel(u);
            _message = $"Rover refuelled {u:0} units for {cost} cr.";
            Refresh();
        }

        void Refuel(float units)
        {
            var ship = Session.Exploration.Ship;
            float room = Mathf.Max(0f, ship.fuelCapacity - ship.fuel);
            float afford = vendor.fuelPricePerUnit > 0f ? Eco.Credits / vendor.fuelPricePerUnit : 0f;
            float u = Mathf.Floor(Mathf.Min(units, room, afford));
            if (u < 1f) { _message = room < 1f ? "Tank is full." : "Not enough credits."; Refresh(); return; }
            int cost = Mathf.CeilToInt(u * vendor.fuelPricePerUnit);
            if (!Eco.Spend(cost, "refuel", vendor.Id, "fuel", (int)u)) return;
            Session.Exploration.Refuel(u);
            _message = $"Refuelled {u:0} units for {cost} cr.";
            Refresh();
        }

        protected override void Build(VisualElement body)
        {
            if (vendor == null) { Text(body, "Vendor data missing."); return; }
            Status.text = $"{Eco.Credits:N0} cr   |   {Eco.RankName} ({Eco.Score} notoriety{(Eco.NextRankAt > 0 ? $", next rank at {Eco.NextRankAt}" : "")})";
            Hint.text = "E / Esc: close   |   Sells from your backpack";

            var sellCol = ScrollCol(body, 0, true);
            if (!string.IsNullOrEmpty(vendor.greeting)) Text(sellCol, vendor.greeting, "xa-term__dim");
            if (vendor.buys.Count > 0)
            {
                Text(sellCol, "SELL", "xa-term__h");
                var inv = Session.Inventory;
                var seen = new HashSet<string>(); int shown = 0;
                foreach (var st in inv.Slots)
                {
                    if (st.IsEmpty) continue;
                    int offer = OfferFor(st);
                    if (offer <= 0) continue;
                    if (string.IsNullOrEmpty(st.instanceId))
                    {
                        if (!seen.Add(st.itemId)) continue;
                        int have = inv.Count(st.itemId); var s = st;
                        var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; sellCol.Add(row);
                        Btn(row, $"{Name(st)} x{have}: {offer} cr each", () => Sell(s, 1)).style.flexGrow = 1;
                        if (have > 1) Btn(row, $"All ({offer * have} cr)", () => Sell(s, have));
                    }
                    else
                    {
                        var s = st;
                        var art = Session.Artifacts.Get(st.instanceId);
                        var gate = art != null ? Session.Artifacts.Check(art, ArtifactAction.Sell) : Eligibility.Allowed;
                        if (gate.IsBlocked) Text(sellCol, $"{Name(st)}: not for sale. {gate.Message}", "xa-term__dim");
                        else Btn(sellCol, $"{Name(st)}: {offer} cr" + (gate.IsWarning ? (_confirmInstance == st.instanceId ? "  (click again to confirm)" : "  (not fully researched)") : ""), () => Sell(s, 1));
                    }
                    shown++;
                }
                if (shown == 0) Text(sellCol, "Nothing in your backpack this vendor wants.", "xa-term__dim");
                Text(sellCol, $"Pays {Rate * 100f:0}% of value (rank bonus included).", "xa-term__dim");
            }

            var mid = ScrollCol(body, 1);
            if (vendor.stock.Count > 0)
            {
                Text(mid, "BUY", "xa-term__h");
                foreach (var s in vendor.stock)
                {
                    var d = Def(s.itemId); if (d == null) continue;
                    if (s.unique && Eco.Wallet.boughtUnique.Contains(vendor.Id + ":" + s.itemId)) continue;
                    if (Eco.Rank < s.minRank) { Text(mid, $"{d.DisplayNameOrId}: needs rank {Eco.RankNameOf(s.minRank)}", "xa-term__dim"); continue; }
                    var st = s;
                    Btn(mid, $"{d.DisplayNameOrId}: {s.price} cr" + (s.unique ? "  (one only)" : ""), () => Buy(st), Eco.CanAfford(s.price));
                }
            }
            if (vendor.upgrades.Count > 0)
            {
                Text(mid, "UPGRADES", "xa-term__h");
                foreach (var u in vendor.upgrades)
                {
                    if (string.IsNullOrEmpty(u.upgradeId)) continue;
                    int lvl = Session.UpgradeLevel(u.upgradeId), max = Session.UpgradeMax(u.upgradeId);
                    if (max <= 0) continue;
                    if (lvl >= max) { Text(mid, $"{u.label}: level {lvl + 1} (max)", "xa-term__dim"); continue; }
                    if (Eco.Rank < u.minRank) { Text(mid, $"{u.label}: needs rank {Eco.RankNameOf(u.minRank)}", "xa-term__dim"); continue; }
                    int price = UpgradePrice(u, lvl); var uu = u;
                    Btn(mid, $"{u.label} level {lvl + 1} -> {lvl + 2}: {price} cr", () => BuyUpgrade(uu), price > 0 && Eco.CanAfford(price));
                }
            }
            if (vendor.fuelPricePerUnit > 0f)
            {
                var ship = Session.Exploration.Ship;
                Text(mid, "SHIP FUEL", "xa-term__h");
                Text(mid, $"{ship.fuel:0.#} / {ship.fuelCapacity:0} units   ({vendor.fuelPricePerUnit:0.#} cr per unit)");
                Bar(mid, ship.fuel / Mathf.Max(1f, ship.fuelCapacity));
                Btn(mid, "Buy 10 units", () => Refuel(10f), ship.fuel < ship.fuelCapacity - 0.5f);
                Btn(mid, "Fill the tank", () => Refuel(ship.fuelCapacity), ship.fuel < ship.fuelCapacity - 0.5f);
                var rover = RoverDirector.Instance;
                if (rover != null)
                {
                    Text(mid, "ROVER FUEL", "xa-term__h");
                    Text(mid, $"{rover.Fuel:0.#} / {rover.FuelCapacity:0} units   ({vendor.fuelPricePerUnit:0.#} cr per unit)");
                    Bar(mid, rover.Fuel01);
                    Btn(mid, "Refuel rover: 10 units", () => RefuelRover(10f), rover.Fuel < rover.FuelCapacity - 0.5f);
                    Btn(mid, "Fill the rover", () => RefuelRover(rover.FuelCapacity), rover.Fuel < rover.FuelCapacity - 0.5f);
                }
            }
            if (!string.IsNullOrEmpty(_message)) Text(mid, _message);

            var log = ScrollCol(body, 2);
            Text(log, "RECENT DEALS", "xa-term__h");
            var tx = Eco.Wallet.transactions.Where(t => t.vendorId == vendor.Id).Reverse().Take(12).ToList();
            if (tx.Count == 0) Text(log, "None yet.", "xa-term__dim");
            foreach (var t in tx) Text(log, $"{t.kind} {t.count}x {Def(t.itemId)?.DisplayNameOrId ?? t.itemId}: {(t.credits >= 0 ? "+" : "")}{t.credits} cr", "xa-term__dim");
        }
    }
}
