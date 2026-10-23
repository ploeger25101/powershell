using System.Linq;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Simulation
{
    /// <summary>
    /// Simple scripted player used by headless tests, load tests and the developer mode ("bot" co-op partner).
    /// It only produces <see cref="PlayerInput"/>; it has no privileged access to the simulation.
    /// </summary>
    public sealed class AutoPilot
    {
        private readonly DeterministicRng rng;
        private uint sequence;
        private InputButtons lastHeld;
        private FlowField? flow;
        private ArenaMap? flowArena;
        private TilePoint flowTarget;
        private int flowBuiltTick = int.MinValue;

        public AutoPilot(ulong seed)
        {
            this.rng = new DeterministicRng(seed, 0xB07);
        }

        public PlayerInput Next(WorldState state, int playerId)
        {
            var me = state.Players.Find(p => p.Id == playerId);
            this.sequence++;
            if (me == null || me.LifeState != LifeState.Alive)
                return this.Emit(Vec2.Zero, 0f, InputButtons.None);

            var downedAlly = state.Players.FirstOrDefault(p => p.Id != playerId && p.LifeState == LifeState.Downed);
            if (downedAlly != null)
            {
                var toAlly = downedAlly.Position - me.Position;
                if (toAlly.Length > 20f)
                    return this.Emit(toAlly.Normalized(), toAlly.Angle, InputButtons.None);
                return this.Emit(Vec2.Zero, toAlly.Angle, InputButtons.Interact);
            }

            var target = state.Enemies.Where(e => e.Hp > 0).OrderBy(e => Vec2.DistanceSquared(e.Position, me.Position)).FirstOrDefault();
            if (target == null)
                return this.Emit(Vec2.Zero, me.Aim, InputButtons.None);
            var to = target.Position - me.Position;
            float dist = to.Length;
            var held = InputButtons.None;
            Vec2 move = this.PathTo(state, me, target.Position);
            if (dist < 26f + target.Radius)
            {
                held |= InputButtons.Attack;
                move = Vec2.Zero;
                if (this.rng.Chance(0.02f))
                    held |= InputButtons.Special;
                if (this.rng.Chance(0.01f))
                    held |= InputButtons.Ability1;
                if (this.rng.Chance(0.005f))
                    held |= InputButtons.Ability2;
                if (me.Resource >= 60 && this.rng.Chance(0.01f))
                    held |= InputButtons.Ultimate;
            }
            // dodge telegraphed attacks of nearby enemies
            bool threatened = state.Enemies.Any(e => e.Action != null && e.Action.PhaseAt(state.Tick) == ActionPhase.Windup
                && e.Action.StartTick + e.Action.WindupTicks - state.Tick < 8 && Vec2.Distance(e.Position, me.Position) < 70f);
            if (threatened && me.DashCharges > 0)
            {
                held |= InputButtons.Dash;
                move = new Vec2(-to.Y, to.X).Normalized();
            }
            if (me.Hp < me.MaxHp * 0.35f)
                held |= InputButtons.Potion;
            return this.Emit(move, to.Angle, held);
        }

        /// <summary>Straight line when the way is clear, otherwise follow a BFS field towards the target tile.</summary>
        private Vec2 PathTo(WorldState state, PlayerEntity me, Vec2 goal)
        {
            if (state.Arena.HasClearPath(me.Position, goal, me.Radius))
                return (goal - me.Position).Normalized();
            var goalTile = TilePoint.FromWorld(goal);
            if (this.flow == null || this.flowArena != state.Arena || goalTile != this.flowTarget || state.Tick - this.flowBuiltTick > 30)
            {
                this.flow ??= new FlowField(state.Arena);
                if (this.flowArena != state.Arena)
                    this.flow = new FlowField(state.Arena);
                this.flowArena = state.Arena;
                this.flow.Build(new[] { goalTile });
                this.flowTarget = goalTile;
                this.flowBuiltTick = state.Tick;
            }
            var d = this.flow.DirectionFrom(me.Position);
            return d.LengthSquared > 0 ? d : (goal - me.Position).Normalized();
        }

        private PlayerInput Emit(Vec2 move, float aim, InputButtons held)
        {
            var pressed = held & ~this.lastHeld;
            this.lastHeld = held;
            return PlayerInput.Create(this.sequence, move, aim, held, pressed);
        }
    }
}
