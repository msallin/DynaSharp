using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA117, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class RequireKeyCodeFixTests
{
    private const string Key = RequireKeyCodeFix.Key;

    private const string Header = "using DynaSharp.Mapping;\n\nnamespace Shop;\n\n";

    private const string Entity = """[DynamoDbEntity, Discriminator("ORDER"), PartitionKey("ORDER#{Id}")]""" + "\npublic partial class Order\n{\n";

    private static readonly CodeFixRun Run = new(new RequireKeyCodeFix(), "DYNA117");

    [Test]
    [Arguments("public string Id { get; set; } = string.Empty;\n", "public required string Id { get; set; }\n")]
    [Arguments("public string Id { get; init; } = \"\"; // the order\n", "public required string Id { get; init; } // the order\n")]
    [Arguments("public virtual string Id { get; set; } = null!;\n", "public virtual required string Id { get; set; }\n")]
    public async Task Fix_KeyPropertyWithAnEmptyDefault_MakesItRequired(string property, string expected)
    {
        // Arrange
        var source = Header + Entity + "    " + property + "}\n";

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Header + Entity + "    " + expected + "}\n");
    }

    [Test]
    [Arguments("private set")]
    [Arguments("protected init")]
    public async Task RegisterCodeFixes_SetterLessVisibleThanTheType_OffersNothing(string setter)
    {
        // Arrange
        var source = Header + Entity + "    public string Id { get; " + setter + "; } = \"\";\n}\n";

        // Act
        var (solution, diagnostics) = await Run.RunAsync([("Order.cs", source)]);
        var actions = await Run.ActionsAsync(solution, diagnostics.Single());

        // Assert
        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_KeyProperty_NamesIt()
    {
        // Arrange
        var source = Header + Entity + "    public string Id { get; set; } = string.Empty;\n}\n";

        // Act
        var titles = await Run.TitlesAsync(("Order.cs", source));

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make Id required, without its initializer"]);
    }

    [Test]
    public async Task RegisterCodeFixes_GeneratedFile_OffersNothing()
    {
        // Arrange
        var source = "#nullable enable\n" + Header + Entity + "    public string Id { get; set; } = string.Empty;\n}\n";

        // Act
        var (solution, diagnostics) = await Run.RunAsync([("Order.generated.cs", source)]);
        var actions = await Run.ActionsAsync(solution, diagnostics.Single());

        // Assert
        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task Fix_Result_HasNothingLeftToReport()
    {
        // Arrange
        var source = Header + Entity + "    public string Id { get; set; } = string.Empty;\n}\n";

        // Act
        var fixedFiles = await Run.FixAsync(Key, ("Order.cs", source));
        var reported = await Run.ReportedAsync(fixedFiles);

        // Assert
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_TwoKeyProperties_MakesEachRequired(FixAllScope scope)
    {
        // Arrange
        var source = Header + Entity.Replace("PartitionKey(\"ORDER#{Id}\")", "PartitionKey(\"ORDER#{Id}\"), SortKey(\"AT#{At}\")", StringComparison.Ordinal)
            + "    public string Id { get; set; } = \"\";\n    public string At { get; set; } = \"\";\n}\n";

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, scope, [("Order.cs", source)]);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).EndsWith("    public required string Id { get; set; }\n    public required string At { get; set; }\n}\n");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new RequireKeyCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA117"]);
    }
}
