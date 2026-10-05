// Solace.Core — deterministic seeded random streams.
// Mulberry32 generator. Integer-only, so the sequence is identical on every
// platform and runtime. Streams are partitioned by domain (world / ai / event)
// so world generation never perturbs AI decisions.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    /// <summary>
    /// Deterministic PRNG (mulberry32). One instance per domain stream.
    /// Exposes its full state so save/load can resume a sequence exactly.
    /// </summary>
    public sealed class SeededRandom
    {
        private uint _state;

        public SeededRandom(uint seed)
        {
            // Zero is a weak state for mulberry32; mix it.
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        /// <summary>Full generator state. Save and restore for exact resume.</summary>
        public uint State
        {
            get { return _state; }
            set { _state = value; }
        }

        public uint NextUInt()
        {
            _state += 0x6D2B79F5u;
            uint t = _state;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + ((t ^ (t >> 7)) * (t | 61u));
            return t ^ (t >> 14);
        }

        /// <summary>Integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException("maxExclusive", "maxExclusive must be > minInclusive");
            uint range = (uint)(maxExclusive - minInclusive);
            // Rejection sampling to avoid modulo bias.
            uint limit = uint.MaxValue - (uint.MaxValue % range);
            uint r;
            do { r = NextUInt(); } while (r >= limit);
            return minInclusive + (int)(r % range);
        }

        public int NextInt(int maxExclusive)
        {
            return NextInt(0, maxExclusive);
        }

        /// <summary>Float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public float NextFloat(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>Double in [0, 1).</summary>
        public double NextDouble()
        {
            ulong hi = NextUInt();
            ulong lo = NextUInt();
            ulong combined = (hi << 32) | lo;
            return (combined >> 11) * (1.0 / 9007199254740992.0);
        }

        public bool NextBool(float probability = 0.5f)
        {
            return NextFloat() < probability;
        }

        /// <summary>Standard-normal sample (Box-Muller). Deterministic.</summary>
        public float NextGaussian()
        {
            double u1 = Math.Max(NextDouble(), 1e-12);
            double u2 = NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                throw new ArgumentException("Cannot pick from an empty list.", "list");
            return list[NextInt(list.Count)];
        }

        /// <summary>In-place Fisher-Yates shuffle. Deterministic for a given state.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// Derives a domain-partitioned stream from a master seed. The same
        /// (masterSeed, domain) always yields the same stream, on any platform.
        /// </summary>
        public static SeededRandom Derive(int masterSeed, string domain)
        {
            uint h = StableHash(domain == null ? "" : domain);
            uint mixed = ((uint)masterSeed * 0x9E3779B1u) ^ (h + 0x9E3779B9u);
            // Avalanche the mix so similar domains diverge fully.
            mixed ^= mixed >> 16;
            mixed *= 0x7feb352du;
            mixed ^= mixed >> 15;
            mixed *= 0x846ca68bu;
            mixed ^= mixed >> 16;
            return new SeededRandom(mixed);
        }

        /// <summary>
        /// Stable FNV-1a 32-bit string hash. Unlike string.GetHashCode, this is
        /// identical across runtimes and platforms — safe for save data.
        /// </summary>
        public static uint StableHash(string s)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint h = offset;
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= prime;
                }
            }
            return h;
        }
    }
}
