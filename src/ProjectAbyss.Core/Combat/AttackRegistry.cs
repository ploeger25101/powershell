using System.Collections.Generic;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Combat
{
    /// <summary>Resolves serialisable attack keys to definitions: w:{weapon}:basic|special, a:{ability}, e:{enemy}:{attack}, b:{boss}:{attack}.</summary>
    public sealed class AttackRegistry
    {
        private readonly Dictionary<string, AttackDef> attacks = new();

        public AttackRegistry(GameData data)
        {
            foreach (var w in data.WeaponTypes.Values)
            {
                this.attacks[BasicKey(w.Id)] = w.Basic;
                this.attacks[SpecialKey(w.Id)] = w.Special;
            }
            foreach (var a in data.Abilities.Values)
                this.attacks[AbilityKey(a.Id)] = a.Attack;
            foreach (var e in data.Enemies.Values)
            {
                foreach (var atk in e.Attacks)
                    this.attacks[EnemyKey(e.Id, atk.Id)] = atk;
            }
            foreach (var b in data.Bosses.Values)
            {
                foreach (var atk in b.Attacks)
                    this.attacks[BossKey(b.Id, atk.Id)] = atk;
            }
        }

        public static string BasicKey(string weapon) => $"w:{weapon}:basic";
        public static string SpecialKey(string weapon) => $"w:{weapon}:special";
        public static string AbilityKey(string ability) => $"a:{ability}";
        public static string EnemyKey(string enemy, string attack) => $"e:{enemy}:{attack}";
        public static string BossKey(string boss, string attack) => $"b:{boss}:{attack}";

        public AttackDef Get(string key) =>
            this.attacks.TryGetValue(key, out var def) ? def : throw new KeyNotFoundException($"Unknown attack key '{key}'.");

        public bool TryGet(string key, out AttackDef def) => this.attacks.TryGetValue(key, out def!);
    }
}
