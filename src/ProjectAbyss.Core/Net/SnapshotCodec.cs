using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Net
{
    /// <summary>
    /// Compact, quantised per-tick state for clients (≈1 KB with 32 enemies). Static data (names, classes, arena) is
    /// only sent in the full state on join/reconnect.
    /// </summary>
    public sealed class SnapshotCodec
    {
        public const int MaxEntities = 512;
        private readonly ContentIndex index;

        public SnapshotCodec(ContentIndex index)
        {
            this.index = index;
        }

        public byte[] Encode(WorldState s, uint ackInputSeq, IReadOnlyList<SimEvent> events)
        {
            var w = Protocol.Begin(MessageType.Snapshot).I32(s.Tick).U32(ackInputSeq);
            var enc = s.Encounter;
            w.U8((byte)enc.Status).U8((byte)enc.Kind).I32(enc.WaveIndex).I32(enc.SurvivalEndTick).I32(enc.BossEntityId).Bool(s.PactActive);

            w.U16((ushort)s.Players.Count);
            foreach (var p in s.Players)
            {
                w.I32(p.Id).Pos(p.Position.X).Pos(p.Position.Y).U16(PlayerInput.QuantizeAngle(p.Aim)).U8((byte)p.Facing).Bool(p.IsMoving)
                    .F32(p.Hp).F32(p.MaxHp).U8((byte)p.Lives).U8((byte)p.LifeState).I32(p.LifeStateUntilTick).I32(p.ReviveProgressTicks)
                    .U8((byte)p.DashCharges).I32(p.DashRechargeTicks).I32(p.DashEndTick).I32(p.DashInvulnerableUntilTick)
                    .F32(p.DashDirection.X).F32(p.DashDirection.Y).I32(p.HitInvulnerableUntilTick).I32(p.ProtectedUntilTick)
                    .U8((byte)p.PotionCharges).F32(p.Resource).F32(p.ResourceMax).Bool(p.Connected).I32(p.LastHitTick).U32(p.LastProcessedInput);
                this.WriteAction(w, p.Action);
                w.U16((ushort)p.CooldownReadyTick.Count);
                foreach (var cd in p.CooldownReadyTick)
                    w.U16(this.index.Id(cd.Key)).I32(cd.Value);
                WriteStatuses(w, p.Statuses);
                w.U16((ushort)p.Buffs.Count);
                foreach (var b in p.Buffs)
                    w.U16(this.index.Id(b.BuffId)).I32(b.ExpireTick).F32(b.Shield);
            }

            w.U16((ushort)s.Enemies.Count);
            foreach (var e in s.Enemies)
            {
                w.I32(e.Id).U16(this.index.Id(e.DefId)).Bool(e.IsBoss).Bool(e.IsElite).Pos(e.Position.X).Pos(e.Position.Y)
                    .U16(PlayerInput.QuantizeAngle(e.Aim)).U8((byte)e.Facing).Bool(e.IsMoving).F32(e.Hp).F32(e.MaxHp).F32(e.Radius)
                    .U8((byte)e.State).I32(e.TargetId).I32(e.StunnedUntilTick).I32(e.LastHitTick);
                this.WriteAction(w, e.Action);
                WriteStatuses(w, e.Statuses);
                w.Bool(e.Boss != null);
                if (e.Boss != null)
                    w.U8((byte)e.Boss.PhaseIndex).F32(e.Boss.Stagger).I32(e.Boss.StaggeredUntilTick).I32(e.Boss.StaggerImmuneUntilTick).Bool(e.Boss.PactActive).U8((byte)e.Boss.ActivePlayerCount);
            }

            w.U16((ushort)s.Projectiles.Count);
            foreach (var pr in s.Projectiles)
            {
                w.I32(pr.Id).I32(pr.OwnerId).Bool(pr.FromPlayer).U16(this.index.Id(pr.AttackKey)).Pos(pr.Position.X).Pos(pr.Position.Y)
                    .F32(pr.Velocity.X).F32(pr.Velocity.Y).F32(pr.Radius).I32(pr.ExpireTick);
            }

            w.U16((ushort)events.Count);
            foreach (var ev in events)
            {
                w.U8((byte)ev.Type).I32(ev.Tick).I32(ev.SourceId).I32(ev.TargetId).F32(ev.Amount).Bool(ev.Flag).Str(ev.Key)
                    .Pos(ev.Position.X).Pos(ev.Position.Y).I32(ev.IntValue).I32(ev.IntValue2);
            }
            return w.ToArray();
        }

        private void WriteAction(NetWriter w, ActiveAction? a)
        {
            w.Bool(a != null);
            if (a == null)
                return;
            w.U16(this.index.Id(a.AttackKey)).U8((byte)a.Kind).I32(a.StartTick).I32(a.WindupTicks).I32(a.ActiveTicks).I32(a.RecoveryTicks)
                .U16(PlayerInput.QuantizeAngle(a.Aim)).Pos(a.GroundTarget.X).Pos(a.GroundTarget.Y);
        }

        private ActiveAction? ReadAction(NetReader r)
        {
            if (!r.Bool())
                return null;
            return new ActiveAction
            {
                AttackKey = this.index.Key(r.U16()),
                Kind = (ActionKind)r.U8(),
                StartTick = r.I32(),
                WindupTicks = r.I32(),
                ActiveTicks = r.I32(),
                RecoveryTicks = r.I32(),
                Aim = r.U16() / 65536f * 2f * MathF.PI,
                GroundTarget = new Vec2(r.Pos(), r.Pos()),
                // hits are resolved by the host only; the replica never applies damage
                ActivationHandled = true,
            };
        }

        private static void WriteStatuses(NetWriter w, List<StatusInstance> statuses)
        {
            w.U16((ushort)statuses.Count);
            foreach (var st in statuses)
                w.U8((byte)st.Type).I32(st.ExpireTick).U8((byte)st.Stacks);
        }

        private static List<StatusInstance> ReadStatuses(NetReader r)
        {
            int n = r.Count(16);
            var list = new List<StatusInstance>(n);
            for (int i = 0; i < n; i++)
                list.Add(new StatusInstance { Type = (StatusType)r.U8(), ExpireTick = r.I32(), Stacks = r.U8() });
            return list;
        }

        public sealed class Decoded
        {
            public int Tick { get; set; }
            public uint AckInputSeq { get; set; }
            public List<SimEvent> Events { get; } = new();
            public bool MissingStaticData { get; set; }
        }

        /// <summary>Overwrite the client replica with the host's state. Unknown player ids mean a full state is needed.</summary>
        public Decoded Apply(byte[] data, WorldState replica)
        {
            var r = Protocol.Open(data, MessageType.Snapshot);
            var result = new Decoded { Tick = r.I32(), AckInputSeq = r.U32() };
            replica.Tick = result.Tick;
            var enc = replica.Encounter;
            enc.Status = (EncounterStatus)r.U8();
            enc.Kind = (EncounterKind)r.U8();
            enc.WaveIndex = r.I32();
            enc.SurvivalEndTick = r.I32();
            enc.BossEntityId = r.I32();
            replica.PactActive = r.Bool();

            int pc = r.Count(Protocol.MaxPlayers);
            for (int i = 0; i < pc; i++)
            {
                int id = r.I32();
                var p = replica.Players.Find(x => x.Id == id);
                if (p == null)
                {
                    result.MissingStaticData = true;
                    p = new PlayerEntity { Id = id }; // read into a throwaway entity to keep the stream aligned
                }
                p.Position = new Vec2(r.Pos(), r.Pos());
                p.Aim = r.U16() / 65536f * 2f * MathF.PI;
                p.Facing = (Direction8)(r.U8() & 7);
                p.IsMoving = r.Bool();
                p.Hp = r.F32();
                p.MaxHp = r.F32();
                p.Lives = r.U8();
                p.LifeState = (LifeState)r.U8();
                p.LifeStateUntilTick = r.I32();
                p.ReviveProgressTicks = r.I32();
                p.DashCharges = r.U8();
                p.DashRechargeTicks = r.I32();
                p.DashEndTick = r.I32();
                p.DashInvulnerableUntilTick = r.I32();
                p.DashDirection = new Vec2(r.F32(), r.F32());
                p.HitInvulnerableUntilTick = r.I32();
                p.ProtectedUntilTick = r.I32();
                p.PotionCharges = r.U8();
                p.Resource = r.F32();
                p.ResourceMax = r.F32();
                p.Connected = r.Bool();
                p.LastHitTick = r.I32();
                p.LastProcessedInput = r.U32();
                p.Action = this.ReadAction(r);
                p.CooldownReadyTick.Clear();
                int cds = r.Count(32);
                for (int c = 0; c < cds; c++)
                    p.CooldownReadyTick[this.index.Key(r.U16())] = r.I32();
                p.Statuses = ReadStatuses(r);
                p.Buffs.Clear();
                int buffs = r.Count(16);
                for (int b = 0; b < buffs; b++)
                    p.Buffs.Add(new BuffInstance { BuffId = this.index.Key(r.U16()), ExpireTick = r.I32(), Shield = r.F32() });
            }

            int ec = r.Count(MaxEntities);
            var seen = new HashSet<int>();
            var enemies = new List<EnemyEntity>(ec);
            for (int i = 0; i < ec; i++)
            {
                int id = r.I32();
                var e = replica.Enemies.Find(x => x.Id == id) ?? new EnemyEntity { Id = id };
                e.DefId = this.index.Key(r.U16());
                e.IsBoss = r.Bool();
                e.IsElite = r.Bool();
                e.Position = new Vec2(r.Pos(), r.Pos());
                e.Aim = r.U16() / 65536f * 2f * MathF.PI;
                e.Facing = (Direction8)(r.U8() & 7);
                e.IsMoving = r.Bool();
                e.Hp = r.F32();
                e.MaxHp = r.F32();
                e.Radius = r.F32();
                e.State = (AiState)r.U8();
                e.TargetId = r.I32();
                e.StunnedUntilTick = r.I32();
                e.LastHitTick = r.I32();
                e.Action = this.ReadAction(r);
                e.Statuses = ReadStatuses(r);
                if (r.Bool())
                {
                    e.Boss ??= new BossState();
                    e.Boss.PhaseIndex = r.U8();
                    e.Boss.Stagger = r.F32();
                    e.Boss.StaggeredUntilTick = r.I32();
                    e.Boss.StaggerImmuneUntilTick = r.I32();
                    e.Boss.PactActive = r.Bool();
                    e.Boss.ActivePlayerCount = r.U8();
                }
                if (seen.Add(id))
                    enemies.Add(e);
            }
            replica.Enemies = enemies;

            int prc = r.Count(MaxEntities);
            var projectiles = new List<ProjectileEntity>(prc);
            for (int i = 0; i < prc; i++)
            {
                projectiles.Add(new ProjectileEntity
                {
                    Id = r.I32(),
                    OwnerId = r.I32(),
                    FromPlayer = r.Bool(),
                    AttackKey = this.index.Key(r.U16()),
                    Position = new Vec2(r.Pos(), r.Pos()),
                    Velocity = new Vec2(r.F32(), r.F32()),
                    Radius = r.F32(),
                    ExpireTick = r.I32(),
                });
            }
            replica.Projectiles = projectiles;

            int evc = r.Count(4096);
            for (int i = 0; i < evc; i++)
            {
                result.Events.Add(new SimEvent
                {
                    Type = (SimEventType)r.U8(),
                    Tick = r.I32(),
                    SourceId = r.I32(),
                    TargetId = r.I32(),
                    Amount = r.F32(),
                    Flag = r.Bool(),
                    Key = r.Str(128),
                    Position = new Vec2(r.Pos(), r.Pos()),
                    IntValue = r.I32(),
                    IntValue2 = r.I32(),
                });
            }
            return result;
        }
    }
}
