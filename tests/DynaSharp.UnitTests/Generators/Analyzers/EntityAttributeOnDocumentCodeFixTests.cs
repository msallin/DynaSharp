using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA012, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class EntityAttributeOnDocumentCodeFixTests
{
    private const string Remove = EntityAttributeOnDocumentCodeFix.RemoveKey;

    private const string Entity = EntityAttributeOnDocumentCodeFix.EntityKey;

    private const string Address = "[DynamoDbDocument]\npublic partial class Address\n{\n    [PartitionKey]\n    public required string Id { get; set; }\n}";

    private static readonly CodeFixRun Run = new(new EntityAttributeOnDocumentCodeFix(), "DYNA012");

    [Test]
    [Arguments(Remove, "[DynamoDbDocument]\npublic partial class Address\n{\n    public required string Id { get; set; }\n}")]
    [Arguments(Entity, "[DynamoDbEntity]\npublic partial class Address\n{\n    [PartitionKey]\n    public required string Id { get; set; }\n}")]
    public async Task Fix_KeyOnAPropertyOfADocument_RemovesItOrMakesTheTypeAnEntity(string key, string expected)
    {
        // Arrange
        var files = CodeFixRun.One(Address);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + expected);
    }

    [Test]
    public async Task Fix_AttributeOnTheType_RemovesItFromItsList()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument, PartitionKey(\"A#{Id}\")] public partial class Address { public required string Id { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Remove, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "[DynamoDbDocument] public partial class Address { public required string Id { get; set; } }");
    }

    [Test]
    public async Task Fix_DocumentNamedInFull_NamesTheEntityInFull()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", "namespace Shop;\n\n[DynaSharp.Mapping.DynamoDbDocument] public partial class Address { [DynaSharp.Mapping.PartitionKey] public required string Id { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAsync(Entity, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).StartsWith("namespace Shop;\n\n[global::DynaSharp.Mapping.DynamoDbEntity] public partial class Address");
    }

    [Test]
    [Arguments(Remove, 0)]
    [Arguments(Entity, 0)]
    public async Task Fix_Result_HasNoErrorOfTheRuleLeft(string key, int expected)
    {
        // Arrange
        var files = CodeFixRun.One(Address);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left.Count(id => id == "DYNA012")).IsEqualTo(expected);
    }

    [Test]
    public async Task RegisterCodeFixes_Attribute_OffersBothInTheOrderOfTheMessage()
    {
        // Arrange
        var files = CodeFixRun.One(Address);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [PartitionKey]", "Make Address an entity"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_AttributeInAGeneratedFile_OffersTheEntity()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class Address { }"), ("Address.g.cs", CodeFixRun.Header + "public partial class Address { [PartitionKey] public required string Id { get; set; } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make Address an entity"]);
    }

    [Test]
    public async Task RegisterCodeFixes_DocumentInAGeneratedFile_OffersTheRemoval()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class Address { }"), ("Order.cs", CodeFixRun.Header + "public partial class Address { [PartitionKey] public required string Id { get; set; } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [PartitionKey]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_AttributeOfABaseTypeInALibrary_OffersTheEntity()
    {
        // Arrange (the diagnostic stands on the type, since the attribute has no place in source)
        var run = new CodeFixRun(new EntityAttributeOnDocumentCodeFix(), "DYNA012")
        {
            Libraries = [Analysis.Library("namespace Lib; public class Base { [DynaSharp.Mapping.PartitionKey] public string Id { get; set; } = \"\"; }")],
        };
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class Address : Lib.Base { }");

        // Act
        var titles = await run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make Address an entity"]);
    }

    [Test]
    public async Task FixAll_Entity_EditsTheDocumentOnce()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class Address { [PartitionKey] public required string Id { get; set; } [SortKey] public required string Line { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Entity, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).StartsWith(CodeFixRun.Header + "[DynamoDbEntity] public partial class Address { [PartitionKey]");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new EntityAttributeOnDocumentCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA012"]);
    }
}
