using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Items;

namespace ProjectAbyss.Core.Run
{
    /// <summary>
    /// Validates and applies run transactions. Validation and application are separated so the host can reject
    /// a request before anything is written to the journal; application of a validated transaction cannot fail.
    /// </summary>
    public sealed class TransactionProcessor
    {
        private readonly GameData data;

        public TransactionProcessor(GameData data)
        {
            this.data = data;
        }

        public TxResult Validate(RunState run, RunTransaction tx)
        {
            if (string.IsNullOrWhiteSpace(tx.TxId))
                return TxResult.Reject("missing transaction id");
            if (run.AppliedTransactions.Contains(tx.TxId))
                return TxResult.Dup;
            if (run.Status != RunStatus.Active && tx.Kind != TxKind.EndRun)
                return TxResult.Reject("run is over");
            var p = run.Find(tx.ParticipantId);
            if (p == null && tx.Kind is not (TxKind.AdvanceLevel or TxKind.EndRun or TxKind.PactConsume or TxKind.BossMilestone))
                return TxResult.Reject("unknown participant");
            if (tx.Amount < 0)
                return TxResult.Reject("negative amount");

            switch (tx.Kind)
            {
                case TxKind.KillReward:
                case TxKind.GrantGold:
                case TxKind.GrantMaterial:
                    return tx.Item != null ? this.ValidateNewItem(run, tx.Item) : TxResult.Ok;
                case TxKind.GrantItem:
                    return tx.Item == null ? TxResult.Reject("no item") : this.ValidateNewItem(run, tx.Item);
                case TxKind.SpendGold:
                    return p!.Gold >= tx.Amount ? TxResult.Ok : TxResult.Reject("not enough gold");
                case TxKind.SellItem:
                    if (tx.ItemId == null || !p!.Inventory.Contains(tx.ItemId))
                        return TxResult.Reject("item not in inventory");
                    return TxResult.Ok;
                case TxKind.TradeItem:
                {
                    var other = tx.OtherParticipantId == null ? null : run.Find(tx.OtherParticipantId);
                    if (other == null || other == p)
                        return TxResult.Reject("invalid trade partner");
                    if (tx.ItemId == null || !p!.Inventory.Contains(tx.ItemId))
                        return TxResult.Reject("item not in inventory");
                    if (other.Inventory.FreeSlots == 0)
                        return TxResult.Reject("partner inventory full");
                    return TxResult.Ok;
                }
                case TxKind.MoveToChest:
                    if (tx.ItemId == null || !p!.Inventory.Contains(tx.ItemId))
                        return TxResult.Reject("item not in inventory");
                    return p.RunChest.FreeSlots > 0 ? TxResult.Ok : TxResult.Reject("run chest full");
                case TxKind.TakeFromChest:
                    if (tx.ItemId == null || !p!.RunChest.Contains(tx.ItemId))
                        return TxResult.Reject("item not in run chest");
                    return p.Inventory.FreeSlots > 0 ? TxResult.Ok : TxResult.Reject("inventory full");
                case TxKind.ClaimPendingReward:
                    if (!p!.PendingRewards.Any(i => i.InstanceId == tx.ItemId))
                        return TxResult.Reject("no such pending reward");
                    return p.Inventory.FreeSlots > 0 ? TxResult.Ok : TxResult.Reject("inventory full");
                case TxKind.LoseLife:
                    return p!.Lives > 0 ? TxResult.Ok : TxResult.Reject("no lives left");
                case TxKind.ShopRevive:
                {
                    if (!p!.Eliminated)
                        return TxResult.Reject("player is not eliminated");
                    if (tx.Contributions == null || tx.Contributions.Values.Sum() < tx.Amount)
                        return TxResult.Reject("contributions do not cover the cost");
                    foreach (var c in tx.Contributions)
                    {
                        var payer = run.Find(c.Key);
                        if (payer == null || c.Value < 0 || payer.Gold < c.Value)
                            return TxResult.Reject($"invalid contribution from {c.Key}");
                    }
                    return TxResult.Ok;
                }
                case TxKind.PactVote:
                    if (!run.Pact.VoteOpen || run.Pact.Used)
                        return TxResult.Reject("no pact vote open");
                    return run.OriginalParticipants.Contains(tx.ParticipantId) ? TxResult.Ok : TxResult.Reject("not an original participant");
                case TxKind.PactConsume:
                    if (run.Pact.Used)
                        return TxResult.Reject("pact already used");
                    if (!run.Pact.VoteOpen || run.OriginalParticipants.Any(id => !run.Pact.Votes.TryGetValue(id, out bool yes) || !yes))
                        return TxResult.Reject("pact requires unanimous consent of all original participants");
                    return TxResult.Ok;
                default:
                    return TxResult.Ok;
            }
        }

        private TxResult ValidateNewItem(RunState run, ItemInstance item)
        {
            if (!this.data.Items.ContainsKey(item.DefId))
                return TxResult.Reject($"unknown item '{item.DefId}'");
            if (string.IsNullOrEmpty(item.InstanceId) || this.ItemExists(run, item.InstanceId))
                return TxResult.Reject("item instance id missing or already in use");
            return TxResult.Ok;
        }

