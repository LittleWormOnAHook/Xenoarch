using System;
using System.Collections.Generic;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Economy
{
    [Serializable] public sealed class Transaction { public string kind; public string vendorId; public string itemId; public string instanceId; public int count; public int credits; public float time; }
    [Serializable] public sealed class WalletData { public int credits; public List<Transaction> transactions = new List<Transaction>(); public List<string> boughtUnique = new List<string>(); }
    [Serializable] public sealed class NotorietyData { public int score; public List<string> awarded = new List<string>(); }

    /// <summary>
    /// Phase 1D: the player's credits (save key player.wallet, with a transaction log) and notoriety (progression.notoriety,
    /// score + rank from <see cref="EconomySettings"/>; one-off awards are keyed so they never count twice).
    /// Saves from before 1D load with the starting credits and rank 0.
    /// </summary>
    public sealed class EconomyService
    {
        public const string WalletKey = "player.wallet", NotorietyKey = "progression.notoriety";
        const int MaxLog = 200;
        public WalletData Wallet { get; private set; } = new WalletData();
        public NotorietyData Notoriety { get; private set; } = new NotorietyData();
        public EconomySettings Settings { get; }
        public Func<float> Clock = () => 0f;
        public event Action Changed;
        /// <summary>(new rank index, rank name).</summary>
        public event Action<int, string> RankUp;

        public EconomyService(EconomySettings settings, SaveService saves)
        {
            Settings = settings ?? new EconomySettings();
            Wallet.credits = Settings.startingCredits;
            saves?.Register(new JsonStateSection<WalletData>(WalletKey, 20, () => Wallet, v => { Wallet = v ?? NewWallet(); Changed?.Invoke(); }, NewWallet));
            saves?.Register(new JsonStateSection<NotorietyData>(NotorietyKey, 20, () => Notoriety, v => { Notoriety = v ?? new NotorietyData(); Changed?.Invoke(); }, () => new NotorietyData()));
        }

        WalletData NewWallet() => new WalletData { credits = Settings.startingCredits };

        public int Credits => Wallet.credits;
        public bool CanAfford(int price) => price <= Wallet.credits;

        public bool Spend(int amount, string kind, string vendorId = null, string itemId = null, int count = 1)
        {
            if (amount < 0 || amount > Wallet.credits) return false;
            Wallet.credits -= amount;
            Log(kind, vendorId, itemId, null, count, -amount);
            return true;
        }

        public void Earn(int amount, string kind, string vendorId = null, string itemId = null, string instanceId = null, int count = 1)
        {
            if (amount <= 0) return;
            Wallet.credits += amount;
            Log(kind, vendorId, itemId, instanceId, count, amount);
        }

        void Log(string kind, string vendorId, string itemId, string instanceId, int count, int credits)
        {
            Wallet.transactions.Add(new Transaction { kind = kind, vendorId = vendorId, itemId = itemId, instanceId = instanceId, count = count, credits = credits, time = Clock() });
            if (Wallet.transactions.Count > MaxLog) Wallet.transactions.RemoveRange(0, Wallet.transactions.Count - MaxLog);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ notoriety
        public int Score => Notoriety.score;
        public int Rank => RankFor(Notoriety.score);
        public string RankName => RankNameOf(Rank);
        public string RankNameOf(int r) => Settings.rankNames != null && r >= 0 && r < Settings.rankNames.Length ? Settings.rankNames[r] : $"Rank {r}";
        public int RankFor(int score)
        {
            var t = Settings.rankThresholds; int r = 0;
            if (t == null) return 0;
            for (int i = 0; i < t.Length; i++) if (score >= t[i]) r = i;
            return r;
        }
        /// <summary>Score needed for the next rank, or -1 at the top.</summary>
        public int NextRankAt => Settings.rankThresholds != null && Rank + 1 < Settings.rankThresholds.Length ? Settings.rankThresholds[Rank + 1] : -1;

        /// <summary>Adds points. With a key, the award is one-off (a repeat of the same key is ignored).</summary>
        public bool Award(int points, string key = null, string reason = null)
        {
            if (points <= 0) return false;
            if (!string.IsNullOrEmpty(key)) { if (Notoriety.awarded.Contains(key)) return false; Notoriety.awarded.Add(key); }
            int before = Rank;
            Notoriety.score += points;
            if (!string.IsNullOrEmpty(reason)) Debug.Log($"[Xenoarch] Notoriety +{points} ({reason}) = {Notoriety.score}");
            Changed?.Invoke();
            int after = Rank;
            if (after > before) RankUp?.Invoke(after, RankNameOf(after));
            return true;
        }
    }
}
