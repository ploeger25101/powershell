using System.Collections.Generic;
using ProjectAbyss.Core.Items;

namespace ProjectAbyss.Core.Run
{
    public enum TxKind
    {
        /// <summary>Gold + XP (+ optional item) for one participant from one enemy kill. Id is keyed by arena attempt and entity id.</summary>
        KillReward,
        GrantGold,
        SpendGold,
        GrantItem,
        SellItem,
        TradeItem,
        MoveToChest,
        TakeFromChest,
        ClaimPendingReward,
        GrantMaterial,
        LoseLife,
        /// <summary>Shop revival of an eliminated player, financed by voluntary, confirmed contributions.</summary>
        ShopRevive,
        PactVote,
        PactConsume,
        BossMilestone,
        AdvanceLevel,
        EndRun,
    }

    /// <summary>
    /// A host-created, journaled change to the run. Applying the same transaction twice is a no-op (idempotent by
    /// <see cref="TxId"/>), which is what makes crash recovery and network retries duplication-free.
    /// </summary>
    public sealed class RunTransaction
    {
        public string TxId { get; set; } = "";
        public TxKind Kind { get; set; }
        public string ParticipantId { get; set; } = "";
        public string? OtherParticipantId { get; set; }
        public long Amount { get; set; }
        public int Xp { get; set; }
        public string? ItemId { get; set; }
        public ItemInstance? Item { get; set; }
        public string? Key { get; set; }
        public bool Flag { get; set; }
        public Dictionary<string, long>? Contributions { get; set; }

        public static string KillTxId(string runId, int attempt, int entityId, string participantId) => $"kill:{runId}:{attempt}:{entityId}:{participantId}";
    }

    public enum TxOutcome
    {
        Applied,
        Duplicate,
        Rejected,
    }

    public readonly record struct TxResult(TxOutcome Outcome, string? Reason = null)
    {
        public static readonly TxResult Ok = new(TxOutcome.Applied);
        public static readonly TxResult Dup = new(TxOutcome.Duplicate);
        public static TxResult Reject(string reason) => new(TxOutcome.Rejected, reason);
        public bool Succeeded => this.Outcome == TxOutcome.Applied;
    }
}
