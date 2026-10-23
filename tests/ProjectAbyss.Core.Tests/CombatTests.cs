using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Randomness;
using ProjectAbyss.Core.Simulation;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class CombatTests
    {
        private static Dictionary<int, PlayerInput> Input(int playerId, uint seq, Vec2 move, float aim, InputButtons held, InputButtons pressed = InputButtons.None) =>
            new() { [playerId] = PlayerInput.Create(seq, move, aim, held, pressed) };

        [Fact]
        public void DashHasTwoChargesAndRecharges()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            uint seq = 1;
            world.Step(Input(p.Id, seq++, new Vec2(1, 0), 0, InputButtons.Dash, InputButtons.Dash));
            Assert.Equal(1, p.DashCharges);
            TestData.Run(world, 20);
            world.Step(Input(p.Id, seq++, new Vec2(1, 0), 0, InputButtons.Dash, InputButtons.Dash));
            Assert.Equal(0, p.DashCharges);
            TestData.Run(world, 20);
            var before = p.Position;
            world.Step(Input(p.Id, seq++, new Vec2(1, 0), 0, InputButtons.Dash, InputButtons.Dash));
            Assert.Equal(0, p.DashCharges); // no charge left
            TestData.Run(world, SimTime.Ticks(3f));
            Assert.Equal(1, p.DashCharges);
        }

        [Fact]
        public void DashMovesConfiguredDistanceAndGrantsShortInvulnerability()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            var start = p.Position;
            world.Step(Input(p.Id, 1, new Vec2(1, 0), 0, InputButtons.Dash, InputButtons.Dash));
            Assert.False(p.CanBeDamaged(world.Tick));
            TestData.Run(world, SimTime.Ticks(0.22f));
            Assert.InRange(p.Position.X - start.X, 55f, 70f);
            Assert.True(p.CanBeDamaged(world.Tick));
        }

        [Fact]
        public void BasicAttackHitsInAimDirectionOnly()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            var front = world.SpawnEnemy("forest_ghoul", p.Position + new Vec2(22, 0), false);
            var behind = world.SpawnEnemy("forest_ghoul", p.Position + new Vec2(-22, 0), false);
            front.NextDecisionTick = behind.NextDecisionTick = 100000;
            world.Step(Input(p.Id, 1, Vec2.Zero, 0f, InputButtons.Attack, InputButtons.Attack));
            TestData.Run(world, 12);
            Assert.True(front.Hp < front.MaxHp);
            Assert.Equal(behind.MaxHp, behind.Hp);
        }

        [Fact]
        public void DamageCalculatorAppliesArmorResistanceAndShield()
        {
            var cfg = TestData.Data.Balance.Combat;
            var rng = new DeterministicRng(1);
            var phys = DamageCalculator.Compute(100, DamageType.Physical, 1f, false, 0, rng, 100, null, 1f, 0, cfg);
            Assert.Equal(50f, phys.Amount, 3);
            var fire = DamageCalculator.Compute(100, DamageType.Fire, 1f, false, 0, rng, 100, new Dictionary<DamageType, float> { [DamageType.Fire] = 0.25f }, 1f, 0, cfg);
            Assert.Equal(75f, fire.Amount, 3);
            var capped = DamageCalculator.Compute(100, DamageType.Fire, 1f, false, 0, rng, 0, new Dictionary<DamageType, float> { [DamageType.Fire] = 5f }, 1f, 0, cfg);
            Assert.Equal(25f, capped.Amount, 3);
            var shielded = DamageCalculator.Compute(100, DamageType.Arcane, 1f, false, 0, rng, 0, null, 1f, 30, cfg);
            Assert.Equal(70f, shielded.Amount, 3);
            Assert.Equal(30f, shielded.AbsorbedByShield, 3);
            var crit = DamageCalculator.Compute(100, DamageType.Arcane, 1f, true, 1f, rng, 0, null, 1f, 0, cfg);
            Assert.True(crit.Critical);
            Assert.Equal(150f, crit.Amount, 3);
        }

        [Fact]
        public void ParryNegatesHitAndRipostes()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            var ghoul = world.SpawnEnemy("forest_ghoul", p.Position + new Vec2(16, 0), false);
            ghoul.NextDecisionTick = 0;
            uint seq = 1;
            bool parried = false;
            // wait until the ghoul starts its claw windup, then parry
            for (int i = 0; i < 300 && !parried; i++)
            {
                var held = ghoul.Action != null && ghoul.Action.PhaseAt(world.Tick) == ActionPhase.Windup
                    && ghoul.Action.StartTick + ghoul.Action.WindupTicks - world.Tick <= 6 ? InputButtons.Special : InputButtons.None;
                world.Step(Input(p.Id, seq++, Vec2.Zero, 0f, held, held));
                parried |= world.Events.Any(e => e.Type == SimEventType.Parried);
            }
            Assert.True(parried);
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.True(ghoul.Hp < ghoul.MaxHp);
        }

        [Fact]
        public void StunsOnNormalEnemiesDiminish()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            p.WeaponTypeId = "warrior_mace";
            var ghoul = world.SpawnEnemy("forest_ghoul", p.Position + new Vec2(30, 0), false);
            ghoul.Hp = ghoul.MaxHp = 100000;
            var stunDurations = new List<int>();
            uint seq = 1;
            for (int i = 0; i < 3; i++)
            {
                p.CooldownReadyTick.Clear();
                ghoul.Position = p.Position + new Vec2(30, 0);
                int stunTick = -1;
                int before = ghoul.StunnedUntilTick;
                world.Step(Input(p.Id, seq++, Vec2.Zero, 0f, InputButtons.Special, InputButtons.Special));
                for (int t = 0; t < 70; t++)
                {
                    world.Step(Input(p.Id, seq++, Vec2.Zero, 0f, InputButtons.None));
                    if (stunTick < 0 && world.Events.Any(e => e.Type == SimEventType.Hit && e.TargetId == ghoul.Id))
                        stunTick = world.Tick - 1;
                }
                Assert.True(stunTick > 0, $"special did not hit in round {i}, action={p.Action?.AttackKey} hp={ghoul.Hp} dist={Vec2.Distance(p.Position, ghoul.Position)} life={p.LifeState}");
                stunDurations.Add(System.Math.Max(0, ghoul.StunnedUntilTick - System.Math.Max(before, stunTick)));
            }
            Assert.Equal(SimTime.Ticks(1f), stunDurations[0]);
            Assert.Equal(SimTime.Ticks(1f) / 2, stunDurations[1]);
            Assert.Equal(0, stunDurations[2]);
        }

        [Fact]
        public void SoloDeathCostsLifeRespawnsAndFinallyEliminates()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            var events = new List<SimEvent>();
            for (int life = 3; life >= 1; life--)
            {
                // wait for a respawn to finish before the next lethal hit
                TestData.Run(world, SimTime.Ticks(4f));
                Assert.Equal(LifeState.Alive, p.LifeState);
                p.Hp = 1;
                var archer = world.SpawnEnemy("forest_thorn_archer", p.Position + new Vec2(40, 0), false);
                for (int i = 0; i < 400 && p.LifeState == LifeState.Alive; i++)
                {
                    world.Step();
                    events.AddRange(world.Events);
                }
                archer.Hp = 0;
                Assert.Equal(life - 1, p.Lives);
            }
            TestData.Run(world, 5);
            Assert.Equal(LifeState.Eliminated, p.LifeState);
            Assert.Equal(EncounterStatus.Failed, world.State.Encounter.Status);
        }

        [Fact]
        public void AllyCanReviveWithoutLosingALife()
        {
            var world = TestData.EmptyArena(players: 2);
            var a = world.State.Players[0];
            var b = world.State.Players[1];
            b.Position = a.Position + new Vec2(10, 0);
            a.Hp = 1;
            var ghoul = world.SpawnEnemy("forest_ghoul", a.Position + new Vec2(-16, 0), false);
            for (int i = 0; i < 400 && a.LifeState == LifeState.Alive; i++)
                world.Step();
            Assert.Equal(LifeState.Downed, a.LifeState);
            ghoul.Hp = 0;
            uint seq = 1;
            for (int i = 0; i < SimTime.Ticks(3f); i++)
                world.Step(Input(b.Id, seq++, Vec2.Zero, 0, InputButtons.Interact));
            Assert.Equal(LifeState.Alive, a.LifeState);
            Assert.Equal(3, a.Lives);
            Assert.Equal(a.MaxHp * 0.3f, a.Hp, 2);
        }

        [Fact]
        public void BossChangesPhasesAtThresholds()
        {
            var setup = new ArenaSetup
            {
                RunId = "t", Seed = 3, ArenaTemplateId = "forest_boss_warden",
                Encounter = EncounterDirector.BossEncounter("forest_warden", 1),
                Players = { TestData.Warrior("p0") },
            };
            var world = new CombatWorld(TestData.Data, WorldFactory.Create(TestData.Data, setup));
            world.Step();
            var boss = world.State.Enemies.Single(e => e.IsBoss);
            Assert.Equal(0, boss.Boss!.PhaseIndex);
            boss.Hp = boss.MaxHp * 0.69f;
            world.Step();
            Assert.Equal(1, boss.Boss.PhaseIndex);
            Assert.Contains("thorn_ring", world.AvailableBossAttacks(boss));
            Assert.DoesNotContain("thorn_barrage", world.AvailableBossAttacks(boss)); // co-op only
            boss.Hp = boss.MaxHp * 0.3f;
            world.Step();
            Assert.Equal(2, boss.Boss.PhaseIndex);
            Assert.Contains("charge", world.AvailableBossAttacks(boss));
        }

        [Fact]
        public void BossHpScalesWithPlayerCount()
        {
            float Hp(int players)
            {
                var setup = new ArenaSetup { RunId = "t", Seed = 3, ArenaTemplateId = "forest_boss_warden", Encounter = EncounterDirector.BossEncounter("forest_warden", players) };
                for (int i = 0; i < players; i++)
                    setup.Players.Add(TestData.Warrior("p" + i));
                var w = new CombatWorld(TestData.Data, WorldFactory.Create(TestData.Data, setup));
                w.Step();
                return w.State.Enemies.Single(e => e.IsBoss).MaxHp;
            }
            Assert.Equal(4400f, Hp(1), 1);
            Assert.Equal(4400f * 1.75f, Hp(2), 1);
            Assert.Equal(4400f * 3.25f, Hp(4), 1);
        }

        [Fact]
        public void StaggerIsFollowedByImmunitySoBossCannotBeStunLocked()
        {
            var setup = new ArenaSetup { RunId = "t", Seed = 3, ArenaTemplateId = "forest_boss_warden", Encounter = EncounterDirector.BossEncounter("forest_warden", 1), Players = { TestData.Warrior("p0", "warrior_mace") } };
            var world = new CombatWorld(TestData.Data, WorldFactory.Create(TestData.Data, setup));
            world.Step();
            var boss = world.State.Enemies.Single(e => e.IsBoss);
            boss.Boss!.Stagger = 299;
            var p = world.State.Players[0];
            p.Position = boss.Position + new Vec2(-30, 0);
            world.Step(Input(p.Id, 1, Vec2.Zero, 0f, InputButtons.Attack, InputButtons.Attack));
            TestData.Run(world, 15);
            Assert.True(boss.Boss.StaggeredUntilTick > world.Tick);
            int immuneUntil = boss.Boss.StaggerImmuneUntilTick;
            Assert.True(immuneUntil - boss.Boss.StaggeredUntilTick >= SimTime.Ticks(8f) - 1);
            // stagger damage during the window is ignored
            TestData.Run(world, SimTime.Ticks(3.1f));
            p.Position = boss.Position + new Vec2(-30, 0);
            boss.Boss.Stagger = 0;
            world.Step(Input(p.Id, 2, Vec2.Zero, (boss.Position - p.Position).Angle, InputButtons.Attack, InputButtons.Attack));
            TestData.Run(world, 15);
            Assert.Equal(0f, boss.Boss.Stagger);
        }

        [Fact]
        public void ReconnectProtectionBlocksDamageAndActions()
        {
            var world = TestData.EmptyArena();
            var p = world.State.Players[0];
            world.GrantReconnectProtection(p.Id);
            var archer = world.SpawnEnemy("forest_thorn_archer", p.Position + new Vec2(60, 0), false);
            archer.NextDecisionTick = 0;
            var ghoul = world.SpawnEnemy("forest_ghoul", p.Position + new Vec2(20, 0), false);
            ghoul.NextDecisionTick = 100000;
            uint seq = 1;
            for (int i = 0; i < SimTime.Ticks(1.9f); i++)
                world.Step(Input(p.Id, seq++, Vec2.Zero, 0f, InputButtons.Attack | InputButtons.Ability1, InputButtons.Attack | InputButtons.Ability1));
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.Equal(ghoul.MaxHp, ghoul.Hp);
            Assert.True(world.IsReady(p, "warrior_whirlwind")); // no ability was used
        }

        [Fact]
        public void DisconnectedPlayerStaysVulnerable()
        {
            var world = TestData.EmptyArena(players: 2);
            var p = world.State.Players[0];
            world.SetConnected(p.Id, false);
            var ghoul = world.SpawnEnemy("forest_ghoul", p.Position + new Vec2(16, 0), false);
            ghoul.NextDecisionTick = 0;
            TestData.Run(world, 120);
            Assert.True(p.Hp < p.MaxHp);
        }

        [Fact]
        public void SnapshotRestoreContinuesIdentically()
        {
            var director = new EncounterDirector(TestData.Data);
            var enc = director.Build(EncounterKind.Waves, 1, 3, "normal", 2, false, new DeterministicRng(5));
            var setup = new ArenaSetup { RunId = "t", Seed = 11, ArenaTemplateId = "forest_clearing_01", Encounter = enc, Players = { TestData.Warrior("a"), TestData.Warrior("b", "warrior_axe") } };
            var world = new CombatWorld(TestData.Data, WorldFactory.Create(TestData.Data, setup));
            var botA = new AutoPilot(1);
            var botB = new AutoPilot(2);
            Dictionary<int, PlayerInput> Inputs(CombatWorld w, AutoPilot a, AutoPilot b) => new()
            {
                [w.State.Players[0].Id] = a.Next(w.State, w.State.Players[0].Id),
                [w.State.Players[1].Id] = b.Next(w.State, w.State.Players[1].Id),
            };
            for (int i = 0; i < 600; i++)
                world.Step(Inputs(world, botA, botB));
            var snapshot = world.CaptureState();
            var restored = new CombatWorld(TestData.Data, snapshot.DeepClone());
            // bots are stateful too; recreate them identically for both branches
            var a1 = new AutoPilot(10); var b1 = new AutoPilot(20);
            var a2 = new AutoPilot(10); var b2 = new AutoPilot(20);
            for (int i = 0; i < 900; i++)
            {
                world.Step(Inputs(world, a1, b1));
                restored.Step(Inputs(restored, a2, b2));
            }
            string s1 = System.Text.Json.JsonSerializer.Serialize(world.CaptureState(), GameData.JsonOptions);
            string s2 = System.Text.Json.JsonSerializer.Serialize(restored.CaptureState(), GameData.JsonOptions);
            Assert.Equal(s1, s2);
        }

        [Fact]
        public void TwoBotsCanClearAWaveArena()
        {
            var director = new EncounterDirector(TestData.Data);
            var enc = director.Build(EncounterKind.Waves, 1, 1, "normal", 2, false, new DeterministicRng(8));
            var setup = new ArenaSetup { RunId = "t", Seed = 8, ArenaTemplateId = "forest_clearing_01", Encounter = enc, Players = { TestData.Warrior("a", "warrior_axe"), TestData.Warrior("b", "warrior_mace") } };
            var world = new CombatWorld(TestData.Data, WorldFactory.Create(TestData.Data, setup));
            var bots = new[] { new AutoPilot(1), new AutoPilot(2) };
            int kills = 0;
            for (int i = 0; i < SimTime.Ticks(180f) && world.State.Encounter.Status == EncounterStatus.InProgress; i++)
            {
                world.Step(new Dictionary<int, PlayerInput>
                {
                    [world.State.Players[0].Id] = bots[0].Next(world.State, world.State.Players[0].Id),
                    [world.State.Players[1].Id] = bots[1].Next(world.State, world.State.Players[1].Id),
                });
                kills += world.Events.Count(e => e.Type == SimEventType.EnemyKilled);
            }
            Assert.Equal(EncounterStatus.Cleared, world.State.Encounter.Status);
            Assert.True(kills >= enc.Waves.Sum(w => w.Count));
        }
    }
}
