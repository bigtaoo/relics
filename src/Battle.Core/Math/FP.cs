#nullable enable
using System;

namespace Automatic.Battle.Math
{
    /// <summary>
    /// Fixed-point number, scale 10000 (four decimal places), backed by a 64-bit integer.
    ///
    /// Design rules (design/DECISIONS.md ADR-002):
    /// - No implicit conversion from or to int/long/float/double. Every value enters through a
    ///   named factory, so "raw vs scaled" confusion (funny ADR-065, 1000x off bugs) cannot compile.
    /// - No float/double anywhere, not even for debugging output.
    /// - Multiplication and division truncate toward zero. All arithmetic is checked: overflow
    ///   throws instead of silently wrapping, and throws identically on every platform.
    /// - FP * int and FP / int are allowed: an int operand is a dimensionless count, never a
    ///   scaled value.
    /// </summary>
    public readonly struct FP : IEquatable<FP>, IComparable<FP>
    {
        public const long Scale = 10000;

        public static readonly FP Zero = new FP(0);
        public static readonly FP One = new FP(Scale);

        /// <summary>Raw scaled integer. Value = Raw / Scale.</summary>
        public readonly long Raw;

        private FP(long raw)
        {
            Raw = raw;
        }

        public static FP FromRaw(long raw) => new FP(raw);

        public static FP FromInt(long value) => new FP(checked(value * Scale));

        /// <summary>numerator / denominator, truncated toward zero. FromRatio(355, 1000) = 0.355.</summary>
        public static FP FromRatio(long numerator, long denominator)
        {
            if (denominator == 0) throw new DivideByZeroException();
            return new FP(checked(numerator * Scale) / denominator);
        }

        /// <summary>Percent as authored in skill text: FromPercent(125) = 1.25.</summary>
        public static FP FromPercent(long percent) => FromRatio(percent, 100);

        /// <summary>Integer part, truncated toward zero.</summary>
        public long TruncateToLong() => Raw / Scale;

        public static FP operator +(FP a, FP b) => new FP(checked(a.Raw + b.Raw));
        public static FP operator -(FP a, FP b) => new FP(checked(a.Raw - b.Raw));
        public static FP operator -(FP a) => new FP(checked(-a.Raw));
        public static FP operator *(FP a, FP b) => new FP(checked(a.Raw * b.Raw) / Scale);
        public static FP operator *(FP a, int b) => new FP(checked(a.Raw * b));
        public static FP operator *(int a, FP b) => new FP(checked(a * b.Raw));

        public static FP operator /(FP a, FP b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();
            return new FP(checked(a.Raw * Scale) / b.Raw);
        }

        public static FP operator /(FP a, int b)
        {
            if (b == 0) throw new DivideByZeroException();
            return new FP(a.Raw / b);
        }

        public static bool operator ==(FP a, FP b) => a.Raw == b.Raw;
        public static bool operator !=(FP a, FP b) => a.Raw != b.Raw;
        public static bool operator <(FP a, FP b) => a.Raw < b.Raw;
        public static bool operator >(FP a, FP b) => a.Raw > b.Raw;
        public static bool operator <=(FP a, FP b) => a.Raw <= b.Raw;
        public static bool operator >=(FP a, FP b) => a.Raw >= b.Raw;

        public static FP Min(FP a, FP b) => a.Raw <= b.Raw ? a : b;
        public static FP Max(FP a, FP b) => a.Raw >= b.Raw ? a : b;
        public static FP Clamp(FP v, FP lo, FP hi) => Max(lo, Min(hi, v));

        public bool Equals(FP other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is FP other && Raw == other.Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(FP other) => Raw.CompareTo(other.Raw);

        /// <summary>Exact decimal text, integer-only formatting. For logs and tests, never for logic.</summary>
        public override string ToString()
        {
            long abs = Raw < 0 ? -Raw : Raw;
            string sign = Raw < 0 ? "-" : "";
            long frac = abs % Scale;
            if (frac == 0) return sign + (abs / Scale);
            return sign + (abs / Scale) + "." + frac.ToString("D4").TrimEnd('0');
        }
    }
}
