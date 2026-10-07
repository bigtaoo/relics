using Automatic.Battle.Math;

namespace Automatic.Battle.Sim
{
    /// <summary>Integer-only helpers the movement needs. Deterministic on every platform.</summary>
    public static class FixedMath
    {
        /// <summary>Square root, truncated. v must not be negative.</summary>
        public static FP Sqrt(FP v)
        {
            if (v.Raw <= 0) return FP.Zero;
            return FP.FromRaw((long)ISqrt((ulong)v.Raw * (ulong)FP.Scale));
        }

        /// <summary>floor(sqrt(n)), bit by bit.</summary>
        public static ulong ISqrt(ulong n)
        {
            ulong root = 0, bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= root + bit)
                {
                    n -= root + bit;
                    root = (root >> 1) + bit;
                }
                else
                {
                    root >>= 1;
                }
                bit >>= 2;
            }
            return root;
        }

        /// <summary>Integer part of a non-negative value.</summary>
        public static int Floor(FP v) => (int)(v.Raw / FP.Scale);
    }
}
