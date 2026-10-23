using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Simulation
{
    /// <summary>
    /// Host-authoritative arena simulation. Advances <see cref="WorldState"/> one fixed tick at a time from player
    /// inputs. Contains no rendering, audio or networking; the same code runs headless in tests.
    /// </summary>
    public sealed partial class CombatWorld
    {
        private const int FlowFieldRebuildTicks = 10;

        private readonly DeterministicRng rng;
        private readonly FlowField flow;
        private int flowBuiltTick = int.MinValue;
        private readonly List<SimEvent> events = new();
        private readonly Dictionary<int, PlayerInput> noInputs = new();

        public GameData Data { get; }
        public BalanceConfig Balance => this.Data.Balance;
        public AttackRegistry Attacks { get; }
        public WorldState State { get; }
        public DifficultyConfig Difficulty { get; }

        /// <summary>Events emitted during the most recent <see cref="Step"/>.</summary>
        public IReadOnlyList<SimEvent> Events => this.events;

        public int Tick => this.State.Tick;

        public CombatWorld(GameData data, WorldState state)
        {
            if (state.SchemaVersion != WorldState.CurrentSchemaVersion)
                throw new InvalidOperationException($"Unsupported world state schema {state.SchemaVersion}.");
            this.Data = data;
            this.State = state;
            this.Attacks = new AttackRegistry(data);
            this.rng = DeterministicRng.FromState(state.Rng);
            this.flow = new FlowField(state.Arena);
            this.Difficulty = data.Balance.Scaling.Difficulties.FirstOrDefault(d => d.Id == state.DifficultyId)
                ?? data.Balance.Scaling.Difficulties[0];
        }

        /// <summary>Deep copy of the current state, including the RNG position (for snapshots and crash recovery).</summary>
        public WorldState CaptureState()
        {
            this.State.Rng = this.rng.SaveState();
            return this.State.DeepClone();
        }

        public PlayerEntity? FindPlayer(int id) => this.State.Players.Find(p => p.Id == id);
        public PlayerEntity? FindPlayerByParticipant(string participantId) => this.State.Players.Find(p => p.ParticipantId == participantId);
        public EnemyEntity? FindEnemy(int id) => this.State.Enemies.Find(e => e.Id == id);

        public void Step(IReadOnlyDictionary<int, PlayerInput>? inputs = null)
        {
            inputs ??= this.noInputs;
            this.events.Clear();
            int tick = this.State.Tick;

            if (this.State.Encounter.Status == EncounterStatus.InProgress)
            {
                foreach (var p in this.State.Players)
                {
                    bool has = inputs.TryGetValue(p.Id, out var input);
                    this.UpdatePlayer(p, has ? input : default, has);
                }
                this.UpdateEnemies();
                this.UpdateProjectiles();
                this.UpdateStatuses();
                this.UpdateLifeStates();
                this.UpdateEncounter();
            }
            this.State.Enemies.RemoveAll(e => e.Hp <= 0);
            this.State.Tick = tick + 1;
            this.State.Rng = this.rng.SaveState();
        }

        private void Emit(SimEventType type, int sourceId = 0, int targetId = 0, float amount = 0, string key = "", bool flag = false)
        {
            this.events.Add(new SimEvent { Type = type, Tick = this.State.Tick, SourceId = sourceId, TargetId = targetId, Amount = amount, Key = key, Flag = flag });
        }

        private SimEvent EmitAt(SimEventType type, Mathematics.Vec2 position, int sourceId = 0, int targetId = 0, float amount = 0, string key = "", bool flag = false)
        {
            var e = new SimEvent { Type = type, Tick = this.State.Tick, SourceId = sourceId, TargetId = targetId, Amount = amount, Key = key, Flag = flag, Position = position };
            this.events.Add(e);
            return e;
        }

        private int NewEntityId() => this.State.NextEntityId++;

        private void EnsureFlowField()
        {
            if (this.State.Tick - this.flowBuiltTick < FlowFieldRebuildTicks)
                return;
            this.flow.Build(this.State.Players.Where(p => p.LifeState == LifeState.Alive).Select(p => TilePoint.FromWorld(p.Position)));
            this.flowBuiltTick = this.State.Tick;
        }

        internal DeterministicRng Rng => this.rng;
    }
}
