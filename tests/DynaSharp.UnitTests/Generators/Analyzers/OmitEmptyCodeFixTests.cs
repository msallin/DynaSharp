using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA030, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class OmitEmptyCodeFixTests
{
    private const string Remove = OmitEmptyCodeFix.RemoveKey;

    private const string Sparse = OmitEmptyCodeFix.SparseKey;

    private const string Members = " public partial class E { public string Id { get; set; } = \"id\"; [OmitEmpty] public string Owner { get; set; } = \"none\"; }";

    private static readonly CodeFixRun Run = new(new OmitEmptyCodeFix(), "DYNA030");

    [Test]
    public async Task Fix_InputOfAnIndexThatIsNotSparse_MakesTheIndexSparse()
    {
        // Arrange
        var files = Entity("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\")");

        // Act
        var fixedFiles = await Run.FixAsync(Sparse, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Entity("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\", Sparse = true)")[0].Item2);
    }

    [Test]
    public async Task Fix_InputOfTwoIndexes_MakesTheOneThatIsNotSparse()
    {
        // Arrange
        var files = Entity("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\", Sparse = true), GlobalIndexKey(\"GSI2\", PartitionKey = \"O#{Owner}\", PartitionKeyAttributeName = \"o2\")");

        // Act
        var fixedFiles = await Run.FixAsync(Sparse, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("GlobalIndexKey(\"GSI2\", PartitionKey = \"O#{Owner}\", PartitionKeyAttributeName = \"o2\", Sparse = true)");
    }

    [Test]
    public async Task Fix_Remove_TakesTheAttributeOff()
    {
        // Arrange
        var files = Entity("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\")");

        // Act
        var fixedFiles = await Run.FixAsync(Remove, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).EndsWith("public string Owner { get; set; } = \"none\"; }");
    }

    [Test]
    [Arguments(Remove)]
    [Arguments(Sparse)]
    public async Task Fix_Result_CompilesWithoutAnError(string key)
    {
        // Arrange
        var files = Entity("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\"), GlobalIndexKey(\"GSI2\", PartitionKey = \"P#{Owner}\")");

        // Act
        var fixedFiles = await Run.FixAsync(key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_InputOfTwoIndexes_NamesBoth()
    {
        // Arrange
        var files = Entity("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\"), GlobalIndexKey(\"GSI2\", PartitionKey = \"P#{Owner}\")");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [OmitEmpty]", "Make indexes GSI1 and GSI2 sparse"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("SortKey(\"O#{Owner}\")")]
    [Arguments("GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\", Sparse = true), SortKey(\"O#{Owner}\")")]
    public async Task RegisterCodeFixes_InputOfAKeyOfTheTable_OffersTheRemoval(string keys)
    {
        // Arrange
        var files = Entity(keys);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [OmitEmpty]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyThatIsNoString_OffersTheRemoval()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { [OmitEmpty] public int Count { get; set; } }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [OmitEmpty]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_IndexInAGeneratedPart_OffersTheRemoval()
    {
        // Arrange
        (string, string)[] files =
        [
            ("Order.cs", CodeFixRun.Header + "public partial class E { public string Id { get; set; } = \"id\"; [OmitEmpty] public string Owner { get; set; } = \"none\"; }"),
            ("E.g.cs", CodeFixRun.Header + "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{Id}\"), GlobalIndexKey(\"GSI1\", PartitionKey = \"O#{Owner}\")] public partial class E { }"),
        ];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [OmitEmpty]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_AttributeOfABaseTypeInALibrary_OffersNothing()
    {
        // Arrange (the diagnostic stands on the type, since the attribute has no place in source)
        var run = new CodeFixRun(new OmitEmptyCodeFix(), "DYNA030")
        {
            Libraries = [Analysis.Library("namespace Lib; public class Base { [DynaSharp.Mapping.OmitEmpty] public int Count { get; set; } }")],
        };
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A : Lib.Base { }");

        // Act
        var titles = await run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Remove_TakesEachOff()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { [OmitEmpty] public int Count { get; set; } [OmitEmpty] public int Total { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Remove, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).EndsWith("{ public int Count { get; set; } public int Total { get; set; } }");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new OmitEmptyCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA030"]);
    }

    private static (string, string)[] Entity(string keys) => CodeFixRun.One("[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E#{Id}\"), " + keys + "]" + Members);
}
