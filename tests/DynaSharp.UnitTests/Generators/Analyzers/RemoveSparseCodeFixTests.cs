using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;
using static DynaSharp.UnitTests.Generators.Analyzers.CodeFixRun;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA110, applied in a real workspace to what the generator reports there, and run through the generator again.</summary>
public sealed class RemoveSparseCodeFixTests
{
    private static readonly CodeFixRun Run = new(new RemoveSparseCodeFix(), "DYNA110");

    [Test]
    [Arguments("""GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}", Sparse = true)""", """GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}")""")]
    [Arguments("""GlobalIndexKey("ByStatus", Sparse = true, PartitionKey = "S#{Status}")""", """GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}")""")]
    [Arguments("GlobalIndexKey(\"ByStatus\",\n    PartitionKey = \"S#{Status}\",\n    Sparse = true)", "GlobalIndexKey(\"ByStatus\",\n    PartitionKey = \"S#{Status}\")")]
    public async Task Fix_SparseIndexOfInputsNeverAbsent_RemovesSparse(string index, string expected)
    {
        // Arrange
        var files = Order(index);

        // Act
        var fixedFiles = await Run.FixAsync(RemoveSparseCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(index, expected, StringComparison.Ordinal));
    }

    [Test]
    public async Task Fix_Result_HasNoDiagnosticLeft()
    {
        // Arrange
        var files = Order("""GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}", Sparse = true)""");

        // Act
        var fixedFiles = await Run.FixAsync(RemoveSparseCodeFix.Key, files);

        // Assert
        await Assert.That(await Run.LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_SparseIndex_OffersToRemoveIt()
    {
        // Arrange
        var files = Order("""GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}", Sparse = true)""");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove Sparse = true"]);
    }

    [Test]
    public async Task FixAll_TwoIndexes_RemovesBoth()
    {
        // Arrange
        var files = Order("""GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}", Sparse = true), GlobalIndexKey("ById", PartitionKey = "I#{Id}", Sparse = true)""");

        // Act
        var fixedFiles = await Run.FixAllAsync(RemoveSparseCodeFix.Key, FixAllScope.Project, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("""GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}"), GlobalIndexKey("ById", PartitionKey = "I#{Id}")]""");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new RemoveSparseCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA110"]);
    }

    private static (string, string)[] Order(string index) =>
        One("[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey(\"O#{Id}\"), " + index + "]\npublic partial class Order\n{\n    public required string Id { get; set; }\n    public required string Status { get; set; }\n}\n");
}
