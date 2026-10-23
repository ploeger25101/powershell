using System;

namespace ProjectAbyss.Core.Randomness
{
    /// <summary>
    /// PCG32 (XSH-RR) random generator with fully serializable state, so runs, arena generation and loot
    /// are reproducible from a seed and can be resumed exactly after crash recovery.
    /// </summary>
    public sealed class DeterministicRng
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private ulong state;
        private readonly ulong increment;

        public DeterministicRng(ulong seed, ulong stream = 54)
        {
            this.increment = (stream << 1) | 1UL;
            this.state = 0;
            this.NextUInt();
            this.state += seed;
            this.NextUInt();
        }

        private DeterministicRng(RngState saved)
        {
            this.state = saved.State;
            this.increment = saved.Increment | 1UL;
        }

        public static DeterministicRng FromState(RngState saved) => new(saved);

        public RngState SaveState() => new(this.state, this.increment);

        public uint NextUInt()
        {
            ulong old = this.state;
            this.state = unchecked(old * Multiplier + this.increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive) without modulo bias.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range must not be empty.");
            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = (uint)(-(int)range) % range;
            while (true)
            {
                uint r = this.NextUInt();
                if (r >= threshold)
                    return minInclusive + (int)(r % range);
            }
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (this.NextUInt() >> 8) * (1f / 16777216f);

        public float NextFloat(float min, float max) => min + (max - min) * this.NextFloat();

        public bool Chance(float probability) => this.NextFloat() < probability;

        /// <summary>Derive an independent generator, e.g. one per arena or per loot roll stream.</summary>
        public DeterministicRng Fork(ulong streamId)
        {
            ulong seed = ((ulong)this.NextUInt() << 32) | this.NextUInt();
            return new DeterministicRng(seed, streamId);
        }

        public static ulong Mix(ulong a, ulong b)
        {
            // SplitMix64 finaliser over a combined value, used to derive per-level seeds.
            ulong z = unchecked(a + 0x9E3779B97F4A7C15UL * (b + 1));
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }

    public readonly record struct RngState(ulong State, ulong Increment);
}
