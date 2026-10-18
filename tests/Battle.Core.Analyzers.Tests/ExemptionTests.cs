namespace Automatic.Battle.Analyzers.Tests;

public class ExemptionTests
{
    [Fact]
    public Task ExemptMemberIsSilenced() => Verify.Analyze(@"
using Automatic.Battle;
class C
{
    [DeterminismExempt(""debug readout, never feeds the simulation"")]
    double Ratio(int a, int b) => (double)a / b;

    [DeterminismExempt(""editor tooling"")]
    readonly System.Random _r = new System.Random(1);

    int Plain() => 1;
}");

    [Fact]
    public Task ExemptTypeCoversNestedMembersAndLocalFunctions() => Verify.Analyze(@"
using Automatic.Battle;
[DeterminismExempt(""presentation helpers"")]
class Presentation
{
    float F() => 1f;
}
class C
{
    int M()
    {
        return Local();
        [DeterminismExempt(""local tooling"")]
        static int Local() => (int)System.Math.Abs(-1.0);
    }
}");

    [Fact]
    public Task EmptyReasonIsAnErrorAndExemptsNothing() => Verify.Analyze(@"
using Automatic.Battle;
class C
{
    [{|DET009:DeterminismExempt("" "")|}]
    {|DET001:double|} F() => 0;
}");

    [Fact]
    public Task PragmaCannotSuppress() => Verify.Analyze(@"
class C
{
#pragma warning disable DET001
    {|DET001:double|} _d;
#pragma warning restore DET001
}");

    [Fact]
    public Task UnmarkedAssemblyIsNotChecked() => Verify.Analyze(@"
class C
{
    double M() => System.Math.Sqrt(2.0) + new System.Random().NextDouble();
}", marked: false);
}
