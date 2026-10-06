using System;
using System.Collections.Generic;
using Automatic.Battle.Math;

namespace Automatic.Game
{
    /// <summary>
    /// Runs the core's golden values inside the interpreter on device (design/07 §7 step 4).
    /// Values are copied from tests/Battle.Core.Tests; keep them in sync.
    /// </summary>
    public static class GoldenSelfCheck
    {
        public static List<string> Run()
        {
            var lines = new List<string>();
            Check(lines, "Prng seed 0", Draw(0u), "543,650,463,66,85,891|1587069247");
            Check(lines, "Prng seed 42", Draw(42u), "703,307,85,895,155,827|1892812290");
            Check(lines, "Prng seed max", Draw(4294967295u), "314,53,480,737,421,601|1923535031");

            var list = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            new Prng(7).Shuffle(list);
            Check(lines, "Shuffle seed 7", string.Join(",", list), "4,2,3,1,7,8,0,5,6,9");

            Check(lines, "FP 10*(1/3)", (FP.FromInt(10) * FP.FromRatio(1, 3)).ToString(), "3.333");
            Check(lines, "FP -1/3 raw", (-FP.One / FP.FromInt(3)).Raw.ToString(), "-3333");
            Check(lines, "FP overflow", Overflows(), "OverflowException");
            return lines;
        }

        private static string Draw(uint seed)
        {
            var prng = new Prng(seed);
            var values = new int[6];
            for (int i = 0; i < values.Length; i++) values[i] = prng.NextInt(1000);
            return string.Join(",", values) + "|" + prng.Peek();
        }

        private static string Overflows()
        {
            var big = FP.FromRaw(long.MaxValue / 2);
            try { _ = big + big + big; return "no exception"; }
            catch (OverflowException) { return "OverflowException"; }
        }

        private static void Check(List<string> lines, string name, string actual, string expected)
        {
            var ok = actual == expected;
            lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}: {actual}" + (ok ? "" : $" (expected {expected})"));
        }
    }
}
