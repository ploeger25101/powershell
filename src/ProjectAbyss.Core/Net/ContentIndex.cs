using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Net
{
    /// <summary>
    /// Stable string ↔ ushort mapping for ids that appear in every snapshot. Valid because the handshake guarantees that
    /// host and client run identical content (same <see cref="GameData.ContentHash"/>).
    /// </summary>
    public sealed class ContentIndex
    {
        private readonly List<string> strings;
        private readonly Dictionary<string, ushort> lookup;

        public ContentIndex(GameData data)
        {
            var all = new SortedSet<string>(StringComparer.Ordinal) { "" };
            foreach (var w in data.WeaponTypes.Keys)
            {
                all.Add(AttackRegistry.BasicKey(w));
                all.Add(AttackRegistry.SpecialKey(w));
                all.Add(w);
            }
            foreach (var a in data.Abilities.Keys)
            {
                all.Add(AttackRegistry.AbilityKey(a));
                all.Add(a);
            }
            foreach (var e in data.Enemies.Values)
            {
                all.Add(e.Id);
                foreach (var atk in e.Attacks)
                    all.Add(AttackRegistry.EnemyKey(e.Id, atk.Id));
            }
            foreach (var b in data.Bosses.Values)
            {
                all.Add(b.Id);
                foreach (var atk in b.Attacks)
                    all.Add(AttackRegistry.BossKey(b.Id, atk.Id));
            }
            foreach (var c in data.Classes.Keys)
                all.Add(c);
            foreach (var a in data.Abilities.Values.Where(a => a.SelfBuff != null))
                all.Add(a.SelfBuff!.Id);
            all.Add(Simulation.CombatWorld.SpecialCooldownKey);
            all.Add(Simulation.CombatWorld.UltimateCooldownKey);
            all.Add(Simulation.CombatWorld.PotionCooldownKey);
            this.strings = all.ToList();
            if (this.strings.Count > ushort.MaxValue)
                throw new InvalidOperationException("too many content ids");
            this.lookup = this.strings.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => (ushort)p.i, StringComparer.Ordinal);
        }

        public ushort Id(string key) =>
            this.lookup.TryGetValue(key, out ushort id) ? id : throw new KeyNotFoundException($"'{key}' is not an indexed content id");

        public string Key(ushort id) =>
            id < this.strings.Count ? this.strings[id] : throw new ProtocolException($"unknown content index {id}");
    }
}
