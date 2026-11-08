using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA021, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class IgnoredKeyCodeFixTests
{
    private const string Ignore = IgnoredKeyCodeFix.IgnoreKey;

    private const string Key = IgnoredKeyCodeFix.KeyKey;

    private const string Entity = "[DynamoDbEntity, Discriminator(\"E\")]\npublic partial class E\n{\n    [PartitionKey]\n    [DynamoDbIgnore]\n    public required string Id { get; set; }\n}";

    private static readonly CodeFixRun Run = new(() => new IgnoredKeyCodeFix(), "DYNA021");

    [Test]
    [Arguments(Ignore, "[DynamoDbEntity, Discriminator(\"E\")]\npublic partial class E\n{\n    [PartitionKey]\n    public required string Id { get; set; }\n}")]
    [Arguments(Key, "[DynamoDbEntity, Discriminator(\"E\")]\npublic partial class E\n{\n    [DynamoDbIgnore]\n    public required string Id { get; set; }\n}")]
    public async Task Fix_IgnoredPlainKey_RemovesOneOfTheTwo(string key, string expected)
    {
        // Arrange
        var files = CodeFixRun.One(Entity);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + expected);
    }

    [Test]
    public async Task Fix_ParameterOfAPositionalRecord_RemovesTheIgnoreOfItsProperty()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbEntity, Discriminator(\"E\")] public partial record E([property: PartitionKey, DynamoDbIgnore] string Id);");

        // Act
        var fixedFiles = await Run.FixAsync(Ignore, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).EndsWith("public partial record E([property: PartitionKey] string Id);");
    }

    [Test]
    public async Task Fix_RemoveTheIgnore_CompilesWithoutAnError()
    {
        // Arrange
        var files = CodeFixRun.One(Entity);

        // Act
        var fixedFiles = await Run.FixAsync(Ignore, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Property_OffersBoth()
    {
        // Arrange
        var files = CodeFixRun.One(Entity);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [DynamoDbIgnore]", "Remove [PartitionKey]"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_OverrideThatIgnoresTheKeyOfItsBase_OffersTheKey()
    {
        // Arrange
        var files = CodeFixRun.One(
            "public abstract class Base { [PartitionKey] public virtual string Id { get; set; } = \"\"; }\n"
            + "[DynamoDbEntity, Discriminator(\"E\")] public partial class E : Base { [DynamoDbIgnore] public override string Id { get; set; } = \"\"; }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [PartitionKey]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_KeyOfABaseTypeInALibrary_OffersNothing()
    {
        // Arrange (the diagnostic stands on the type, since the key has no place in source)
        var run = new CodeFixRun(() => new IgnoredKeyCodeFix(), "DYNA021")
        {
            Libraries = [Analysis.Library("namespace Lib; public abstract class Base { [DynaSharp.Mapping.PartitionKey] public virtual string Id { get; set; } = \"\"; }")],
        };
        var files = CodeFixRun.One("[DynamoDbEntity, Discriminator(\"E\")] public partial class E : Lib.Base { [DynamoDbIgnore] public override string Id { get; set; } = \"\"; }");

        // Act
        var titles = await run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyInAGeneratedPart_OffersNothing()
    {
        // Arrange
        (string, string)[] files =
        [
            ("Order.cs", CodeFixRun.Header + "[DynamoDbEntity, Discriminator(\"E\")] public partial class E { }"),
            ("E.g.cs", CodeFixRun.Header + "public partial class E { [PartitionKey, DynamoDbIgnore] public required string Id { get; set; } }"),
        ];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new IgnoredKeyCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA021"]);
    }
}
