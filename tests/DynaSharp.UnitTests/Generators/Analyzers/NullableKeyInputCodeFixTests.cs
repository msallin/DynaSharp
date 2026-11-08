using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;
using static DynaSharp.UnitTests.Generators.Analyzers.CodeFixRun;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA107, applied in a real workspace to what the generator reports there, and run through the generator again.</summary>
public sealed class NullableKeyInputCodeFixTests
{
    private const string Sparse = NullableKeyInputCodeFix.SparseKey;

    private const string Required = NullableKeyInputCodeFix.RequiredKey;

    private const string Entity = """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("O#{Id}")]""";

    private static readonly CodeFixRun Run = new(new NullableKeyInputCodeFix(), "DYNA107");

    [Test]
    public async Task RegisterCodeFixes_NullableInputOfAGlobalIndex_OffersSparseAndRequired()
    {
        // Arrange
        var files = Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}")]""", "    public string? Email { get; set; }\n");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Set Sparse = true", "Make Email required and non-nullable"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}")]""", """[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}", Sparse = true)]""")]
    [Arguments("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}", Sparse = false)]""", """[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}", Sparse = true)]""")]
    [Arguments("""[SortKey("S"), LocalIndexKey("ByEmail", "E#{Email}")]""", """[SortKey("S"), LocalIndexKey("ByEmail", "E#{Email}", Sparse = true)]""")]
    public async Task Fix_Sparse_MakesTheIndexSparse(string index, string expected)
    {
        // Arrange
        var files = Order(index, "    public string? Email { get; set; }\n");

        // Act
        var fixedFiles = await Run.FixAsync(Sparse, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(index, expected, StringComparison.Ordinal));
    }

    [Test]
    [Arguments("    public string? Email { get; set; }\n", "    public required string Email { get; set; }\n")]
    [Arguments("    public string? Email { get; init; } = null;\n", "    public required string Email { get; init; }\n")]
    [Arguments("    public required int? Email { get; set; }\n", "    public required int Email { get; set; }\n")]
    public async Task Fix_Required_MakesThePropertyRequiredAndNotNullable(string property, string expected)
    {
        // Arrange
        var files = Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}")]""", property);

        // Act
        var fixedFiles = await Run.FixAsync(Required, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(property, expected, StringComparison.Ordinal));
    }

    [Test]
    [Arguments(Sparse)]
    [Arguments(Required)]
    public async Task Fix_Result_HasNoDiagnosticLeft(string key)
    {
        // Arrange
        var files = Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}")]""", "    public string? Email { get; set; }\n");

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(await Run.LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_RequiredAlready_OffersNonNullable()
    {
        // Arrange
        var files = Order(string.Empty, "    [SortKey] public required string? Email { get; set; }\n");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make Email non-nullable"]);
    }

    [Test]
    public async Task Fix_PropertyInAnotherPart_EditsThatPart()
    {
        // Arrange
        (string, string)[] files = [.. Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}")]""", string.Empty), ("Email.cs", Header + "public partial class Order\n{\n    public string? Email { get; set; }\n}\n")];

        // Act
        var fixedFiles = await Run.FixAsync(Required, files);

        // Assert
        await Assert.That(fixedFiles).IsEquivalentTo(new Dictionary<string, string> { ["Email.cs"] = Header + "public partial class Order\n{\n    public required string Email { get; set; }\n}\n" });
    }

    [Test]
    public async Task Fix_ParameterOfARecord_DropsTheQuestionMark()
    {
        // Arrange
        var files = One(Entity + "\n[SortKey(\"E#{Email}\")]\npublic partial record Order(string Id, string? Email);\n");

        // Act
        var fixedFiles = await Run.FixAsync(Required, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("public partial record Order(string Id, string Email);");
    }

    [Test]
    [Arguments("    public string? Email => Id;\n")]
    [Arguments("    public string? Email { get; }\n\n    public Order(string? email) => Email = email;\n")]
    [Arguments("    public System.Nullable<int> Email { get; set; }\n")]
    public async Task RegisterCodeFixes_PropertyThatCannotBeRequired_OffersSparseAlone(string property)
    {
        // Arrange
        var files = Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}")]""", property);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Set Sparse = true"]);
    }

    [Test]
    public async Task RegisterCodeFixes_ParameterWithADefault_OffersSparseAlone()
    {
        // Arrange
        var files = One("[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey(\"O#{Id}\"), GlobalIndexKey(\"ByEmail\", PartitionKey = \"E#{Email}\")]\npublic partial record Order(string Id, string? Email = null);\n");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Set Sparse = true"]);
    }

    [Test]
    public async Task RegisterCodeFixes_KeyOfAPropertyOfACompiledBase_OffersRequiredOnTheOverride()
    {
        // Arrange
        var library = Analysis.Library("namespace Lib;\n\npublic class Base\n{\n    [DynaSharp.Mapping.SortKey] public virtual string? Email { get; set; }\n}\n");
        var files = One(Entity + "\npublic partial class Order : Lib.Base\n{\n    public required string Id { get; set; }\n\n    public override string? Email { get; set; }\n}\n");

        // Act
        var titles = await new CodeFixRun(new NullableKeyInputCodeFix(), "DYNA107") { Libraries = [library] }.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make Email required and non-nullable"]);
    }

    [Test]
    public async Task RegisterCodeFixes_InputOfACompiledBase_OffersSparseAlone()
    {
        // Arrange
        var library = Analysis.Library("namespace Lib;\n\npublic class Base\n{\n    public string? Email { get; set; }\n}\n");
        var files = One(Entity + "\n[GlobalIndexKey(\"ByEmail\", PartitionKey = \"E#{Email}\")]\npublic partial class Order : Lib.Base\n{\n    public required string Id { get; set; }\n}\n");

        // Act
        var titles = await new CodeFixRun(new NullableKeyInputCodeFix(), "DYNA107") { Libraries = [library] }.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Set Sparse = true"]);
    }

    [Test]
    public async Task FixAll_TwoInputsOfOneIndex_AddsSparseOnce()
    {
        // Arrange
        var files = Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}#{Phone}")]""", "    public string? Email { get; set; }\n    public string? Phone { get; set; }\n");

        // Act
        var fixedFiles = await Run.FixAllAsync(Sparse, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}#{Phone}", Sparse = true)]""");
    }

    [Test]
    public async Task FixAll_OnePropertyInTwoIndexes_EditsItOnce()
    {
        // Arrange
        var files = Order("""[GlobalIndexKey("ByEmail", PartitionKey = "E#{Email}"), GlobalIndexKey("ByMail", PartitionKey = "M#{Email}")]""", "    public string? Email { get; set; } = null;\n");

        // Act
        var fixedFiles = await Run.FixAllAsync(Required, FixAllScope.Project, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("    public required string Email { get; set; }\n}");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new NullableKeyInputCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA107"]);
    }

    private static (string, string)[] Order(string attributes, string members) =>
        One(Entity + "\n" + attributes + "\npublic partial class Order\n{\n    public required string Id { get; set; }\n" + members + "}\n");
}
