using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA013, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class EntityOrDocumentCodeFixTests
{
    private const string Both = "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{Id}\")]\n[DynamoDbDocument]\npublic partial class E { public required string Id { get; set; } }";

    private static readonly CodeFixRun Run = new(() => new EntityOrDocumentCodeFix(), "DYNA013");

    [Test]
    [Arguments(EntityOrDocumentCodeFix.EntityKey, "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{Id}\")]\npublic partial class E { public required string Id { get; set; } }")]
    [Arguments(EntityOrDocumentCodeFix.DocumentKey, "[Discriminator(\"E\"), PartitionKey(\"E#{Id}\")]\n[DynamoDbDocument]\npublic partial class E { public required string Id { get; set; } }")]
    public async Task Fix_EntityAndDocument_RemovesOne(string key, string expected)
    {
        // Arrange
        var files = CodeFixRun.One(Both);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + expected);
    }

    [Test]
    public async Task Fix_KeepTheEntity_CompilesWithoutAnError()
    {
        // Arrange
        var files = CodeFixRun.One(Both);

        // Act
        var fixedFiles = await Run.FixAsync(EntityOrDocumentCodeFix.EntityKey, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Type_OffersBoth()
    {
        // Arrange
        var files = CodeFixRun.One(Both);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [DynamoDbEntity]", "Remove [DynamoDbDocument]"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_AttributeInAGeneratedPart_OffersTheOther()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{Id}\")] public partial class E { public required string Id { get; set; } }"), ("E.g.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class E { }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [DynamoDbEntity]"]);
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new EntityOrDocumentCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA013"]);
    }
}
