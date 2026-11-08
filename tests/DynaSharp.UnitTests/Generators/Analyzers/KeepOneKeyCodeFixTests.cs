using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA009, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class KeepOneKeyCodeFixTests
{
    private const string OnTypeAndProperty = "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E\"), SortKey(\"S\")] public partial class E\n{\n    [SortKey] public string B { get; set; } = \"\";\n}";

    private static readonly CodeFixRun Run = new(new KeepOneKeyCodeFix(), "DYNA009");

    [Test]
    public async Task RegisterCodeFixes_KeyOnTypeAndProperty_OffersToKeepEach()
    {
        // Arrange
        var files = CodeFixRun.One(OnTypeAndProperty);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Keep only the [SortKey] on E", "Keep only the [SortKey] on B"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(0, "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E\"), SortKey(\"S\")] public partial class E\n{\n    public string B { get; set; } = \"\";\n}")]
    [Arguments(1, "[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E\")] public partial class E\n{\n    [SortKey] public string B { get; set; } = \"\";\n}")]
    public async Task Fix_KeyOnTypeAndProperty_RemovesTheOther(int index, string expected)
    {
        // Arrange
        var files = CodeFixRun.One(OnTypeAndProperty);

        // Act
        var fixedFiles = await Run.FixAsync(KeepOneKeyCodeFix.KeyPrefix + index, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + expected);
    }

    [Test]
    public async Task Fix_KeyOnThreeProperties_RemovesTheTwoOthers()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbEntity, Discriminator(\"E\")] public partial class E\n{\n    [PartitionKey] public string A { get; set; } = \"\";\n    [PartitionKey] public string B { get; set; } = \"\";\n    [PartitionKey] public string C { get; set; } = \"\";\n}");

        // Act
        var fixedFiles = await Run.FixAsync(KeepOneKeyCodeFix.KeyPrefix + "1", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).EndsWith("{\n    public string A { get; set; } = \"\";\n    [PartitionKey] public string B { get; set; } = \"\";\n    public string C { get; set; } = \"\";\n}");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Fix_Result_CompilesWithoutAnError(int index)
    {
        // Arrange
        var files = CodeFixRun.One(OnTypeAndProperty);

        // Act
        var fixedFiles = await Run.FixAsync(KeepOneKeyCodeFix.KeyPrefix + index, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_KeyOfABaseTypeInAGeneratedFile_OffersToKeepThatOne()
    {
        // Arrange
        (string, string)[] files =
        [
            ("Base.g.cs", CodeFixRun.Header + "public abstract class Base { [PartitionKey] public string A { get; set; } = \"\"; }"),
            ("Order.cs", CodeFixRun.Header + "[DynamoDbEntity, Discriminator(\"E\")] public partial class E : Base { [PartitionKey] public string B { get; set; } = \"\"; }"),
        ];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Keep only the [PartitionKey] on A"]);
    }

    [Test]
    public async Task RegisterCodeFixes_KeyOfABaseTypeInALibrary_OffersNothing()
    {
        // Arrange (the diagnostic stands on the type, since the key has no place in source)
        var run = new CodeFixRun(new KeepOneKeyCodeFix(), "DYNA009")
        {
            Libraries = [Analysis.Library("namespace Lib; public abstract class Base { [DynaSharp.Mapping.PartitionKey] public string A { get; set; } = \"\"; }")],
        };
        var files = CodeFixRun.One("[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"E\")] public partial class E : Lib.Base { }");

        // Act
        var titles = await run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new KeepOneKeyCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA009"]);
    }
}
