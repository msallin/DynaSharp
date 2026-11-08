using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA035, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class NullInitialValueCodeFixTests
{
    private const string Null = NullInitialValueCodeFix.NullKey;

    private const string NonNullable = NullInitialValueCodeFix.NonNullableKey;

    private static readonly CodeFixRun Run = new(() => new NullInitialValueCodeFix(), "DYNA035");

    [Test]
    [Arguments(Null, "public string? Note { get; set; }\n")]
    [Arguments(NonNullable, "public string Note { get; set; } = \"n/a\";\n")]
    public async Task Fix_PropertyWithAnInitializer_RemovesItOrTheQuestionMark(string key, string expected)
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A\n{\n    public string? Note { get; set; } = \"n/a\";\n}");

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "[DynamoDbDocument] public partial class A\n{\n    " + expected + "}");
    }

    [Test]
    [Arguments(Null, "public partial record A(string? Note = null);")]
    [Arguments(NonNullable, "public partial record A(string Note = \"n/a\");")]
    public async Task Fix_ParameterOfAPositionalRecord_MakesTheDefaultNullOrDropsTheQuestionMark(string key, string expected)
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial record A(string? Note = \"n/a\");");

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "[DynamoDbDocument] " + expected);
    }

    [Test]
    public async Task Fix_ParameterOfAConstructor_DropsTheQuestionMarkOfBoth()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { public A(int? count = 1) { Count = count; } public int? Count { get; } }");

        // Act
        var fixedFiles = await Run.FixAsync(NonNullable, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).EndsWith("{ public A(int count = 1) { Count = count; } public int Count { get; } }");
    }

    [Test]
    [Arguments(Null)]
    [Arguments(NonNullable)]
    public async Task Fix_Result_LeavesNoWarning(string key)
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { public string? Note { get; set; } = \"n/a\"; }");

        // Act
        var fixedFiles = await Run.FixAsync(key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Property_OffersBothInTheOrderOfTheMessage()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { public string? Note { get; set; } = \"n/a\"; }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove the initializer", "Make Note non-nullable"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_TypeWrittenWithoutAQuestionMark_OffersTheInitializerAlone()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { public System.Nullable<int> Count { get; set; } = 1; }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove the initializer"]);
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyOfTheParameterInAGeneratedFile_OffersTheDefaultAlone()
    {
        // Arrange
        (string, string)[] files = [.. CodeFixRun.One("[DynamoDbDocument] public partial class A { public A(int? count = 1) { Count = count; } }"), ("A.g.cs", "namespace Shop;\n\npublic partial class A { public int? Count { get; } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make the default value null"]);
    }

    [Test]
    public async Task RegisterCodeFixes_DefaultOfAnAttribute_OffersNothing()
    {
        // Arrange
        var files = CodeFixRun.One(
            "[DynamoDbDocument] public partial class A { public A([System.Runtime.InteropServices.Optional, System.Runtime.InteropServices.DefaultParameterValue(1)] int? count) { Count = count; } public int? Count { get; } }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyOfABaseTypeInAnotherProject_OffersNothing()
    {
        // Arrange (the diagnostic stands on the type, whose project does not hold the property)
        var run = new CodeFixRun(() => new NullInitialValueCodeFix(), "DYNA035") { Chained = true };
        (string, string)[] library = [("Base.cs", "namespace Lib;\n\npublic class Base { public string? Note { get; set; } = \"n/a\"; }")];
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A : Lib.Base { }");

        // Act
        var (solution, diagnostics) = await run.RunAsync(library, files);
        var actions = await run.ActionsAsync(solution, diagnostics.Single());

        // Assert
        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { public string? Note { get; set; } = \"n/a\"; }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_RemovesEachInitializer()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { public string? Note { get; set; } = \"n/a\"; public string? Tag { get; set; } = \"t\"; }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Null, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).EndsWith("{ public string? Note { get; set; } public string? Tag { get; set; } }");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new NullInitialValueCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA035"]);
    }
}
