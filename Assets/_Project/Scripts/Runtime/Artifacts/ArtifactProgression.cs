using System;
using UnityEngine;

namespace DMI.Xenoarch.Artifacts
{
    /// <summary>
    /// Phase 2 M1: how an artifact relates to progression. Separate from <see cref="ArtifactRarity"/> (a Unique find can be
    /// Ordinary; a Common one could be Story Critical). Append-only: values are stored as numbers in assets.
    /// </summary>
    public enum ArtifactProgression
    {
        /// <summary>No progression restrictions: normal trading, research, preservation and ownership rules.</summary>
        Ordinary,
        /// <summary>Meaningful research value but not physically needed by the main story: may be sold, with a warning while
        /// its research (see <see cref="ArtifactDefinition.releaseWhen"/>) is incomplete.</summary>
        ResearchRequired,
        /// <summary>Physically required by mandatory progression: cannot leave the player's accessible possession (sell,
        /// discard, destroy, dismantle, donate, loan, exchange, inaccessible storage) until every
        /// <see cref="ArtifactDefinition.releaseWhen"/> fact holds.</summary>
        StoryCritical,
    }

    /// <summary>A discovery fact and the value it must reach (facts keep their maximum, so once met it stays met).</summary>
    [Serializable]
    public sealed class FactRequirement
    {
        public string factKey;
        [Min(0f)] public float threshold = 1f;
    }

    /// <summary>Everything that can happen to an artifact instance. Append-only. Future milestones route their paths
    /// (museum loans/donations, exchange listings/escrow, rover cargo) through <see cref="ArtifactEligibility.Check"/>.</summary>
    public enum ArtifactAction
    {
        Sell,
        Discard,
        Destroy,
        Dismantle,
        Donate,
        Loan,
        ExchangeList,
        ExchangeEscrow,
        MoveToInaccessibleStorage,
        MoveToShipStorage,
        MoveToBackpack,
        MoveToRoverCargo,
        ResearchOperation,
        Install,
    }

    public enum EligibilityResult { Allowed, Warning, Blocked }

    /// <summary>Result of <see cref="ArtifactEligibility.Check"/>: Allowed, Warning (allowed after the player confirms the
    /// explanation) or Blocked (with the reason to show).</summary>
    public readonly struct Eligibility
    {
        public readonly EligibilityResult Result;
        public readonly string Message;
        Eligibility(EligibilityResult r, string m) { Result = r; Message = m; }
        public static readonly Eligibility Allowed = new Eligibility(EligibilityResult.Allowed, null);
        public static Eligibility Warning(string explanation) => new Eligibility(EligibilityResult.Warning, explanation);
        public static Eligibility Blocked(string reason) => new Eligibility(EligibilityResult.Blocked, reason);
        public bool IsBlocked => Result == EligibilityResult.Blocked;
        public bool IsWarning => Result == EligibilityResult.Warning;
        public override string ToString() => Message == null ? Result.ToString() : $"{Result}: {Message}";
    }
}
