using System;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Attack = 1 << 0,
        Special = 1 << 1,
        Ability1 = 1 << 2,
        Ability2 = 1 << 3,
        Ultimate = 1 << 4,
        Dash = 1 << 5,
        Potion = 1 << 6,
        Interact = 1 << 7,
    }

    /// <summary>
    /// One tick of player intent. This is the only thing a client may send for combat: the host decides
    /// what actually happens. Movement and aim are quantised so the network form is compact.
    /// </summary>
    public readonly struct PlayerInput
    {
        public readonly uint Sequence;
        public readonly sbyte MoveX;
        public readonly sbyte MoveY;
        public readonly ushort AimQ;
        /// <summary>Buttons currently held.</summary>
        public readonly InputButtons Held;
        /// <summary>Buttons that went down since the previous input (edge-triggered actions).</summary>
        public readonly InputButtons Pressed;

        public PlayerInput(uint sequence, sbyte moveX, sbyte moveY, ushort aimQ, InputButtons held, InputButtons pressed)
        {
            this.Sequence = sequence;
            this.MoveX = moveX;
            this.MoveY = moveY;
            this.AimQ = aimQ;
            this.Held = held;
            this.Pressed = pressed;
        }

        public static PlayerInput Create(uint sequence, Vec2 move, float aimRadians, InputButtons held, InputButtons pressed)
        {
            Vec2 m = move.ClampLength(1f);
            return new PlayerInput(sequence, (sbyte)MathF.Round(m.X * 127f), (sbyte)MathF.Round(m.Y * 127f), QuantizeAngle(aimRadians), held, pressed);
        }

        public Vec2 Move
        {
            get
            {
                var v = new Vec2(this.MoveX / 127f, this.MoveY / 127f);
                return v.ClampLength(1f);
            }
        }

        public float AimRadians => this.AimQ / 65536f * 2f * MathF.PI;

        public static ushort QuantizeAngle(float radians)
        {
            float twoPi = 2f * MathF.PI;
            float a = radians % twoPi;
            if (a < 0)
                a += twoPi;
            return (ushort)((int)MathF.Round(a / twoPi * 65536f) & 0xFFFF);
        }
    }
}
