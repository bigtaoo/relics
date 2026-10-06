using System;
using System.Collections.Generic;

namespace Automatic.Battle.Math
{
    /// <summary>
    /// Deterministic PRNG: full-period 32-bit LCG (Numerical Recipes constants) with the
    /// MurmurHash3 finalizer applied to both the seed and every output.
    ///
    /// Ported from daydayup engine/math/prng.ts (engine v78). The mixing exists because a bare LCG
    /// has low bits that cycle with period 2^k and correlated first draws for nearby seeds; there
    /// `shuffle` reached only 15 of the 120 orders of five items. Output is bit-identical to the
    /// TS version (see PrngTests).
    ///
    /// Rules (design/01-sync-architecture.md):
    /// - One instance per concern (combat, targeting, AI, ...); never a global.
    /// - Draw only when the outcome actually depends on it: a conditional draw shifts every later draw.
    /// </summary>
    public sealed class Prng
    {
        private uint _state;

        public Prng(uint seed)
        {
            _state = Mix32(seed);
            if (_state == 0) _state = 1;
        }

        /// <summary>Current state without advancing. For state hashing only.</summary>
        public uint Peek() => _state;

        private uint Next()
        {
            unchecked
            {
                _state = 1664525u * _state + 1013904223u;
            }
            return Mix32(_state);
        }

        /// <summary>Uniform-ish integer in [0, max). max must be positive.</summary>
        public int NextInt(int max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            return (int)(Next() % (uint)max);
        }

        /// <summary>True with probability percent/100. Always draws exactly once.</summary>
        public bool Chance(int percent) => NextInt(100) < percent;

        /// <summary>Index picked proportionally to non-negative integer weights; one draw.</summary>
        public int WeightedIndex(IReadOnlyList<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++) total = checked(total + weights[i]);
            int roll = NextInt(total);
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }

        /// <summary>In-place Fisher-Yates.</summary>
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

        /// <summary>MurmurHash3 32-bit finalizer: a bijection on uint with full avalanche.</summary>
        public static uint Mix32(uint x)
        {
            unchecked
            {
                uint h = x;
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
