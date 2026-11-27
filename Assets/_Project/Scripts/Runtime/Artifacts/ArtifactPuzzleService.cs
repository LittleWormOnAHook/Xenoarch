using System;
using System.Collections.Generic;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Artifacts
{
    [Serializable]
    public sealed class PuzzleBoardState
    {
        public string groupId;
        [Tooltip("Artifact instance id per slot (empty = free).")] public List<string> slots = new List<string>();
        public bool solved;
        public float solvedAt = -1f;
    }

    [Serializable] public sealed class PuzzleState { public List<PuzzleBoardState> boards = new List<PuzzleBoardState>(); }

    /// <summary>Static description of a fragment puzzle: which fragment group feeds it, its slot count and its reward.</summary>
    public sealed class PuzzleSpec
    {
        public string groupId, title, unlockSystemId, revealTitle, revealText;
        public int slots = 8;
    }

    /// <summary>
    /// Fragment puzzles: artifacts whose <see cref="ArtifactDefinition.fragmentOf"/> matches a puzzle's group are its
    /// pieces. The player lays distinct pieces into the board's slots (the artifacts stay in the inventory; the board
    /// only references them). Filling every slot solves it once: publishes <c>puzzle.solved:&lt;group&gt;</c>, the Nav
    /// unlock fact for the reward system and a journal record. Board state is saved under its own key.
    /// </summary>
    public sealed class ArtifactPuzzleService
    {
        public const string Key = "artifacts.puzzles";
        public const string StarMapGroup = "proxima_star_map";

        /// <summary>Built-in puzzles. DLC can add more with <see cref="Register"/>.</summary>
        static readonly List<PuzzleSpec> _specs = new List<PuzzleSpec>
        {
            new PuzzleSpec
            {
                groupId = StarMapGroup, slots = 8, title = "Vael star map", unlockSystemId = "sys.proxima_centauri",
                revealTitle = "PROXIMA CENTAURI",
                revealText = "RA 14h 29m 43s   Dec -62\u00B0 40' 46\"   Distance 4.25 ly",
            },
        };

        public static void Register(PuzzleSpec spec) { if (spec != null && !string.IsNullOrEmpty(spec.groupId) && Spec(spec.groupId) == null) _specs.Add(spec); }
        public static PuzzleSpec Spec(string groupId) { foreach (var s in _specs) if (s.groupId == groupId) return s; return null; }
        public static string SolvedFact(string groupId) => "puzzle.solved:" + groupId;

        readonly ArtifactService _artifacts;
        readonly DiscoveryService _discovery;
        public PuzzleState State { get; private set; } = new PuzzleState();
        /// <summary>True if the player still has this artifact instance (backpack or a ship crate). Wired by GameSession.</summary>
        public Func<string, bool> Owns = _ => true;
        public Func<float> Clock = () => 0f;
        public event Action Changed;
        public event Action<PuzzleSpec> Solved;

        public ArtifactPuzzleService(ArtifactService artifacts, DiscoveryService discovery, SaveService saves)
        {
            _artifacts = artifacts; _discovery = discovery;
            saves?.Register(new JsonStateSection<PuzzleState>(Key, 16, () => State, s => { State = s ?? new PuzzleState(); Changed?.Invoke(); }));
        }

        public bool IsPiece(ArtifactInstance a, string groupId)
        {
            var d = _artifacts?.Definition(a);
            return d != null && !string.IsNullOrEmpty(groupId) && d.fragmentOf == groupId;
        }

        /// <summary>Puzzle group of an artifact, or null if it is not a piece of a known puzzle.</summary>
        public string GroupOf(ArtifactInstance a)
        {
            var d = _artifacts?.Definition(a);
            return d != null && !string.IsNullOrEmpty(d.fragmentOf) && Spec(d.fragmentOf) != null ? d.fragmentOf : null;
        }

        /// <summary>Pieces of the group the player currently owns.</summary>
        public List<ArtifactInstance> Pieces(string groupId)
        {
            var list = new List<ArtifactInstance>();
            if (_artifacts == null) return list;
            foreach (var a in _artifacts.State.instances)
                if (a != null && IsPiece(a, groupId) && Owns(a.instanceId)) list.Add(a);
            return list;
        }

        /// <summary>Combining needs at least two pieces discovered (or a board already started).</summary>
        public bool CanCombine(string groupId) => Pieces(groupId).Count >= 2 || Filled(groupId) > 0;

        public PuzzleBoardState Board(string groupId)
        {
            var spec = Spec(groupId);
            PuzzleBoardState b = null;
            foreach (var x in State.boards) if (x != null && x.groupId == groupId) { b = x; break; }
            if (b == null) { b = new PuzzleBoardState { groupId = groupId }; State.boards.Add(b); }
            int n = spec != null ? spec.slots : 8;
            while (b.slots.Count < n) b.slots.Add("");
            if (!b.solved) // Phase 2 M1: only drop pieces whose record is gone or that were really sold/installed. A placed piece
                           // that is temporarily missing (failed transfer, interrupted load) keeps its slot; recovery restores it.
                for (int i = 0; i < b.slots.Count; i++)
                {
                    if (string.IsNullOrEmpty(b.slots[i])) continue;
                    var a = _artifacts?.Get(b.slots[i]);
                    if (a == null || a.location == ArtifactLocation.Sold || a.location == ArtifactLocation.Installed) b.slots[i] = "";
                }
            return b;
        }

        public int Filled(string groupId) { int n = 0; foreach (var s in Board(groupId).slots) if (!string.IsNullOrEmpty(s)) n++; return n; }
        public bool IsSolved(string groupId) => Board(groupId).solved;
        public int SlotOf(string groupId, string instanceId) => string.IsNullOrEmpty(instanceId) ? -1 : Board(groupId).slots.IndexOf(instanceId);

        /// <summary>Places a piece in a slot (moves it if already on the board). Pieces must be distinct artifact types.</summary>
        public bool Place(string groupId, int slot, string instanceId, out string reason)
        {
            reason = null;
            var b = Board(groupId);
            if (b.solved) { reason = "Already reconstructed."; return false; }
            if (slot < 0 || slot >= b.slots.Count) { reason = "No such slot."; return false; }
            var a = _artifacts?.Get(instanceId);
            if (a == null || !IsPiece(a, groupId) || !Owns(instanceId)) { reason = "That isn't a piece of this puzzle."; return false; }
            for (int i = 0; i < b.slots.Count; i++)
            {
                if (i == slot || string.IsNullOrEmpty(b.slots[i])) continue;
                if (b.slots[i] == instanceId) { b.slots[i] = ""; continue; }
                var other = _artifacts.Get(b.slots[i]);
                if (other != null && other.definitionId == a.definitionId) { reason = "A piece of that type is already on the board."; return false; }
            }
            b.slots[slot] = instanceId;
            Changed?.Invoke();
            if (Filled(groupId) >= b.slots.Count)
            {
                foreach (var id in b.slots)
                    if (!Owns(id)) { reason = "A placed piece is missing from your inventory; it is being recovered. Try again in a moment."; return true; }
                Solve(b);
            }
            return true;
        }

        /// <summary>Phase 2 M1: solves a full board whose solve was deferred because a placed piece was being recovered.</summary>
        public bool RecheckSolved(string groupId)
        {
            var b = Board(groupId);
            if (b.solved || Filled(groupId) < b.slots.Count) return false;
            foreach (var id in b.slots) if (!Owns(id)) return false;
            Solve(b);
            return true;
        }

        /// <summary>Ids of every registered puzzle group.</summary>
        public static IEnumerable<string> Groups() { foreach (var s in _specs) yield return s.groupId; }

        public void Remove(string groupId, int slot)
        {
            var b = Board(groupId);
            if (b.solved || slot < 0 || slot >= b.slots.Count || string.IsNullOrEmpty(b.slots[slot])) return;
            b.slots[slot] = "";
            Changed?.Invoke();
        }

        void Solve(PuzzleBoardState b)
        {
            if (b.solved) return;
            b.solved = true; b.solvedAt = Clock();
            var spec = Spec(b.groupId);
            if (_discovery != null)
            {
                _discovery.Publish(SolvedFact(b.groupId));
                if (spec != null && !string.IsNullOrEmpty(spec.unlockSystemId)) _discovery.Publish(Facts.NavSystem(spec.unlockSystemId));
                _discovery.Record("puzzle", b.groupId, spec != null
                    ? $"{spec.title} reconstructed: coordinates for {spec.revealTitle} ({spec.revealText}). New Nav destination unlocked."
                    : $"Puzzle {b.groupId} reconstructed.");
            }
            Changed?.Invoke();
            if (spec != null) Solved?.Invoke(spec);
        }
    }
}
