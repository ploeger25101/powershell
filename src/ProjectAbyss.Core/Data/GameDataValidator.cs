using System.Collections.Generic;
using System.Linq;

namespace ProjectAbyss.Core.Data
{
    /// <summary>Cross-reference and range checks so broken content fails at load time instead of mid-run.</summary>
    public static class GameDataValidator
    {
        public static List<string> Validate(GameData data)
        {
            var errors = new List<string>();
            var b = data.Balance;
            if (b.Dash.Charges < 1)
                errors.Add("balance.dash.charges must be >= 1");
            if (b.Dash.InvulnerableSeconds > b.Dash.DurationSeconds)
                errors.Add("balance.dash.invulnerableSeconds must not exceed durationSeconds");
            if (b.Lives.StartLives < 1)
                errors.Add("balance.lives.startLives must be >= 1");
            foreach (var (name, list) in new[] { ("bossHp", b.Scaling.BossHp), ("enemyBudget", b.Scaling.EnemyBudget), ("enemyHp", b.Scaling.EnemyHp), ("enemyDamage", b.Scaling.EnemyDamage) })
            {
                if (list.Count != 4)
                    errors.Add($"balance.scaling.{name} needs exactly 4 entries (1-4 players)");
            }
            if (b.Scaling.Difficulties.Count == 0)
                errors.Add("balance.scaling.difficulties must define at least one difficulty");
            if (b.Progression.XpToNextLevel.Count != b.Progression.MaxLevel - 1)
                errors.Add($"balance.progression.xpToNextLevel needs {b.Progression.MaxLevel - 1} entries");

            foreach (var c in data.Classes.Values)
            {
                if (c.MaxHp <= 0)
                    errors.Add($"class {c.Id}: maxHp must be > 0");
                foreach (string w in c.WeaponTypes.Where(w => !data.WeaponTypes.ContainsKey(w)))
                    errors.Add($"class {c.Id}: unknown weapon type '{w}'");
                foreach (string a in c.StarterAbilities.Where(a => !data.Abilities.ContainsKey(a)))
                    errors.Add($"class {c.Id}: unknown starter ability '{a}'");
                if (!data.Abilities.TryGetValue(c.BaseUltimate, out var ult) || !ult.IsUltimate)
                    errors.Add($"class {c.Id}: baseUltimate '{c.BaseUltimate}' missing or not an ultimate");
            }
            foreach (var w in data.WeaponTypes.Values)
            {
                if (!data.Classes.ContainsKey(w.ClassId))
                    errors.Add($"weapon {w.Id}: unknown class '{w.ClassId}'");
                ValidateAttack($"weapon {w.Id}.basic", w.Basic, data, errors);
                ValidateAttack($"weapon {w.Id}.special", w.Special, data, errors);
            }
            foreach (var a in data.Abilities.Values)
                ValidateAttack($"ability {a.Id}", a.Attack, data, errors);
            foreach (var e in data.Enemies.Values)
            {
                if (e.Attacks.Count == 0)
                    errors.Add($"enemy {e.Id}: needs at least one attack");
                if (e.SpawnCost <= 0)
                    errors.Add($"enemy {e.Id}: spawnCost must be > 0");
                foreach (var atk in e.Attacks)
                    ValidateAttack($"enemy {e.Id}.{atk.Id}", atk, data, errors);
            }
            foreach (var boss in data.Bosses.Values)
            {
                if (boss.Phases.Count < 3)
                    errors.Add($"boss {boss.Id}: needs at least 3 phases");
                var attackIds = boss.Attacks.Select(a => a.Id).ToHashSet();
                foreach (var phase in boss.Phases)
                {
                    foreach (string id in phase.Attacks.Concat(phase.CoopAttacks).Where(id => !attackIds.Contains(id)))
                        errors.Add($"boss {boss.Id}: phase references unknown attack '{id}'");
                }
                if (!string.IsNullOrEmpty(boss.PactAttack) && !attackIds.Contains(boss.PactAttack))
                    errors.Add($"boss {boss.Id}: unknown pact attack '{boss.PactAttack}'");
                if (!data.Arenas.ContainsKey(boss.ArenaTemplate))
                    errors.Add($"boss {boss.Id}: unknown arena '{boss.ArenaTemplate}'");
                foreach (var atk in boss.Attacks)
                    ValidateAttack($"boss {boss.Id}.{atk.Id}", atk, data, errors);
            }
            foreach (var arena in data.Arenas.Values)
            {
                if (arena.Rows.Count == 0 || arena.Rows.Any(r => r.Length != arena.Rows[0].Length))
                    errors.Add($"arena {arena.Id}: rows must be non-empty and rectangular");
            }
            if (!data.Arenas.Values.Any(a => a.IsFallback))
                errors.Add("at least one arena must be marked isFallback");
            foreach (var item in data.Items.Values)
            {
                if (item.Category == ItemCategory.Weapon && (item.WeaponType == null || !data.WeaponTypes.ContainsKey(item.WeaponType)))
                    errors.Add($"item {item.Id}: weapon needs a valid weaponType");
                if (item.MaxStack < 1)
                    errors.Add($"item {item.Id}: maxStack must be >= 1");
            }
            return errors;
        }

        private static void ValidateAttack(string owner, AttackDef atk, GameData data, List<string> errors)
        {
            if (atk.WindupSeconds < 0 || atk.ActiveSeconds <= 0 && atk.Shape != HitShape.None || atk.RecoverySeconds < 0)
                errors.Add($"{owner}: invalid timing");
            if (atk.Shape == HitShape.Projectile && atk.Projectile == null)
                errors.Add($"{owner}: projectile attack needs 'projectile'");
            if (atk.SummonEnemyId != null && !data.Enemies.ContainsKey(atk.SummonEnemyId))
                errors.Add($"{owner}: unknown summon '{atk.SummonEnemyId}'");
            if (atk.Damage < 0)
                errors.Add($"{owner}: damage must be >= 0");
        }
    }
}
