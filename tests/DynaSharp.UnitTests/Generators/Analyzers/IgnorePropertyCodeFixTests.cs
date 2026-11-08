using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA003, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class IgnorePropertyCodeFixTests
{
    private const string Key = IgnorePropertyCodeFix.Key;

    private static readonly CodeFixRun Run = new(new IgnorePropertyCodeFix(), "DYNA003");

    [Test]
    public async Task Fix_PropertyOnALineOfItsOwn_PutsTheAttributeOnALineAboveIt()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument]\npublic partial class A\n{\n    /// <summary>Anything.</summary>\n    public object? Bag { get; set; }\n}");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "[DynamoDbDocument]\npublic partial class A\n{\n    /// <summary>Anything.</summary>\n    [DynamoDbIgnore]\n    public object? Bag { get; set; }\n}");
    }

    [Test]
    public async Task Fix_Result_CompilesWithoutTheError()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { public object? Bag { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_ParameterOfAPositionalRecord_OffersNothing()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial record A(object Bag);");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { public object? Bag { get; set; } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_IgnoresEachProperty()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { public object? Bag { get; set; } public object? Box { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).EndsWith("{ [DynamoDbIgnore] public object? Bag { get; set; } [DynamoDbIgnore] public object? Box { get; set; } }");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new IgnorePropertyCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA003"]);
    }
}
