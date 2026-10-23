using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;
using ProjectAbyss.Core.Run;

namespace ProjectAbyss.Core.Items
{
    /// <summary>Personal loot rolls. Blueprints widen the pool; they never grant free starting items.</summary>
    public sealed class LootGenerator
    {
        public const float NormalDropChance = 0.06f;
        public const float EliteDropChance = 0.5f;
        private static readonly float[] BaseRarityWeights = { 60f, 25f, 10f, 4f, 0.9f, 0.1f };
        private readonly GameData data;

        public LootGenerator(GameData data)
        {
            this.data = data;
        }

        public ItemInstance? RollDrop(RunState run, DeterministicRng rng, RunParticipant owner, bool elite, bool boss, ISet<string> blueprints)
        {
            float chance = boss ? 1f : elite ? EliteDropChance : NormalDropChance;
            if (!rng.Chance(chance))
                return null;
            return this.Generate(run, rng, owner, boss ? 2 : elite ? 1 : 0, blueprints);
        }

        public ItemInstance? Generate(RunState run, DeterministicRng rng, RunParticipant owner, int rarityBoost, ISet<string> blueprints)
        {
            var pool = this.data.Items.Values
                .Where(i => i.Category is ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Jewelry)
                .Where(i => string.IsNullOrEmpty(i.ClassId) || i.ClassId == owner.ClassId)
                .Where(i => !i.RequiresBlueprint || blueprints.Contains(i.Id))
                .OrderBy(i => i.Id, StringComparer.Ordinal)
                .ToList();
            if (pool.Count == 0)
                return null;
            var def = pool[rng.NextInt(0, pool.Count)];
            var rarity = this.RollRarity(rng, run.CurrentLevel, rarityBoost);
            if (rarity < def.MinRarity)
                rarity = def.MinRarity;
            var item = new ItemInstance
            {
                InstanceId = run.NewItemId(),
                DefId = def.Id,
                Rarity = rarity,
                ItemLevel = run.CurrentLevel,
                ForgePotential = 10 + 5 * (int)rarity,
            };
            if (def.MaxSockets > 0)
                item.Sockets = Math.Min(def.MaxSockets, rng.NextInt(0, 1 + (int)rarity / 2 + 1));
            int affixCount = rarity switch
            {
                Rarity.Common => 0,
                Rarity.Uncommon => 1,
                Rarity.Rare => 2,
                Rarity.Epic or Rarity.Legendary => 3,
                _ => 4,
            };
            var affixes = this.data.Affixes.Where(a => a.AllowedCategories.Contains(def.Category) && a.MinRarity <= rarity).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
            for (int i = 0; i < affixCount && affixes.Count > 0; i++)
            {
                var a = affixes[rng.NextInt(0, affixes.Count)];
                affixes.Remove(a);
                float levelScale = 1f + 0.04f * (run.CurrentLevel - 1);
                item.Affixes.Add(new AffixRoll { AffixId = a.Id, Stat = a.Stat, Value = MathF.Round(rng.NextFloat(a.Min, a.Max) * levelScale, 3) });
            }
            return item;
        }

        public Rarity RollRarity(DeterministicRng rng, int level, int boost)
        {
            var weights = (float[])BaseRarityWeights.Clone();
            // deeper levels and bosses shift weight from common to higher tiers
            float shift = Math.Min(40f, level * 1.2f + boost * 12f);
            weights[0] = Math.Max(5f, weights[0] - shift);
            weights[2] += shift * 0.45f;
            weights[3] += shift * 0.35f;
            weights[4] += shift * 0.15f;
            weights[5] += shift * 0.05f;
            float total = weights.Sum();
            float r = rng.NextFloat() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (r < weights[i])
                    return (Rarity)(i + 1);
                r -= weights[i];
            }
            return Rarity.Common;
        }
    }
}
