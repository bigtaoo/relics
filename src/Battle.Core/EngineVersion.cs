namespace Automatic.Battle
{
    /// <summary>
    /// Version of the battle rules (design/07 §5, 01 §6). Bump it with every change that can change a
    /// battle's result or event stream (rules, timing tables, formulas, the order of random draws), and
    /// update the golden hashes in the tests. Clients and servers with different versions must not
    /// play the same battle. Part of the end-of-battle hash.
    /// </summary>
    public static class EngineVersion
    {
        /// <summary>1: TurnBattle, the first stand-in rules driving the presentation (ADR-014).</summary>
        public const int Current = 1;
    }
}
