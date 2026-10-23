using System;
using ProjectAbyss.Core.Persistence;

namespace ProjectAbyss.Core.Run
{
    /// <summary>
    /// Host-side gate for every critical change: validate → journal (fsync) → apply. Only after Commit returns
    /// Applied may the result be shown to players or sent to clients.
    /// </summary>
    public sealed class RunLedger
    {
        private readonly TransactionProcessor processor;
        private readonly TransactionJournal? journal;

        public RunLedger(RunState run, TransactionProcessor processor, TransactionJournal? journal)
        {
            this.Run = run;
            this.processor = processor;
            this.journal = journal;
        }

        public RunState Run { get; }

        /// <summary>Raised after a transaction was journaled and applied (e.g. to trigger a recovery snapshot).</summary>
        public event Action<RunTransaction>? Committed;

        public TxResult Commit(RunTransaction tx)
        {
            var validation = this.processor.Validate(this.Run, tx);
            if (!validation.Succeeded)
                return validation;
            long seq = this.journal?.Append(tx) ?? this.Run.JournalSequence + 1;
            var applied = this.processor.Apply(this.Run, tx);
            if (!applied.Succeeded)
                throw new InvalidOperationException($"Transaction {tx.TxId} passed validation but failed to apply: {applied.Reason}");
            this.Run.JournalSequence = seq;
            this.Committed?.Invoke(tx);
            return applied;
        }
    }
}
