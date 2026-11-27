using System;
using System.Collections.Generic;
using DMI.Xenoarch.Discovery;

namespace DMI.Xenoarch.Artifacts
{
    /// <summary>
    /// Phase 2 M1: the single gate every artifact transaction goes through (vendor sales, inventory bulk removal used by
    /// crafting/refining, ship storage and crate transfers, research, installation; later museum loans and donations,
    /// exchange listings and escrow, rover cargo). Services call it, so a UI or confirm dialog cannot bypass it.
    /// Rules, in order:
    /// <list type="number">
    /// <item>Moves between accessible places (backpack, ship crates, rover cargo, the lab) and research are always allowed.</item>
    /// <item>Disposal of something the player no longer has (sold / installed) is refused.</item>
    /// <item>An artifact in the running lab job cannot be disposed of.</item>
    /// <item>Story Critical and not released (see <see cref="ArtifactDefinition.releaseWhen"/>): blocked.</item>
    /// <item>Fragment-puzzle pieces while the puzzle is unsolved: a piece on the board is blocked; any other piece is blocked
    /// if losing it would leave fewer distinct owned piece types than the board has slots.</item>
    /// <item>Research Required with incomplete research: warning (allowed once the player confirms).</item>
    /// </list>
    /// Knowledge is never affected: facts, translations, civilizations and journal records stay when an object leaves.
    /// </summary>
    public sealed class ArtifactEligibility
    {
        readonly ArtifactService _artifacts;
        readonly ArtifactPuzzleService _puzzles;
        readonly DiscoveryService _discovery;

        /// <summary>True if the player can reach this instance (backpack, ship crates, items held for pickup at the lab,
        /// later rover cargo). Wired by GameSession.</summary>
        public Func<string, bool> Owns = _ => true;

        public ArtifactEligibility(ArtifactService artifacts, ArtifactPuzzleService puzzles, DiscoveryService discovery)
        {
            _artifacts = artifacts; _puzzles = puzzles; _discovery = discovery;
        }

        public static bool IsAccessibleMove(ArtifactAction a) =>
            a == ArtifactAction.MoveToShipStorage || a == ArtifactAction.MoveToBackpack || a == ArtifactAction.MoveToRoverCargo
            || a == ArtifactAction.ResearchOperation || a == ArtifactAction.Install;

        /// <summary>True if every release fact of the definition holds (an empty list counts as released).</summary>
        public bool RequirementsMet(ArtifactDefinition d)
        {
            if (d == null || d.releaseWhen == null) return true;
            foreach (var r in d.releaseWhen)
                if (r != null && !string.IsNullOrEmpty(r.factKey) && (_discovery == null || !_discovery.Has(r.factKey, r.threshold))) return false;
            return true;
        }

        /// <summary>Story Critical and still physically required.</summary>
        public bool IsStoryProtected(ArtifactDefinition d) => d != null && d.progression == ArtifactProgression.StoryCritical && !RequirementsMet(d);
        public bool IsStoryProtected(ArtifactInstance a) => IsStoryProtected(_artifacts?.Definition(a));

        /// <summary>A piece of a fragment puzzle that is not solved yet.</summary>
        public bool IsOpenPuzzlePiece(ArtifactInstance a)
        {
            string g = _puzzles?.GroupOf(a);
            return g != null && !_puzzles.IsSolved(g);
        }

        /// <summary>Anything recovery must keep in the player's possession (story critical or an open puzzle piece).</summary>
        public bool NeedsSafeguard(ArtifactInstance a) => a != null && (IsStoryProtected(a) || IsOpenPuzzlePiece(a));

        public Eligibility Check(ArtifactInstance a, ArtifactAction action)
        {
            if (a == null) return Eligibility.Allowed;      // not an artifact record: nothing to protect
            var d = _artifacts?.Definition(a);
            if (d == null) return Eligibility.Allowed;
            if (IsAccessibleMove(action)) return Eligibility.Allowed;
            string name = _artifacts.DisplayName(a);

            if (a.location == ArtifactLocation.Sold || a.location == ArtifactLocation.Installed)
                return Eligibility.Blocked($"{name} is no longer in your possession.");
            if (_artifacts.InJob(a.instanceId))
                return Eligibility.Blocked($"{name} is in the lab ({ResearchRules.Label(_artifacts.Job.op)}). Wait for it to finish or cancel the job first.");

            if (IsStoryProtected(d))
                return Eligibility.Blocked($"{name} is required for the expedition. " + (string.IsNullOrEmpty(d.progressionNote) ? "It cannot leave your possession yet." : d.progressionNote));

            string g = _puzzles?.GroupOf(a);
            if (g != null && !_puzzles.IsSolved(g))
            {
                var spec = ArtifactPuzzleService.Spec(g);
                string title = spec != null ? spec.title : g;
                if (_puzzles.SlotOf(g, a.instanceId) >= 0)
                    return Eligibility.Blocked($"{name} is on the {title} board. Placed pieces stay secured until the map is reconstructed.");
                int slots = spec != null ? spec.slots : 8;
                var distinct = new HashSet<string>();
                foreach (var p in _puzzles.Pieces(g)) if (p != null && p.instanceId != a.instanceId) distinct.Add(p.definitionId);
                if (distinct.Count < slots)
                    return Eligibility.Blocked($"{name} is needed for the {title}: it takes {slots} different pieces and you would have {distinct.Count}.");
            }

            if (d.progression == ArtifactProgression.ResearchRequired && !RequirementsMet(d))
                return Eligibility.Warning($"{name} has not been fully researched. " + (string.IsNullOrEmpty(d.progressionNote)
                    ? "Letting it go ends any chance to study it." : d.progressionNote) + " Knowledge you already recorded is kept.");

            return Eligibility.Allowed;
        }
    }
}
