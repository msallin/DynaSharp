using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA042, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class PassByValueCodeFixTests
{
    private const string Value = PassByValueCodeFix.ValueKey;

    private const string In = PassByValueCodeFix.InKey;

    private static readonly CodeFixRun Run = new(new PassByValueCodeFix(), "DYNA042");

    [Test]
    [Arguments("ref int count", Value, "int count")]
    [Arguments("ref int count", In, "in int count")]
    [Arguments("out int count", Value, "int count")]
    [Arguments("ref readonly int count", Value, "int count")]
    [Arguments("ref readonly int count", In, "in int count")]
    [Arguments("scoped ref int count", Value, "int count")]
    [Arguments("scoped ref int count", In, "scoped in int count")]
    public async Task Fix_ParameterByReference_TakesItByValueOrAsIn(string parameter, string key, string expected)
    {
        // Arrange
        var files = Document(parameter);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Document(expected)[0].Item2);
    }

    [Test]
    [Arguments(Value)]
    [Arguments(In)]
    public async Task Fix_Result_CompilesWithoutTheError(string key)
    {
        // Arrange
        var files = Document("ref int count");

        // Act
        var fixedFiles = await Run.FixAsync(key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Parameter_OffersBothInTheOrderOfTheMessage()
    {
        // Arrange
        var files = Document("ref int count");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Take count by value", "Take count as in"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_ParameterInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", Document("ref int count")[0].Item2)];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_TakesEachByValue()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { public A(ref int count, out int total) { total = 0; } public int Count { get; set; } public int Total { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Value, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("public A(int count, int total)");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new PassByValueCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA042"]);
    }

    private static (string, string)[] Document(string parameter) =>
        CodeFixRun.One("[DynamoDbDocument] public partial class A { public A(" + parameter + ") { Count = 1; } public int Count { get; set; } }");
}
