using System;

namespace ProjectAbyss.Core.Simulation
{
    /// <summary>The simulation runs at a fixed tick rate independent of rendering frame rate.</summary>
    public static class SimTime
    {
        public const int TickRate = 60;
        public const float TickSeconds = 1f / TickRate;

        public static int Ticks(float seconds) => seconds <= 0 ? 0 : Math.Max(1, (int)MathF.Round(seconds * TickRate));
        public static float Seconds(int ticks) => ticks * TickSeconds;
    }
}
