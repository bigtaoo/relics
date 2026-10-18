namespace Automatic.Battle.Analyzers.Tests;

public class ApiTests
{
    [Fact]
    public Task FloatingPointTypesLiteralsAndCasts() => Verify.Analyze(@"
class C
{
    {|DET001:float|} _f;
    System.{|DET001:Double|} _d;
    System.Collections.Generic.List<{|DET001:decimal|}>? _l;

    int M(int i)
    {
        var x = {|DET001:1.5|};
        var y = ({|DET001:double|})i;
        var z = {|DET001:double|}.MaxValue;
        return i;
    }
}");

    [Fact]
    public Task FloatingPointFromFrameworkMembers() => Verify.Analyze(@"
class C
{
    void M(System.TimeSpan t)
    {
        var s = t.{|DET001:TotalSeconds|};
        var u = System.TimeSpan.FromSeconds({|DET001:5|});
    }
}");

    [Fact]
    public Task MathClockRandomGuid() => Verify.Analyze(@"
class C
{
    void M()
    {
        var a = System.{|DET002:Math|}.Abs(-1);
        var b = System.{|DET003:DateTime|}.UtcNow.Ticks;
        var c = System.{|DET003:Environment|}.TickCount;
        var d = System.Diagnostics.{|DET003:Stopwatch|}.StartNew();
        var e = new System.{|DET004:Random|}(1).Next();
        var f = System.{|DET005:Guid|}.NewGuid();
    }
}");

    [Fact]
    public Task UsingStaticIsCaughtAtTheCall() => Verify.Analyze(@"
using static System.{|DET002:Math|};
class C
{
    int M() => {|DET002:Abs|}(-1);
}");

    [Fact]
    public Task ThreadingTypesAndKeywords() => Verify.Analyze(@"
class C
{
    readonly object _gate = new object();
    {|DET006:volatile|} int _v;

    {|DET006:async|} System.Threading.Tasks.{|DET006:Task|} M()
    {
        {|DET006:lock|} (_gate) { _v++; }
        new System.Threading.{|DET006:Thread|}(() => { }).Start();
        {|DET006:await|} System.Threading.Tasks.{|DET006:Task|}.Yield();
    }
}");

    [Fact]
    public Task UnstableHashCodes() => Verify.Analyze(@"
struct S { public int A; }
class C
{
    int M(string s, S v, long l)
    {
        var a = s.{|DET008:GetHashCode|}();
        var b = new object().{|DET008:GetHashCode|}();
        var c = v.{|DET008:GetHashCode|}();
        var d = System.StringComparer.Ordinal.{|DET008:GetHashCode|}(s);
        var e = System.{|DET008:HashCode|}.Combine(1, 2);
        return l.GetHashCode() + 7.GetHashCode();
    }
}");

    [Fact]
    public Task CleanCodeHasNoDiagnostics() => Verify.Analyze(@"
using System.Collections.Generic;
using System.Linq;
class C
{
    long M(List<int> xs, System.TimeSpan t)
    {
        var sum = 0L;
        foreach (var x in xs) sum += x;
        return sum + xs.Where(x => x > 0).Count() + t.Ticks;
    }
}");
}
