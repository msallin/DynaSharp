using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA227: <c>Value</c> of a <c>SelectResult</c> in a member that never looks at <c>Found</c>.</summary>
public sealed class SelectResultAnalyzerTests
{
    private const string Message = "DYNA227 Info: Value of this SelectResult throws InvalidOperationException when the read found no item, and this member never looks at Found. Test Found first, or use GetValueOrDefault().";

    [Test]
    [Arguments("""var total = (await shop.Get("a").Select(Attr<int>("n"))).[|Value|];""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); var total = result.[|Value|];""")]
    [Arguments("""var result = await shop.Query("a").Select(Attr<int>("n")).ValueAsync(); Console.WriteLine(result.[|Value|]);""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); if (result is { [|Value|]: > 3 }) { }""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); Func<int> later = () => result.[|Value|];""")]
    public async Task Analyze_ValueInAMemberWithoutFound_Reports(string statements)
    {
        // Arrange
        var source = AnalyzerRun.Member($$"""public static async Task Run() { {{statements}} }""");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new SelectResultAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_TwoValuesWithoutFound_ReportsBoth()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static async Task Run() { var result = await shop.Get("a").Select(Attr<int>("n")); var sum = result.[|Value|] + result.[|Value|]; }""");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new SelectResultAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message, Message));
    }

    [Test]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); var total = result.Found ? result.Value : 0;""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); if (!result.Found) { return; } var total = result.Value;""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); if (result is { Found: true, Value: var total }) { }""")]
    [Arguments("""var first = await shop.Get("a").Select(Attr<int>("n")); var second = await shop.Get("b").Select(Attr<int>("n")); var total = first.Found ? second.Value : 0;""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); Func<bool> asked = () => result.Found; var total = result.Value;""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); var total = result.GetValueOrDefault(); var fallback = result.GetValueOrDefault(-1);""")]
    [Arguments("""var result = await shop.Get("a").Select(Attr<int>("n")); var name = nameof(result.Value);""")]
    [Arguments("""var got = await shop.Get("a").SendAsync(); var item = got.Item;""")]
    [Arguments("""var maybe = (int?)3; var value = maybe.Value;""")]
    public async Task Analyze_ValueWhereFoundIsAskedOrNoValueOfASelectResult_ReportsNothing(string statements)
    {
        // Arrange
        var source = AnalyzerRun.Member($$"""public static async Task Run() { {{statements}} }""");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new SelectResultAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_FoundInAnotherMember_StillReports()
    {
        // Arrange (the test is by member, so a check elsewhere does not count)
        var source = AnalyzerRun.Member("""
            public static bool Has(SelectResult<int> result) => result.Found;
            public static int Read(SelectResult<int> result) => result.[|Value|];
            """);

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new SelectResultAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_TypeOfTheSameNameInAnotherNamespace_ReportsNothing()
    {
        // Arrange (a type of the same shape that is not the library's)
        var source = AnalyzerRun.Member(
            "public static int Read(Other.SelectResult<int> result) => result.Value;",
            "namespace Other { public readonly struct SelectResult<T> { public T Value => default!; } }");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source, new SelectResultAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilationWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var compilation = CSharpCompilation.Create(
            "NoReference",
            [CSharpSyntaxTree.ParseText("struct SelectResult<T> { public T Value => default!; } class C { int M(SelectResult<int> r) => r.Value; }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        // Act
        var diagnostics = await compilation.WithAnalyzers([new SelectResultAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }
}
