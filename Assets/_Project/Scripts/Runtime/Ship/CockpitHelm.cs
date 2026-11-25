using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Cockpit console (P_Cockpit_Tech_02). Seated in a cockpit chair, press E on it for the ship console: engage the
    /// drive to the course plotted on the navigation terminal (plays <see cref="ShipFtlSequence"/> through the front
    /// window, no scene reload), open the nav map, ship status / orbital scanner, stand up. Standing, E takes the nearest
    /// free cockpit seat and opens the console once seated.
    /// </summary>
    public sealed class CockpitHelm : TerminalWindow
    {
        [Tooltip("Optional: a point outside the front window the jump effect plays toward. Empty = windowDirection.")]
        public Transform viewTarget;
        [Tooltip("World direction out of the front window (set by Setup/9 from the ship's nose).")]
        public Vector3 windowDirection = Vector3.forward;

        ShipFtlSequence _seq;
        string _message;
        protected override string PanelId => "terminal.cockpit_console";
        static ExplorationService X => XenoarchRuntime.Session?.Exploration;
        public bool Busy => _seq != null && _seq.Running;

        void Reset() => terminalName = "Ship Console";
        void Awake() { if (string.IsNullOrEmpty(terminalName) || terminalName == "Terminal") terminalName = "Ship Console"; }

        public override float GetPriority(in InteractionContext c) => X != null && !Busy && !IsOpen && (CockpitSeat.Occupied == null || !CockpitSeat.Occupied.Transitioning) ? 1f : -1f;

        public override string GetPrompt(in InteractionContext c)
        {
            if (CockpitSeat.Occupied != null) return "Ship console";
            return NearestFreeSeat(c.Rig.Root.position) != null ? "Take the helm" : "Ship console";
        }

        public override bool TryInteract(in InteractionContext c)
        {
            if (Busy) return false;
            if (CockpitSeat.Occupied == null && c.Rig is FirstPersonController fpc)
            {
                var seat = NearestFreeSeat(fpc.Root.position);
                if (seat != null) return seat.Sit(fpc, Open);
            }
            Open();
            return IsOpen;
        }

        CockpitSeat NearestFreeSeat(Vector3 from)
        {
            CockpitSeat best = null; float bd = 36f; // within 6 m
            foreach (var s in FindObjectsByType<CockpitSeat>(FindObjectsSortMode.None))
            {
                if (!s.isActiveAndEnabled || s.IsOccupied) continue;
                float d = (s.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        protected override void OnOpened() => _message = null;

        protected override void Build(VisualElement body)
        {
            var x = Session.Exploration;
            var to = x.PlottedBody;
            Status.text = $"{ShipTravel.LocationText(x)}   |   Fuel {x.Ship.fuel:0.#} / {x.Ship.fuelCapacity:0}" + (to != null ? $"   |   Course: {to.displayName}" : "");

            // --- helm
            var helm = Col(body);
            Text(helm, "HELM", "xa-term__h");
            if (to == null)
            {
                Text(helm, "No course plotted.", "xa-term__dim");
                Text(helm, "Plot a destination on the navigation map first.", "xa-term__dim");
            }
            else
            {
                Text(helm, $"Course: {to.displayName}");
                Text(helm, $"Distance {x.DistanceAU(x.CurrentBody, to):0.##} AU   |   Fuel cost {x.FuelCost(to):0.#}");
                bool can = x.CanTravel(to, out var why);
                if (!can) Text(helm, why, "xa-term__warn");
                Btn(helm, $"Engage drive: {to.displayName}", EngageClick, can);
                Btn(helm, "Clear course", () => { x.ClearCourse(); _message = "Course cleared."; Refresh(); }).AddToClassList("xa-term__btn--ghost");
            }
            Btn(helm, "Navigation map", () => SwitchTo<NavigationTerminal>("navigation terminal"));

            // --- ship status
            var stat = Col(body);
            Text(stat, "SHIP STATUS", "xa-term__h");
            Text(stat, ShipTravel.LocationText(x));
            Text(stat, $"Fuel {x.Ship.fuel:0.#} / {x.Ship.fuelCapacity:0}");
            Bar(stat, x.Ship.fuelCapacity > 0f ? x.Ship.fuel / x.Ship.fuelCapacity : 0f);
            Text(stat, $"Scanner Mk {x.Ship.scannerLevel}   |   Module slots {x.Ship.modules.Count} / {x.Ship.moduleSlots}   |   Cargo slots {x.Ship.cargoSlots}", "xa-term__dim");
            var here = x.CurrentBody;
            if (here != null)
            {
                var bd = x.Body(here.Id);
                Text(stat, $"Orbital scan passes on {here.displayName}: {bd.orbitalScanPasses} / {x.MaxOrbitalPasses}", "xa-term__dim");
            }
            Btn(stat, "Orbital scanner", () => SwitchTo<OrbitalScannerTerminal>("orbital scanner"));

            // --- crew
            var crew = Col(body);
            Text(crew, "PILOT", "xa-term__h");
            Text(crew, CockpitSeat.Occupied != null ? "Seated at the helm." : "Standing.", "xa-term__dim");
            if (CockpitSeat.Occupied != null) Btn(crew, "Stand up", () => { Close(); CockpitSeat.Occupied?.Stand(); }).AddToClassList("xa-term__btn--ghost");
            if (!string.IsNullOrEmpty(_message)) Text(crew, _message, "xa-term__good");
        }

        void SwitchTo<T>(string label) where T : TerminalWindow
        {
            var t = FindAnyObjectByType<T>();
            if (t == null) { _message = $"No {label} aboard."; Refresh(); return; }
            Close();
            t.Open();
        }

        /// <summary>Fly the plotted course now (console button; also a tooling hook).</summary>
        public bool Engage()
        {
            var x = X; var to = x?.PlottedBody;
            var rig = PlayerRigLocator.Find();
            if (to == null || Busy || rig == null || !x.CanTravel(to, out _)) return false;
            Close();
            if (_seq == null) _seq = gameObject.AddComponent<ShipFtlSequence>();
            Vector3 dir = viewTarget != null ? viewTarget.position - rig.ViewCamera.transform.position : windowDirection;
            return _seq.Play(rig, to, dir);
        }

        void EngageClick() => Engage();
    }
}
