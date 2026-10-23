using System;
using System.Linq;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    public sealed partial class CombatWorld
    {
        public const string SpecialCooldownKey = "special";
        public const string UltimateCooldownKey = "ultimate";
        public const string PotionCooldownKey = "potion";

        private void UpdatePlayer(PlayerEntity p, PlayerInput input, bool hasInput)
        {
            int tick = this.State.Tick;
            this.UpdateDashRecharge(p);
            this.UpdateResource(p);

            if (!hasInput)
            {
                // Disconnected or input lost: the character stands still but stays in the world and can be hit.
                input = new PlayerInput(p.LastProcessedInput, 0, 0, PlayerInput.QuantizeAngle(p.Aim), InputButtons.None, InputButtons.None);
            }
            else
            {
                p.LastProcessedInput = input.Sequence;
            }

            if (p.LifeState != LifeState.Alive)
            {
                p.IsMoving = false;
                p.LastHeld = InputButtons.None;
                return;
            }

            bool stunned = p.Statuses.Any(s => s.Type == StatusType.Stun && s.ExpireTick > tick);
            bool isProtected = p.IsProtected(tick);
            InputButtons pressed = input.Pressed;
            Vec2 move = input.Move;
            p.Aim = input.AimRadians;
            p.LastHeld = input.Held;

            // --- Dash: allowed while idle or in the recovery phase of an attack (dash-cancel).
            if (!stunned && (pressed & InputButtons.Dash) != 0 && p.DashCharges > 0 && !p.IsDashing(tick)
                && (p.Action == null || p.Action.PhaseAt(tick) == ActionPhase.Recovery))
            {
                p.Action = null;
                p.DashCharges--;
                var dash = this.Balance.Dash;
                p.DashEndTick = tick + SimTime.Ticks(dash.DurationSeconds);
                p.DashInvulnerableUntilTick = tick + SimTime.Ticks(dash.InvulnerableSeconds);
                p.DashDirection = move.LengthSquared > 0.01f ? move.Normalized() : Vec2.FromAngle(p.Aim);
                this.AddResource(p, this.ClassOf(p).Resource.GainOnDash);
                this.EmitAt(SimEventType.Dash, p.Position, p.Id);
            }

            // --- Movement
            var startPos = p.Position;
            if (p.IsDashing(tick))
            {
                var dash = this.Balance.Dash;
                float speed = dash.Distance / Math.Max(SimTime.TickSeconds, dash.DurationSeconds);
                p.Position = this.State.Arena.MoveCircle(p.Position, p.DashDirection * speed * SimTime.TickSeconds, p.Radius);
            }
            else if (!stunned)
            {
                float speed = p.MoveSpeed * this.PlayerMoveMultiplier(p);
                if (p.Action != null && p.Action.PhaseAt(tick) != ActionPhase.Recovery)
                    speed *= this.Attacks.Get(p.Action.AttackKey).MoveSpeedMultiplier;
                p.Position = this.State.Arena.MoveCircle(p.Position, move * speed * SimTime.TickSeconds, p.Radius);
            }
            p.IsMoving = Vec2.DistanceSquared(startPos, p.Position) > 0.0001f;
            p.Facing = Direction8Util.FromAngle(p.Action?.Aim ?? p.Aim);

            // --- Actions (blocked during reconnect protection, dashes and stuns)
            if (!stunned && !isProtected && !p.IsDashing(tick) && p.Action == null)
                this.TryStartPlayerAction(p, input);

            if (!stunned && !isProtected && (pressed & InputButtons.Potion) != 0)
                this.TryUsePotion(p);

            this.UpdatePlayerAction(p);
        }

        private float PlayerMoveMultiplier(PlayerEntity p)
        {
            float m = StatusRules.SlowMultiplier(p.Statuses, this.State.Tick);
            foreach (var b in p.Buffs)
                m *= b.MoveSpeedMultiplier;
            return m;
        }

        public ClassDef ClassOf(PlayerEntity p) => this.Data.Classes[p.ClassId];

        private void TryStartPlayerAction(PlayerEntity p, PlayerInput input)
        {
            var pressed = input.Pressed;
            var held = input.Held;
            if ((pressed & InputButtons.Ultimate) != 0 && !string.IsNullOrEmpty(p.Ultimate)
                && this.TryStartAbility(p, p.Ultimate, ActionKind.Ultimate, UltimateCooldownKey))
                return;
            if ((pressed & InputButtons.Ability1) != 0 && p.EquippedAbilities.Count > 0
                && this.TryStartAbility(p, p.EquippedAbilities[0], ActionKind.Ability, p.EquippedAbilities[0]))
                return;
            if ((pressed & InputButtons.Ability2) != 0 && p.EquippedAbilities.Count > 1
                && this.TryStartAbility(p, p.EquippedAbilities[1], ActionKind.Ability, p.EquippedAbilities[1]))
                return;
            var weapon = this.Data.WeaponTypes[p.WeaponTypeId];
            if ((held & InputButtons.Special) != 0 && this.IsReady(p, SpecialCooldownKey))
            {
                this.SetCooldown(p, SpecialCooldownKey, weapon.SpecialCooldownSeconds);
                this.BeginPlayerAction(p, AttackRegistry.SpecialKey(weapon.Id), ActionKind.Special);
                return;
            }
            if ((held & InputButtons.Attack) != 0)
                this.BeginPlayerAction(p, AttackRegistry.BasicKey(weapon.Id), ActionKind.Basic);
        }

        private bool TryStartAbility(PlayerEntity p, string abilityId, ActionKind kind, string cooldownKey)
        {
            if (!this.Data.Abilities.TryGetValue(abilityId, out var ability) || ability.ClassId != p.ClassId)
                return false;
            if (!this.IsReady(p, cooldownKey) || p.Resource < ability.ResourceCost)
                return false;
            p.Resource -= ability.ResourceCost;
            this.SetCooldown(p, cooldownKey, ability.CooldownSeconds);
            this.BeginPlayerAction(p, AttackRegistry.AbilityKey(abilityId), kind);
            return true;
        }

        public bool IsReady(PlayerEntity p, string cooldownKey) =>
            !p.CooldownReadyTick.TryGetValue(cooldownKey, out int ready) || this.State.Tick >= ready;

        private void SetCooldown(PlayerEntity p, string key, float seconds) => p.CooldownReadyTick[key] = this.State.Tick + SimTime.Ticks(seconds);

        private void BeginPlayerAction(PlayerEntity p, string attackKey, ActionKind kind)
        {
            var atk = this.Attacks.Get(attackKey);
            float buffMult = 1f;
            foreach (var b in p.Buffs)
                buffMult *= b.DamageMultiplier;
            p.Action = new ActiveAction
            {
                AttackKey = attackKey,
                Kind = kind,
                StartTick = this.State.Tick,
                WindupTicks = SimTime.Ticks(atk.WindupSeconds),
                ActiveTicks = Math.Max(1, SimTime.Ticks(atk.ActiveSeconds)),
                RecoveryTicks = SimTime.Ticks(atk.RecoverySeconds),
                Aim = p.Aim,
                DamageMultiplier = p.DamageMultiplier * buffMult,
            };
            this.EmitAt(SimEventType.ActionStarted, p.Position, p.Id, key: attackKey, amount: p.Aim);
        }

        private void UpdatePlayerAction(PlayerEntity p)
        {
            var action = p.Action;
            if (action == null)
                return;
            int tick = this.State.Tick;
            var phase = action.PhaseAt(tick);
            if (phase == ActionPhase.Done)
            {
                p.Action = null;
                return;
            }
            if (phase != ActionPhase.Active)
                return;

            var atk = this.Attacks.Get(action.AttackKey);
            if (!action.ActivationHandled)
            {
                action.ActivationHandled = true;
                if (atk.Shape == HitShape.Projectile)
                    this.SpawnProjectiles(p.Id, true, action.AttackKey, atk, p.Position, action.Aim, action.DamageMultiplier);
                if (action.Kind is ActionKind.Ability or ActionKind.Ultimate)
                {
                    var ability = this.Data.Abilities[action.AttackKey.Substring(2)];
                    if (ability.SelfBuff != null)
                        this.ApplyBuff(p, ability.SelfBuff);
                }
            }
            if (atk.LungeDistance > 0)
            {
                float perTick = atk.LungeDistance / action.ActiveTicks;
                p.Position = this.State.Arena.MoveCircle(p.Position, Vec2.FromAngle(action.Aim) * perTick, p.Radius);
            }
            if (atk.Shape is HitShape.Projectile or HitShape.None)
                return;

            foreach (var enemy in this.State.Enemies)
            {
                if (enemy.Hp <= 0 || action.HitTargets.Count >= atk.MaxTargets || action.HitTargets.Contains(enemy.Id))
                    continue;
                if (!this.AttackHits(atk, p.Position, action.Aim, action.GroundTarget, enemy.Position, enemy.Radius))
                    continue;
                action.HitTargets.Add(enemy.Id);
                this.DamageEnemy(p, enemy, atk, action.AttackKey, action.DamageMultiplier);
            }
        }

        private void ApplyBuff(PlayerEntity caster, BuffDef def)
        {
            var targets = def.AllyRadius > 0
                ? this.State.Players.Where(o => o.LifeState == LifeState.Alive && Vec2.Distance(o.Position, caster.Position) <= def.AllyRadius).ToList()
                : new() { caster };
            foreach (var t in targets)
            {
                t.Buffs.RemoveAll(b => b.BuffId == def.Id);
                t.Buffs.Add(new BuffInstance
                {
                    BuffId = def.Id,
                    ExpireTick = this.State.Tick + SimTime.Ticks(def.DurationSeconds),
                    DamageMultiplier = def.DamageMultiplier,
                    DamageTakenMultiplier = def.DamageTakenMultiplier,
                    MoveSpeedMultiplier = def.MoveSpeedMultiplier,
                    ResourceGainMultiplier = def.ResourceGainMultiplier,
                    Shield = def.Shield,
                });
            }
        }

        private void TryUsePotion(PlayerEntity p)
        {
            if (p.PotionCharges <= 0 || !this.IsReady(p, PotionCooldownKey) || p.Hp >= p.MaxHp)
                return;
            p.PotionCharges--;
            this.SetCooldown(p, PotionCooldownKey, this.Balance.Potion.CooldownSeconds);
            float heal = p.MaxHp * this.Balance.Potion.HealFraction;
            p.Hp = Math.Min(p.MaxHp, p.Hp + heal);
            this.EmitAt(SimEventType.PotionUsed, p.Position, p.Id, amount: heal);
        }

        private void UpdateDashRecharge(PlayerEntity p)
        {
            var dash = this.Balance.Dash;
            if (p.DashCharges >= dash.Charges)
            {
                p.DashRechargeTicks = 0;
                return;
            }
            p.DashRechargeTicks++;
            if (p.DashRechargeTicks >= SimTime.Ticks(dash.RechargeSeconds))
            {
                p.DashCharges++;
                p.DashRechargeTicks = 0;
            }
        }

        private void UpdateResource(PlayerEntity p)
        {
            var def = this.ClassOf(p).Resource;
            if (p.LifeState != LifeState.Alive)
                return;
            float delta = def.RegenPerSecond * SimTime.TickSeconds;
            if (def.DecayPerSecond > 0 && this.State.Tick - p.LastCombatTick > SimTime.Ticks(def.DecayDelaySeconds))
                delta -= def.DecayPerSecond * SimTime.TickSeconds;
            p.Resource = Math.Clamp(p.Resource + delta, 0f, p.ResourceMax);
        }

        private void AddResource(PlayerEntity p, float amount)
        {
            if (amount <= 0)
                return;
            float mult = 1f;
            foreach (var b in p.Buffs)
                mult *= b.ResourceGainMultiplier;
            p.Resource = Math.Min(p.ResourceMax, p.Resource + amount * mult);
        }

        public bool AttackHits(AttackDef atk, Vec2 origin, float aim, Vec2 groundTarget, Vec2 target, float targetRadius)
        {
            switch (atk.Shape)
            {
                case HitShape.Sector:
                    return Geometry.SectorIntersectsCircle(origin, aim, atk.HalfAngleDegrees * MathF.PI / 180f, atk.Range, target, targetRadius);
                case HitShape.Circle:
                    var center = atk.TargetGround ? groundTarget : origin + Vec2.FromAngle(aim) * atk.Offset;
                    return Geometry.CircleIntersectsCircle(center, atk.Range, target, targetRadius);
                case HitShape.Capsule:
                    return Geometry.CapsuleIntersectsCircle(origin, origin + Vec2.FromAngle(aim) * atk.Range, atk.Width / 2f, target, targetRadius);
                default:
                    return false;
            }
        }
    }
}
