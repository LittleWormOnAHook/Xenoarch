using System.Linq;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship research laboratory (Mk I): pick a recovered artifact, run one operation at a time (cleaning, identification,
    /// analyses, civilization id, restoration, translation, technology analysis). Results reveal the artifact's data;
    /// translation reveals text segments and feeds the discovery chains. Also shows the discovery journal.
    /// </summary>
    public sealed class ResearchTerminal : TerminalWindow
    {
        protected override string PanelId => "terminal.research";
        string _selected, _message;
        float _nextRefresh;

        protected override void OnOpened()
        {
            Session.Recovery.Ensure("lab"); // Phase 2 M1: controlled recovery path for mandatory items
            var a = Session.Artifacts;
            if (_selected == null || a.Get(_selected) == null) _selected = Held().FirstOrDefault()?.instanceId;
            a.ResearchCompleted += OnDone;
        }

        protected override void OnClosed() { if (Session != null) Session.Artifacts.ResearchCompleted -= OnDone; }

        void OnDone(ArtifactInstance a, ResearchOp op, string result) { _message = $"{ResearchRules.Label(op)} complete: {result}"; Refresh(); }

        System.Collections.Generic.IEnumerable<ArtifactInstance> Held()
        {
            var s = Session;
            return s.Artifacts.State.instances.Where(i => i.location == ArtifactLocation.Held && s.Inventory.ContainsInstance(i.instanceId));
        }

        protected override void Tick()
        {
            // Never rebuild mid-click (a rebuild between press and release drops the click) or under an open menu.
            if (Session.Artifacts.Busy && Time.unscaledTime >= _nextRefresh && !UIClickKit.Busy) { _nextRefresh = Time.unscaledTime + 0.5f; Refresh(); }
        }

        protected override void Build(VisualElement body)
        {
            var arts = Session.Artifacts;
            Status.text = arts.Busy ? "LAB BUSY" : "LAB IDLE";

            var finds = ScrollCol(body, 0);
            Text(finds, "FINDS", "xa-term__h");
            int n = 0;
            foreach (var inst in Held())
            {
                n++;
                string label = $"{(inst.instanceId == _selected ? "> " : "")}{arts.DisplayName(inst)}  ({inst.condition:P0})";
                Btn(finds, label, () => { _selected = inst.instanceId; _message = null; Refresh(); });
            }
            if (n == 0) Text(finds, "No artifacts aboard. Recover finds at a dig site (tools: 2-5, hold LMB to dig, hold E to recover).");

            var detail = ScrollCol(body, 1, true);
            var a = arts.Get(_selected);
            var d = arts.Definition(a);
            if (a == null || d == null) Text(detail, "Select a find.");
            else BuildDetail(detail, a, d);

            var lab = ScrollCol(body, 2);
            Text(lab, "LAB", "xa-term__h");
            var job = arts.Job;
            if (job != null)
            {
                var ji = arts.Get(job.instanceId);
                Text(lab, $"{ResearchRules.Label(job.op)}: {(ji != null ? arts.DisplayName(ji) : "?")}");
                Bar(lab, job.elapsed / Mathf.Max(0.01f, job.duration));
                Text(lab, $"{Mathf.Max(0f, job.duration - job.elapsed):0} s left");
                Btn(lab, "Cancel", () => { arts.Cancel(); Refresh(); }).AddToClassList("xa-term__btn--ghost");
            }
            else Text(lab, "Idle. One operation at a time (Mk I lab).");
            Text(lab, $"Lab reagents in cargo: {Session.Materials.ReagentUnits()} units (refine more at the refiner)", "xa-term__dim");
            var pending = Session.Recovery.State.pendingAtLab;
            if (pending.Count > 0)
            {
                Text(lab, "RECOVERED ITEMS (held by the lab)", "xa-term__h");
                foreach (var id in pending) { var pi = arts.Get(id); Text(lab, pi != null ? arts.DisplayName(pi) : id, "xa-term__dim"); }
                Btn(lab, "Collect (needs a free backpack or crate slot)", () =>
                {
                    int n = Session.Recovery.CollectPending();
                    _message = n > 0 ? $"Collected {n} item{(n == 1 ? "" : "s")}." : "No free slot in your backpack or the artifact crates.";
                    Refresh();
                });
            }
            if (!string.IsNullOrEmpty(_message)) Text(lab, _message);

            var installed = arts.State.instances.Where(i => i.location == ArtifactLocation.Installed).ToList();
            if (installed.Count > 0)
            {
                Text(lab, "INSTALLED TECH", "xa-term__h");
                foreach (var i in installed) { var idef = arts.Definition(i); Text(lab, $"{arts.DisplayName(i)}: {idef?.installTarget} +{idef?.installBonus}", "xa-term__dim"); }
            }

            Text(lab, "JOURNAL", "xa-term__h");
            var journal = Session.Discovery.State.journal;
            if (journal.Count == 0) Text(lab, "No entries yet.");
            for (int i = journal.Count - 1; i >= 0 && i >= journal.Count - 5; i--) Text(lab, journal[i].text);
        }

        void BuildDetail(VisualElement col, ArtifactInstance a, ArtifactDefinition d)
        {
            var arts = Session.Artifacts;
            Text(col, arts.DisplayName(a).ToUpperInvariant(), "xa-term__h");
            Text(col, a.Identified ? $"{d.artifactCategory}, {d.rarity}. {d.description}" : "Unidentified. Clean and identify it to learn more.");
            Text(col, $"Condition {a.condition:P0}");
            Bar(col, a.condition);
            if (a.Done(ResearchOp.MaterialAnalysis)) Text(col, $"Material: {d.material}");
            if (a.Done(ResearchOp.AgeEstimation)) Text(col, $"Age: {d.ageEstimate}");
            if (a.Done(ResearchOp.CivilizationIdentification))
            {
                var civ = arts.Civilization(d.civilizationId);
                Text(col, $"Origin: {(civ != null ? civ.displayName : "unknown")} (language knowledge {arts.Knowledge(d.civilizationId).language:P0})");
            }
            if (a.Done(ResearchOp.TechnologyAnalysis) && !string.IsNullOrEmpty(d.researchedDescription)) Text(col, d.researchedDescription);
            Text(col, $"Found at {a.siteId}. Est. value {arts.Value(a):0} cr");

            if (d.HasTranslation)
            {
                Text(col, $"Translation {a.translation:P0} (lab limit {arts.TranslationCap:P0})");
                Bar(col, a.translation);
                foreach (var seg in arts.RevealedText(a)) Text(col, $"\"{seg.text}\"");
            }

            if (d.installTarget != TechInstallTarget.None)
            {
                Text(col, "FUNCTIONAL DEVICE", "xa-term__h");
                string target = d.installTarget == TechInstallTarget.HandheldScanner ? "handheld scanner" : d.installTarget == TechInstallTarget.NavigationDrive ? "ship's navigation drive" : "orbital scanner";
                bool can = arts.CanInstall(a, out var why);
                Btn(col, $"Install in the {target} (+{d.installBonus} tier{(d.installBonus == 1 ? "" : "s")}; the device is used up)" + (can ? "" : $"\n{why}"), () =>
                {
                    if (arts.Install(a, out var r)) { Session.Inventory.RemoveInstance(a.instanceId); _message = $"Installed {arts.DisplayName(a)} in the {target}." + (d.installTarget == TechInstallTarget.NavigationDrive ? " Interstellar jumps can now be plotted at the Nav terminal." : ""); Toast.Show(_message, 6f); _selected = null; }
                    else _message = r;
                    Refresh();
                }, can);
            }

            Text(col, "OPERATIONS", "xa-term__h");
            var ops = d.researchOps.ToList();
            if (d.HasTranslation && !ops.Contains(ResearchOp.Translation)) ops.Add(ResearchOp.Translation);
            foreach (var op in ops)
            {
                bool done = a.Done(op) && !ResearchRules.Repeatable(op);
                if (done) { Text(col, $"Done: {ResearchRules.Label(op)}"); continue; }
                bool can = arts.CanStart(a, op, out var why);
                int cost = ResearchRules.ReagentCost(op);
                string label = $"{ResearchRules.Label(op)} ({ResearchRules.Duration(op):0} s, {cost} reagent{(cost == 1 ? "" : "s")})" + (can ? "" : $"\n{why}");
                Btn(col, label, () => { if (!arts.Start(a, op, out var r)) _message = r; else _message = null; Refresh(); }, can);
            }
        }
    }
}
