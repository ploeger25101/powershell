using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Profile;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Client.Rendering
{
    /// <summary>
    /// Draws a <see cref="WorldState"/>: tiles, telegraphs, players (eight-direction sprites + weapon rotated to the exact
    /// aim), enemies, bosses and projectiles. Pure presentation: reads state, never changes it.
    /// </summary>
    public sealed class WorldRenderer
    {
        private static readonly Color TelegraphFill = new(200, 30, 30);
        private static readonly Color TelegraphEdge = new(255, 220, 120);
        private readonly AssetLibrary assets;
        private readonly Draw draw;
        private readonly GameData data;
        private readonly AttackRegistry attacks;

        public WorldRenderer(AssetLibrary assets, GameData data)
        {
            this.assets = assets;
            this.draw = new Draw(assets);
            this.data = data;
            this.attacks = new AttackRegistry(data);
        }

        public void Render(SpriteBatch b, WorldState world, Camera cam, int ownPlayerId, Vec2? ownPositionOverride, AccessibilitySettings access, float renderTick)
        {
            this.DrawTiles(b, world.Arena, cam);
            this.DrawTelegraphs(b, world, cam, access);

            var drawables = new List<(float y, Action draw)>();
            foreach (var p in world.Players)
            {
                if (p.LifeState is LifeState.Respawning or LifeState.Eliminated)
                    continue;
                var pos = p.Id == ownPlayerId && ownPositionOverride.HasValue ? ownPositionOverride.Value : p.Position;
                drawables.Add((pos.Y, () => this.DrawPlayer(b, world, p, pos, cam, p.Id == ownPlayerId, access)));
            }
            foreach (var e in world.Enemies.Where(e => e.Hp > 0))
                drawables.Add((e.Position.Y, () => this.DrawEnemy(b, world, e, cam, access)));
            foreach (var (_, d) in drawables.OrderBy(d => d.y))
                d();
            foreach (var pr in world.Projectiles)
            {
                var c = pr.FromPlayer ? new Color(230, 230, 255) : new Color(150, 230, 90);
                var center = cam.ToScreen(pr.Position);
                this.draw.Circle(b, center, Math.Max(2f, pr.Radius * cam.Scale), Color.Black * 0.6f);
                this.draw.Circle(b, center, Math.Max(1.5f, (pr.Radius - 1) * cam.Scale), c);
            }
        }

        private void DrawTiles(SpriteBatch b, ArenaMap arena, Camera cam)
        {
            var tex = this.assets.Get("assets/tiles/forest.png");
            var meta = this.assets.Meta.Tiles.TryGetValue("forest", out var m) ? m : new TileSheetMeta();
            int ts = meta.TileSize;
            int Index(string name) => Math.Max(0, meta.Order.IndexOf(name));
            var exits = new HashSet<TilePoint>(arena.Exits);
            for (int y = 0; y < arena.Height; y++)
            {
                for (int x = 0; x < arena.Width; x++)
                {
                    var t = arena.Get(x, y);
                    int idx = t switch
                    {
                        TileType.Wall => Index("wall"),
                        TileType.Obstacle => Index("obstacle"),
                        TileType.Hazard => Index("hazard"),
                        _ => exits.Contains(new TilePoint(x, y)) ? Index("exit") : Index("floor" + ((x * 7 + y * 13) % 4)),
                    };
                    var pos = cam.ToScreen(new Vec2(x * ts, y * ts));
                    b.Draw(tex, new Rectangle((int)pos.X, (int)pos.Y, ts * cam.Scale, ts * cam.Scale), new Rectangle(idx * ts, 0, ts, ts), Color.White);
                }
            }
        }

        /// <summary>Windup telegraphs for enemy attacks: shape in red with a bright dotted edge and an icon (readable without colour).</summary>
        private void DrawTelegraphs(SpriteBatch b, WorldState world, Camera cam, AccessibilitySettings access)
        {
            foreach (var e in world.Enemies)
            {
                var a = e.Action;
                if (a == null || e.Hp <= 0 || a.PhaseAt(world.Tick) != ActionPhase.Windup || !this.attacks.TryGet(a.AttackKey, out var atk) || !atk.Telegraphed)
                    continue;
                float progress = a.WindupTicks <= 0 ? 1f : Math.Clamp((world.Tick - a.StartTick) / (float)a.WindupTicks, 0f, 1f);
                float alpha = access.HighContrastTelegraphs ? 0.55f : 0.2f + 0.3f * progress;
                var fill = (access.HighContrastTelegraphs ? Color.OrangeRed : TelegraphFill) * alpha;
                var edge = TelegraphEdge * (0.6f + 0.4f * progress);
                var origin = cam.ToScreen(e.Position);
                float screenAngle = -a.Aim;
                float s = cam.Scale;
                switch (atk.Shape)
                {
                    case HitShape.Sector:
                        this.draw.Sector(b, origin, screenAngle, atk.HalfAngleDegrees * MathF.PI / 180f, atk.Range * s, fill, edge);
                        break;
                    case HitShape.Circle:
                    {
                        var c = atk.TargetGround ? cam.ToScreen(a.GroundTarget) : cam.ToScreen(e.Position + Vec2.FromAngle(a.Aim) * atk.Offset);
                        this.draw.Circle(b, c, atk.Range * s, fill);
                        this.draw.Ring(b, c, atk.Range * s, edge, 2f);
                        this.draw.Circle(b, c, atk.Range * s * progress, fill * 0.6f);
                        break;
                    }
                    case HitShape.Capsule:
                    {
                        float len = (atk.Range + atk.LungeDistance) * s;
                        var dir = new Vector2(MathF.Cos(screenAngle), MathF.Sin(screenAngle));
                        this.draw.Line(b, origin, origin + dir * len, fill, atk.Width * s);
                        var normal = new Vector2(-dir.Y, dir.X) * (atk.Width * s / 2f);
                        this.draw.Line(b, origin + normal, origin + normal + dir * len, edge, 2f);
                        this.draw.Line(b, origin - normal, origin - normal + dir * len, edge, 2f);
                        break;
                    }
                    case HitShape.Projectile:
                    {
                        var pdef = atk.Projectile!;
                        int count = Math.Max(1, pdef.Count);
                        float len = Math.Min(160f, pdef.Speed * 0.5f) * s;
                        for (int i = 0; i < count; i++)
                        {
                            float ang = a.Aim;
                            if (count > 1)
                            {
                                ang = pdef.SpreadDegrees >= 360f ? a.Aim + i * (2 * MathF.PI / count)
                                    : a.Aim - pdef.SpreadDegrees * MathF.PI / 360f + pdef.SpreadDegrees * MathF.PI / 180f * i / (count - 1);
                            }
                            this.draw.Line(b, origin, origin + new Vector2(MathF.Cos(-ang), MathF.Sin(-ang)) * len, fill, 2f * s / 2f);
                        }
                        break;
                    }
                }
                // shape-independent warning icon above the attacker
                var icon = origin + new Vector2(0, -(e.Radius * 2 + 14) * s);
                this.draw.Rect(b, new Rectangle((int)icon.X - 2 * (int)s, (int)icon.Y - 6 * (int)s, 4 * (int)s, 8 * (int)s), TelegraphEdge);
                this.draw.Rect(b, new Rectangle((int)icon.X - 2 * (int)s, (int)icon.Y + 4 * (int)s, 4 * (int)s, 3 * (int)s), TelegraphEdge);
            }
        }

        private static string PlayerAnimation(WorldState w, PlayerEntity p, out int startTick)
        {
            startTick = 0;
            if (p.LifeState == LifeState.Downed)
                return "downed";
            if (w.Tick < p.DashEndTick)
            {
                startTick = p.DashEndTick - 14;
                return "dash";
            }
            if (p.Action != null)
            {
                startTick = p.Action.StartTick;
                return p.Action.Kind switch
                {
                    ActionKind.Special => "special",
                    ActionKind.Ability => "ability",
                    ActionKind.Ultimate => "ultimate",
                    _ => "attack",
                };
            }
            if (w.Tick - p.LastHitTick < 12)
            {
                startTick = p.LastHitTick;
                return "hit";
            }
            return p.IsMoving ? "walk" : "idle";
        }

        private void DrawPlayer(SpriteBatch b, WorldState w, PlayerEntity p, Vec2 pos, Camera cam, bool own, AccessibilitySettings access)
        {
            if (!this.assets.Meta.Players.TryGetValue(p.ClassId, out var meta))
                return;
            var tex = this.assets.Get($"assets/characters/{p.ClassId}.png");
            string animName = PlayerAnimation(w, p, out int start);
            if (!meta.Animations.TryGetValue(animName, out var anim))
                anim = meta.Animations["idle"];
            int elapsed = Math.Max(0, w.Tick - start);
            int frame = elapsed * anim.Fps / SimTime.TickRate;
            frame = anim.Loop ? frame % anim.Frames : Math.Min(frame, anim.Frames - 1);
            int dir = (int)p.Facing;
            var src = new Rectangle((dir * meta.FramesPerRowPerDirection + frame) * meta.FrameWidth, anim.Row * meta.FrameHeight, meta.FrameWidth, meta.FrameHeight);
            var screen = cam.ToScreen(pos);
            var dest = new Rectangle((int)(screen.X - meta.OriginX * cam.Scale), (int)(screen.Y - meta.OriginY * cam.Scale), meta.FrameWidth * cam.Scale, meta.FrameHeight * cam.Scale);
            var tint = Color.White;
            if (p.IsProtected(w.Tick) || w.Tick < p.HitInvulnerableUntilTick)
                tint *= (w.Tick / 4) % 2 == 0 ? 0.55f : 0.9f;
            if (!p.Connected)
                tint = new Color(120, 120, 140) * 0.7f;
            bool weaponBehind = p.Facing is Direction8.Up or Direction8.UpLeft or Direction8.UpRight;
            if (weaponBehind)
                this.DrawWeapon(b, w, p, screen, cam);
            b.Draw(tex, dest, src, tint);
            if (!weaponBehind && p.LifeState == LifeState.Alive)
                this.DrawWeapon(b, w, p, screen, cam);

            // own marker + downed revive progress
            if (own)
                this.draw.Ring(b, screen, 9 * cam.Scale, new Color(120, 200, 255) * 0.5f, 2f);
            if (p.LifeState == LifeState.Downed)
            {
                float revive = p.ReviveProgressTicks / (float)SimTime.Ticks(this.data.Balance.Lives.ReviveChannelSeconds);
                this.draw.Ring(b, screen + new Vector2(0, -40 * cam.Scale), 8 * cam.Scale, Color.Gray, 3f);
                this.draw.Ring(b, screen + new Vector2(0, -40 * cam.Scale), 8 * cam.Scale, Color.LightGreen, 3f, -MathF.PI / 2, MathF.PI * 2 * revive);
            }
        }

        /// <summary>The weapon is rotated to the exact aim angle (not snapped to eight directions) with a swing offset per phase.</summary>
        private void DrawWeapon(SpriteBatch b, WorldState w, PlayerEntity p, Vector2 screen, Camera cam)
        {
            var wm = this.assets.Meta.Weapons;
            int index = wm.Order.IndexOf(p.WeaponTypeId);
            if (index < 0)
                return;
            var tex = this.assets.Get("assets/characters/weapons.png");
            float aim = p.Action?.Aim ?? p.Aim;
            float swing = 0f;
            if (p.Action != null && this.attacks.TryGet(p.Action.AttackKey, out var atk))
            {
                var a = p.Action;
                int t = w.Tick - a.StartTick;
                float arc = atk.Shape == HitShape.Sector ? atk.HalfAngleDegrees * MathF.PI / 180f : 0.6f;
                if (atk.Shape == HitShape.Circle && atk.Offset <= 0)
                    swing = a.PhaseAt(w.Tick) == ActionPhase.Active ? (t - a.WindupTicks) / (float)Math.Max(1, a.ActiveTicks) * MathF.PI * 2f : 0f;
                else if (atk.Parry)
                    swing = MathF.PI / 2f;
                else
                    swing = a.PhaseAt(w.Tick) switch
                    {
                        ActionPhase.Windup => arc * (t / (float)Math.Max(1, a.WindupTicks)),
                        ActionPhase.Active => arc - 2 * arc * ((t - a.WindupTicks) / (float)Math.Max(1, a.ActiveTicks)),
                        _ => -arc,
                    };
            }
            float screenAngle = -(aim + swing);
            var hand = screen + new Vector2(0, -22 * cam.Scale) + new Vector2(MathF.Cos(-aim), MathF.Sin(-aim)) * 5 * cam.Scale;
            b.Draw(tex, hand, new Rectangle(index * wm.FrameWidth, 0, wm.FrameWidth, wm.FrameHeight), Color.White, screenAngle,
                new Vector2(wm.GripX, wm.GripY), cam.Scale, SpriteEffects.None, 0f);
        }

        private void DrawEnemy(SpriteBatch b, WorldState w, EnemyEntity e, Camera cam, AccessibilitySettings access)
        {
            var sheets = e.IsBoss ? this.assets.Meta.Bosses : this.assets.Meta.Enemies;
            var screen = cam.ToScreen(e.Position);
            if (!sheets.TryGetValue(e.DefId, out var meta))
            {
                this.draw.Circle(b, screen, e.Radius * cam.Scale, Color.Purple);
                return;
            }
            var tex = this.assets.Get(e.IsBoss ? $"assets/bosses/{e.DefId}.png" : $"assets/enemies/{e.DefId}.png");
            int frame;
            if (e.Action != null)
                frame = meta.WalkFrames + Math.Min(meta.AttackFrames - 1, e.Action.PhaseAt(w.Tick) == ActionPhase.Windup ? 0 : 1);
            else
                frame = e.IsMoving ? (w.Tick / 8 + e.Id) % meta.WalkFrames : 0;
            bool faceLeft = MathF.Cos(e.Aim) < 0;
            var fx = faceLeft == meta.FaceRight ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            float scale = cam.Scale * (e.IsElite ? 1.2f : 1f);
            var dest = new Rectangle((int)(screen.X - meta.OriginX * scale), (int)(screen.Y - meta.OriginY * scale), (int)(meta.FrameWidth * scale), (int)(meta.FrameHeight * scale));
            var src = new Rectangle(frame * meta.FrameWidth, 0, meta.FrameWidth, meta.FrameHeight);
            var tint = Color.White;
            if (w.Tick - e.LastHitTick < 6 && !access.ReducedFlashes)
                tint = new Color(255, 180, 180);
            if (w.Tick < e.StunnedUntilTick || (e.Boss != null && w.Tick < e.Boss.StaggeredUntilTick))
                tint = Color.LightSkyBlue;
            if (e.IsElite)
            {
                foreach (var off in new[] { new Point(-2, 0), new Point(2, 0), new Point(0, -2), new Point(0, 2) })
                    b.Draw(tex, new Rectangle(dest.X + off.X, dest.Y + off.Y, dest.Width, dest.Height), src, new Color(230, 190, 60), 0f, Vector2.Zero, fx, 0f);
            }
            b.Draw(tex, dest, src, tint, 0f, Vector2.Zero, fx, 0f);
            if (!e.IsBoss && e.Hp < e.MaxHp)
            {
                var bar = new Rectangle((int)screen.X - 12 * cam.Scale / 2, dest.Y - 4 * cam.Scale, 12 * cam.Scale, Math.Max(3, cam.Scale));
                this.draw.Bar(b, bar, e.Hp / e.MaxHp, new Color(200, 40, 40), Color.Black * 0.7f, Color.Black);
            }
        }
    }
}