        public bool ItemExists(RunState run, string instanceId) =>
            run.Participants.Any(p => p.Inventory.Contains(instanceId) || p.RunChest.Contains(instanceId)
                || p.Equipment.Values.Any(i => i.InstanceId == instanceId) || p.PendingRewards.Any(i => i.InstanceId == instanceId));

        public TxResult Apply(RunState run, RunTransaction tx)
        {
            var v = this.Validate(run, tx);
            if (!v.Succeeded)
                return v;
            var p = run.Find(tx.ParticipantId);
            switch (tx.Kind)
            {
                case TxKind.KillReward:
                    p!.Gold += tx.Amount;
                    this.GrantXp(p, tx.Xp);
                    if (tx.Item != null)
                        this.GiveItem(run, p, tx.Item);
                    break;
                case TxKind.GrantGold:
                    p!.Gold += tx.Amount;
                    this.GrantXp(p, tx.Xp);
                    break;
                case TxKind.SpendGold:
                    p!.Gold -= tx.Amount;
                    break;
                case TxKind.GrantItem:
                    this.GiveItem(run, p!, tx.Item!);
                    break;
                case TxKind.GrantMaterial:
                    p!.Materials[tx.Key ?? "unknown"] = p.Materials.GetValueOrDefault(tx.Key ?? "unknown") + (int)tx.Amount;
                    break;
                case TxKind.SellItem:
                {
                    var item = p!.Inventory.Remove(tx.ItemId!)!;
                    p.Gold += this.SellPrice(item);
                    break;
                }
                case TxKind.TradeItem:
                {
                    var other = run.Get(tx.OtherParticipantId!);
                    var item = p!.Inventory.Remove(tx.ItemId!)!;
                    other.Inventory.TryAdd(item, 1);
                    break;
                }
                case TxKind.MoveToChest:
                    p!.RunChest.TryAdd(p.Inventory.Remove(tx.ItemId!)!, 1);
                    break;
                case TxKind.TakeFromChest:
                    p!.Inventory.TryAdd(p.RunChest.Remove(tx.ItemId!)!, 1);
                    break;
                case TxKind.ClaimPendingReward:
                {
                    var item = p!.PendingRewards.First(i => i.InstanceId == tx.ItemId);
                    p.PendingRewards.Remove(item);
                    p.Inventory.TryAdd(item, this.data.Items[item.DefId].MaxStack);
                    break;
                }
                case TxKind.LoseLife:
                    p!.Lives--;
                    if (p.Lives <= 0)
                        p.Eliminated = true;
                    break;
                case TxKind.ShopRevive:
                    foreach (var c in tx.Contributions!)
                        run.Get(c.Key).Gold -= c.Value;
                    p!.Eliminated = false;
                    p.Lives = 1;
                    p.Hp = null;
                    break;
                case TxKind.PactVote:
                    run.Pact.Votes[tx.ParticipantId] = tx.Flag;
                    break;
                case TxKind.PactConsume:
                    run.Pact.Used = true;
                    run.Pact.VoteOpen = false;
                    foreach (var part in run.Participants)
                    {
                        part.Gold = 0;
                        part.Lives = this.data.Balance.Lives.PactLives;
                        part.Eliminated = false;
                        part.Hp = null;
                    }
                    break;
                case TxKind.BossMilestone:
                    foreach (var part in run.Participants)
                        part.UnspentSkillPoints += this.data.Balance.Progression.SkillPointsPerBossMilestone;
                    break;
                case TxKind.AdvanceLevel:
                    run.CurrentLevel = (int)tx.Amount;
                    break;
                case TxKind.EndRun:
                    run.Status = tx.Flag ? RunStatus.Won : RunStatus.Lost;
                    break;
            }
            run.AppliedTransactions.Add(tx.TxId);
            return TxResult.Ok;
        }

        /// <summary>Overflow goes to pending rewards rather than being destroyed (design §12).</summary>
        private void GiveItem(RunState run, RunParticipant p, ItemInstance item)
        {
            int maxStack = this.data.Items[item.DefId].MaxStack;
            if (!p.Inventory.TryAdd(item, maxStack))
                p.PendingRewards.Add(item);
            // keep the serial counter ahead of any journaled id so ids stay unique after recovery
            string prefix = $"itm-{run.RunId}-";
            if (item.InstanceId.StartsWith(prefix, StringComparison.Ordinal) && long.TryParse(item.InstanceId.Substring(prefix.Length), out long serial))
                run.NextItemSerial = Math.Max(run.NextItemSerial, serial + 1);
        }

        public long SellPrice(ItemInstance item)
        {
            var def = this.data.Items[item.DefId];
            double rarityFactor = 1 + 0.6 * ((int)item.Rarity - 1);
            return (long)Math.Floor(def.BaseValue * rarityFactor * item.StackCount * this.data.Balance.Economy.SellPriceFraction);
        }

        public void GrantXp(RunParticipant p, int xp)
        {
            if (xp <= 0)
                return;
            var prog = this.data.Balance.Progression;
            p.Xp += xp;
            while (p.Level < prog.MaxLevel && p.Xp >= prog.XpToNextLevel[p.Level - 1])
            {
                p.Xp -= prog.XpToNextLevel[p.Level - 1];
                p.Level++;
                p.UnspentSkillPoints += prog.SkillPointsPerLevel;
            }
            if (p.Level >= prog.MaxLevel)
                p.Xp = 0;
        }
    }
}
