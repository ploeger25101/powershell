using System;
using System.Linq;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    /// <summary>Downed / revive / life loss / respawn / elimination rules (design §21).</summary>
    public sealed partial class CombatWorld
    {
        public const float RespawnDelaySeconds = 3f;

        private void DownPlayer(PlayerEntity p)
        {
            int tick = this.State.Tick;
            p.Hp = 0;
            p.Action = null;
            p.Statuses.Clear();
            p.LifeState = LifeState.Downed;
            p.LifeStateUntilTick = tick + SimTime.Ticks(this.Balance.Lives.ReviveWindowSeconds);
            p.ReviveProgressTicks = 0;
            this.EmitAt(SimEventType.PlayerDowned, p.Position, 0, p.Id);
            if (!this.AnyPotentialReviver(p))
                this.LoseLife(p);
        }

        /// <summary>Waiting only makes sense if a connected ally is alive (or about to respawn).</summary>
        private bool AnyPotentialReviver(PlayerEntity downed) =>
            this.State.Players.Any(o => o.Id != downed.Id && o.Connected && (o.LifeState == LifeState.Alive || o.LifeState == LifeState.Respawning));

        private void LoseLife(PlayerEntity p)
        {
            int tick = this.State.Tick;
            p.Lives = Math.Max(0, p.Lives - 1);
            var ev = this.EmitAt(SimEventType.LifeLost, p.Position, 0, p.Id);
            ev.IntValue = p.Lives;
            if (p.Lives > 0)
            {
                p.LifeState = LifeState.Respawning;
                p.LifeStateUntilTick = tick + SimTime.Ticks(RespawnDelaySeconds);
            }
            else
            {
                p.LifeState = LifeState.Eliminated;
                p.LifeStateUntilTick = -1;
                this.EmitAt(SimEventType.PlayerEliminated, p.Position, 0, p.Id);
            }
        }

        private void UpdateLifeStates()
        {
            int tick = this.State.Tick;
            var lives = this.Balance.Lives;
            foreach (var p in this.State.Players)
            {
                switch (p.LifeState)
                {
                    case LifeState.Downed:
                        var reviver = this.State.Players.FirstOrDefault(o =>
                            o.Id != p.Id && o.LifeState == LifeState.Alive && o.Connected && !o.IsProtected(tick) && o.Action == null
                            && (o.LastHeld & InputButtons.Interact) != 0
                            && Vec2.Distance(o.Position, p.Position) <= lives.ReviveRadius + o.Radius + p.Radius);
                        if (reviver != null)
                        {
                            p.ReviveProgressTicks++;
                            if (p.ReviveProgressTicks >= SimTime.Ticks(lives.ReviveChannelSeconds))
                            {
                                p.LifeState = LifeState.Alive;
                                p.Hp = p.MaxHp * lives.ReviveHpFraction;
                                p.HitInvulnerableUntilTick = tick + SimTime.Ticks(1f);
                                this.EmitAt(SimEventType.PlayerRevived, p.Position, reviver.Id, p.Id);
                            }
                        }
                        else if (tick >= p.LifeStateUntilTick || !this.AnyPotentialReviver(p))
                        {
                            this.LoseLife(p);
                        }
                        break;

                    case LifeState.Respawning:
                        if (tick >= p.LifeStateUntilTick)
                        {
                            p.LifeState = LifeState.Alive;
                            p.Hp = p.MaxHp * lives.RespawnHpFraction;
                            p.Position = this.SafestPlayerStart();
                            p.HitInvulnerableUntilTick = tick + SimTime.Ticks(lives.RespawnInvulnerableSeconds);
                            this.EmitAt(SimEventType.PlayerRespawned, p.Position, 0, p.Id);
                        }
                        break;
                }
            }
        }

        /// <summary>Player start farthest from any living enemy.</summary>
        public Vec2 SafestPlayerStart()
        {
            var starts = this.State.Arena.PlayerStarts;
            var alive = this.State.Enemies.Where(e => e.Hp > 0).ToList();
            if (alive.Count == 0)
                return starts[0].Center;
            return starts
                .OrderByDescending(s => alive.Min(e => Vec2.DistanceSquared(e.Position, s.Center)))
                .First().Center;
        }

        /// <summary>Host-side: mark a participant (dis)connected. The character stays in the world either way.</summary>
        public void SetConnected(int playerId, bool connected)
        {
            var p = this.FindPlayer(playerId) ?? throw new ArgumentException($"Unknown player {playerId}.");
            p.Connected = connected;
        }

        /// <summary>Reconnect protection (design §27): no damage, actions, interactions or objective effects for the duration.</summary>
        public void GrantReconnectProtection(int playerId)
        {
            var p = this.FindPlayer(playerId) ?? throw new ArgumentException($"Unknown player {playerId}.");
            p.ProtectedUntilTick = this.State.Tick + SimTime.Ticks(this.Balance.Reconnect.ProtectionSeconds);
            p.Action = null;
        }
    }
}
