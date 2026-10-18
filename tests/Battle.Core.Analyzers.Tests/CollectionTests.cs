namespace Automatic.Battle.Analyzers.Tests;

public class CollectionTests
{
    [Fact]
    public Task ForeachOverDictionaryKeysValuesAndHashSet() => Verify.Analyze(@"
using System.Collections.Generic;
class C
{
    void M(Dictionary<int, int> d, HashSet<int> s, IReadOnlyDictionary<int, int> r)
    {
        foreach (var kv in {|DET007:d|}) { }
        foreach (var k in {|DET007:d.Keys|}) { }
        foreach (var v in {|DET007:d.Values|}) { }
        foreach (var x in {|DET007:s|}) { }
        foreach (var kv in {|DET007:r|}) { }
    }
}");

    [Fact]
    public Task LinqCopiesAndEnumerators() => Verify.Analyze(@"
using System.Collections.Generic;
using System.Linq;
class C
{
    void M(Dictionary<int, int> d, HashSet<int> s, int[] buffer)
    {
        var a = {|DET007:s|}.ToList();
        var b = new List<int>({|DET007:d.Keys|});
        var c = {|DET007:d|}.First();
        var e = d.{|DET007:GetEnumerator|}();
        s.{|DET007:CopyTo|}(buffer);
        var f = string.Join("","", {|DET007:s|});
    }
}");

    [Fact]
    public Task LookupsAndOrderedCollectionsAreFine() => Verify.Analyze(@"
using System.Collections.Generic;
class C
{
    int M(Dictionary<int, int> d, HashSet<int> s, SortedDictionary<int, int> sorted, List<int> ids)
    {
        var n = d.Count + s.Count;
        if (d.TryGetValue(1, out var v) && s.Contains(2) && d.ContainsKey(3)) n += v;
        d[4] = 5;
        foreach (var kv in sorted) n += kv.Value;
        foreach (var id in ids) n += d[id];
        return n;
    }
}");

    [Fact]
    public Task DerivedDictionaryIsStillUnordered() => Verify.Analyze(@"
class Table : System.Collections.Generic.Dictionary<int, int> { }
class C
{
    void M(Table t)
    {
        foreach (var kv in {|DET007:t|}) { }
    }
}");
}
