using System;
using System.Collections.Generic;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Combat
{
    public readonly record struct DamageResult(float Amount, bool Critical, float AbsorbedByShield);

    /// <summary>Pure damage maths. Order: base × attacker multipliers → crit → armor/resistance → taken multipliers → shield.</summary>
    public static class DamageCalculator
    {
        public static float ArmorMitigation(float armor, CombatConfig config)
        {
            if (armor <= 0)
                return 0f;
            return Math.Min(config.MaxArmorMitigation, armor / (armor + config.ArmorConstant));
        }

        public static float ClampResistance(float value, CombatConfig config) => Math.Clamp(value, config.MinResistance, config.MaxResistance);

        public static DamageResult Compute(
            float baseDamage,
            DamageType type,
            float attackerMultiplier,
            bool canCrit,
            float critChance,
            DeterministicRng rng,
            float targetArmor,
            IReadOnlyDictionary<DamageType, float>? targetResistances,
            float targetTakenMultiplier,
            float targetShield,
            CombatConfig config)
        {
            if (baseDamage <= 0)
                return new DamageResult(0, false, 0);

            float dmg = baseDamage * Math.Max(0f, attackerMultiplier);
            bool crit = canCrit && critChance > 0 && rng.NextFloat() < critChance;
            if (crit)
                dmg *= config.CritMultiplier;

            if (type == DamageType.Physical)
                dmg *= 1f - ArmorMitigation(targetArmor, config);
            float resist = 0f;
            if (targetResistances != null && targetResistances.TryGetValue(type, out float r))
                resist = ClampResistance(r, config);
            dmg *= 1f - resist;
            dmg *= Math.Max(0f, targetTakenMultiplier);

            float absorbed = Math.Min(targetShield, dmg);
            dmg -= absorbed;
            return new DamageResult(dmg, crit, absorbed);
        }
    }
}
