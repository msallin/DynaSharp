using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA001, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class MakePartialCodeFixTests
{
    private const string Key = MakePartialCodeFix.Key;

    private const string Header = "using DynaSharp.Mapping;\n\nnamespace Shop;\n\n";

    private const string Entity = """[DynamoDbEntity, Discriminator("ORDER"), PartitionKey("ORDER#{Id}")]""";

    private const string Members = " { public required string Id { get; set; } }";

    private static readonly CodeFixRun Run = new(new MakePartialCodeFix(), "DYNA001");

    [Test]
    public async Task Fix_TypeWithModifiers_AddsPartialBeforeTheKeyword()
    {
        // Arrange
        var source = Header + Entity + "\npublic sealed class Order" + Members;

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Header + Entity + "\npublic sealed partial class Order" + Members);
    }

    [Test]
    public async Task Fix_RecordWithoutModifiers_PutsPartialWhereTheKeywordStood()
    {
        // Arrange
        var source = Header + Entity + "\n/// <summary>An order.</summary>\nrecord Order" + Members;

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Header + Entity + "\n/// <summary>An order.</summary>\npartial record Order" + Members);
    }

    [Test]
    public async Task Fix_NestedType_MakesEachTypeThatLacksItPartial()
    {
        // Arrange
        var source = Header + "public static class Outer\n{\n    public partial class Middle\n    {\n        " + Entity + "\n        public class Order" + Members + "\n    }\n}\n";

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Header + "public static partial class Outer\n{\n    public partial class Middle\n    {\n        " + Entity + "\n        public partial class Order" + Members + "\n    }\n}\n");
    }

    [Test]
    public async Task RegisterCodeFixes_NestedType_NamesTheTypesFromTheOutermost()
    {
        // Arrange
        var source = Header + "public static class Outer\n{\n    " + Entity + "\n    public class Order" + Members + "\n}\n";

        // Act
        var titles = await Run.TitlesAsync(("Order.cs", source));

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add partial to Outer, Order"]);
    }

    [Test]
    public async Task RegisterCodeFixes_GeneratedFile_OffersNothing()
    {
        // Arrange
        var source = Header + Entity + "\npublic class Order" + Members;

        // Act
        var titles = await Run.TitlesAsync(("Order.g.cs", source));

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task Fix_Result_HasNothingLeftToReport()
    {
        // Arrange
        var source = Header + "public static class Outer\n{\n    " + Entity + "\n    public class Order" + Members + "\n}\n";

        // Act
        var fixedFiles = await Run.FixAsync(Key, ("Order.cs", source));
        var reported = await Run.ReportedAsync(fixedFiles);

        // Assert
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    [Arguments(FixAllScope.Solution)]
    public async Task FixAll_TwoEntitiesInOneContainingType_MakesEachPartialOnce(FixAllScope scope)
    {
        // Arrange
        var source = Header + "public static class Outer\n{\n    " + Entity + "\n    public class Order" + Members
            + "\n    [DynamoDbEntity, Discriminator(\"LINE\"), PartitionKey(\"LINE#{Id}\")]\n    public class Line" + Members + "\n}\n";

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, scope, [("Order.cs", source)]);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).IsEqualTo(source
            .Replace("static class", "static partial class", StringComparison.Ordinal)
            .Replace("public class", "public partial class", StringComparison.Ordinal));
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new MakePartialCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA001"]);
    }
}
