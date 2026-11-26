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
        [Tooltip("Optional: where the hand presses on the console. Empty = the console surface point in front of the camera.")]
        public Transform pressPoint;

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
            var helm = ScrollCol(body, 0);
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
                if (can && ShipLanding.Running) { can = false; why = "Finish the landing manoeuvre first."; }
                if (!can) Text(helm, why, "xa-term__warn");
                Btn(helm, $"Engage drive: {to.displayName}", () => Press(() => Engage()), can);
                Btn(helm, "Clear course", () => { x.ClearCourse(); _message = "Course cleared."; Refresh(); }).AddToClassList("xa-term__btn--ghost");
            }
            Btn(helm, "Navigation map", () => SwitchTo<NavigationTerminal>("navigation terminal"));
            // Same scanner as the standalone Orbital Scanning terminal (which stays aboard); this is just another route to it.
            bool hasScanner = FindAnyObjectByType<OrbitalScannerTerminal>() != null;
            Btn(helm, "Orbital scanner", () => SwitchTo<OrbitalScannerTerminal>("orbital scanner"), hasScanner);
            if (!hasScanner) Text(helm, "No orbital scanner terminal aboard.", "xa-term__dim");

            // --- ship status
            var stat = ScrollCol(body, 1);
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

            // --- crew
            var crew = ScrollCol(body, 2);
            Text(crew, "PILOT", "xa-term__h");
            Text(crew, CockpitSeat.Occupied != null ? "Seated at the helm." : "Standing.", "xa-term__dim");
            if (CockpitSeat.Occupied != null) Btn(crew, "Stand up", () => { Close(); CockpitSeat.Occupied?.Stand(); }).AddToClassList("xa-term__btn--ghost");
            if (!string.IsNullOrEmpty(_message)) Text(crew, _message, "xa-term__good");
        }

        void SwitchTo<T>(string label) where T : TerminalWindow
        {
            var t = FindAnyObjectByType<T>();
            if (t == null) { _message = $"No {label} aboard."; Refresh(); return; }
            Press(t.Open);
        }

        /// <summary>Close the console, reach the right hand to the console and press, then run the command.
        /// Standing (or no arms rig): the command runs right away.</summary>
        void Press(System.Action command)
        {
            Close();
            var rig = PlayerRigLocator.Find();
            var arms = rig != null ? rig.Root.GetComponentInChildren<FirstPersonArms>() : null;
            if (arms == null || CockpitSeat.Occupied == null) { command(); return; }
            arms.Reach(PressPointFor(rig.ViewCamera.transform.position), command);
        }

        /// <summary>Press point: <see cref="pressPoint"/>, else where the camera ray toward the console hits it, else the
        /// nearest point on its bounds.</summary>
        public Vector3 PressPointFor(Vector3 eye)
        {
            if (pressPoint != null) return pressPoint.position;
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return transform.position;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var aim = b.ClosestPoint(eye) * 0.5f + b.center * 0.5f;
            if (Physics.Raycast(eye, aim - eye, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(transform))
                return hit.point;
            return b.ClosestPoint(eye);
        }

        /// <summary>Fly the plotted course now (console button; also a tooling hook).</summary>
        public bool Engage()
        {
            var x = X; var to = x?.PlottedBody;
            var rig = PlayerRigLocator.Find();
            if (to == null || Busy || ShipLanding.Running || rig == null || !x.CanTravel(to, out _)) return false;
            Close();
            if (_seq == null) _seq = GetComponent<ShipFtlSequence>();
            if (_seq == null) _seq = gameObject.AddComponent<ShipFtlSequence>();
            Vector3 dir = viewTarget != null ? viewTarget.position - rig.ViewCamera.transform.position : windowDirection;
            return _seq.Play(rig, to, dir);
        }

    }
}
