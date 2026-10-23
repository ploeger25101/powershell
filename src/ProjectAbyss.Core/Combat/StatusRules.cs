using System;
using System.Collections.Generic;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Combat
{
    /// <summary>Stacking and expiry rules for status effects.</summary>
    public static class StatusRules
    {
        public static int MaxStacks(StatusType type) => type switch
        {
            StatusType.Bleed => 3,
            StatusType.Poison => 5,
            _ => 1,
        };

        public static bool IsDamageOverTime(StatusType type) => type is StatusType.Bleed or StatusType.Poison or StatusType.Burn;

        /// <summary>Add or refresh a status. Returns the resulting instance.</summary>
        public static StatusInstance Apply(List<StatusInstance> statuses, StatusType type, int nowTick, int durationTicks, float potency, int sourceId, int tickInterval)
        {
            var existing = statuses.Find(s => s.Type == type);
            if (existing == null)
            {
                existing = new StatusInstance { Type = type, ExpireTick = nowTick + durationTicks, NextTick = nowTick + tickInterval, Potency = potency, SourceId = sourceId };
                statuses.Add(existing);
                return existing;
            }
            existing.ExpireTick = Math.Max(existing.ExpireTick, nowTick + durationTicks);
            existing.Potency = Math.Max(existing.Potency, potency);
            existing.Stacks = Math.Min(MaxStacks(type), existing.Stacks + 1);
            existing.SourceId = sourceId;
            return existing;
        }

        public static float SlowMultiplier(List<StatusInstance> statuses, int nowTick)
        {
            var frost = statuses.Find(s => s.Type == StatusType.Frost && s.ExpireTick > nowTick);
            return frost == null ? 1f : Math.Clamp(1f - frost.Potency, 0.2f, 1f);
        }

        public static float ShockTakenMultiplier(List<StatusInstance> statuses, int nowTick)
        {
            var shock = statuses.Find(s => s.Type == StatusType.Shock && s.ExpireTick > nowTick);
            return shock == null ? 1f : 1f + Math.Clamp(shock.Potency, 0f, 0.5f);
        }

        public static void RemoveExpired(List<StatusInstance> statuses, int nowTick) => statuses.RemoveAll(s => s.ExpireTick <= nowTick);
    }
}
