using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Simulation
{
    /// <summary>Character state carried into an arena from the run (HP, lives, cooldowns persist between arenas).</summary>
    public sealed class PlayerLoadout
    {
        public string ParticipantId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ClassId { get; set; } = "warrior";
        public string WeaponTypeId { get; set; } = "";
        public int Level { get; set; } = 1;
        public float? Hp { get; set; }
        public int Lives { get; set; } = 3;
        public float Resource { get; set; }
        public int PotionCharges { get; set; } = 2;
        public List<string> EquippedAbilities { get; set; } = new();
        public string Ultimate { get; set; } = "";
        public float BonusMaxHp { get; set; }
        public float BonusArmor { get; set; }
        public float DamageMultiplier { get; set; } = 1f;
        public float BonusCritChance { get; set; }
        /// <summary>Remaining cooldown seconds per key; cooldowns are never reset by arena transitions or skill resets.</summary>
        public Dictionary<string, float> RemainingCooldowns { get; set; } = new();
        public bool Connected { get; set; } = true;
        public bool Eliminated { get; set; }
    }

    public sealed class ArenaSetup
    {
        public string RunId { get; set; } = "";
        public ulong Seed { get; set; }
        public int Level { get; set; } = 1;
        public string DifficultyId { get; set; } = "normal";
        public string ArenaTemplateId { get; set; } = "";
        public EncounterState Encounter { get; set; } = new();
        public List<PlayerLoadout> Players { get; set; } = new();
        public bool PactActive { get; set; }
    }

    public static class WorldFactory
    {
        public static WorldState Create(GameData data, ArenaSetup setup)
        {
            if (setup.Players.Count is < 1 or > 4)
                throw new ArgumentException("An arena needs 1-4 players.");
            var arena = new ArenaGenerator(data).Generate(setup.ArenaTemplateId, setup.Seed);
            var state = new WorldState
            {
                RunId = setup.RunId,
                Level = setup.Level,
                DifficultyId = setup.DifficultyId,
                Arena = arena,
                Encounter = setup.Encounter,
                Rng = new DeterministicRng(DeterministicRng.Mix(setup.Seed, 0x5EED)).SaveState(),
                PactActive = setup.PactActive,
            };
            // scaling counts players that can actually fight when the arena starts
            state.Encounter.ScalingPlayerCount = Math.Max(1, setup.Players.Count(p => !p.Eliminated));

            for (int i = 0; i < setup.Players.Count; i++)
            {
                var lo = setup.Players[i];
                var cls = data.Classes[lo.ClassId];
                float maxHp = cls.MaxHp + cls.HpPerLevel * (lo.Level - 1) + lo.BonusMaxHp;
                var weapon = string.IsNullOrEmpty(lo.WeaponTypeId) ? cls.WeaponTypes[0] : lo.WeaponTypeId;
                if (data.WeaponTypes[weapon].ClassId != cls.Id)
                    throw new ArgumentException($"Weapon type '{weapon}' is not usable by class '{cls.Id}'.");
                var start = arena.PlayerStarts[i % arena.PlayerStarts.Count];
                var p = new PlayerEntity
                {
                    Id = state.NextEntityId++,
                    ParticipantId = lo.ParticipantId,
                    DisplayName = lo.DisplayName,
                    ClassId = cls.Id,
                    WeaponTypeId = weapon,
                    Position = start.Center,
                    MaxHp = maxHp,
                    Hp = Math.Min(maxHp, lo.Hp ?? maxHp),
                    Armor = cls.Armor + cls.ArmorPerLevel * (lo.Level - 1) + lo.BonusArmor,
                    MoveSpeed = cls.MoveSpeed,
                    Radius = cls.Radius,
                    CritChance = cls.CritChance + lo.BonusCritChance,
                    DamageMultiplier = lo.DamageMultiplier,
                    ResourceMax = cls.Resource.Max,
                    Resource = Math.Clamp(lo.Resource, 0, cls.Resource.Max),
                    Lives = lo.Lives,
                    LifeState = lo.Eliminated || lo.Lives <= 0 ? LifeState.Eliminated : LifeState.Alive,
                    DashCharges = data.Balance.Dash.Charges,
                    PotionCharges = lo.PotionCharges,
                    EquippedAbilities = lo.EquippedAbilities.Take(2).ToList(),
                    Ultimate = string.IsNullOrEmpty(lo.Ultimate) ? cls.BaseUltimate : lo.Ultimate,
                    Connected = lo.Connected,
                };
                foreach (var cd in lo.RemainingCooldowns)
                    p.CooldownReadyTick[cd.Key] = SimTime.Ticks(cd.Value);
                if (p.LifeState == LifeState.Eliminated)
                    p.Hp = 0;
                state.Players.Add(p);
            }
            return state;
        }
    }
}
