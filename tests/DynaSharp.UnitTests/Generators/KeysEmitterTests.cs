using System.Text;
using System.Text.RegularExpressions;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// The key emitter as text: what it writes for each shape of a key in <c>Mapping/KeyModels.cs</c>, compiled at the oldest and the newest language
/// version. The test assembly has the same types from its own build, so here they live in a namespace of their own. <c>KeyFormats</c> has a builder for
/// every row of the format table, so its file is cut into a snapshot per builder, and a change to one format shows in one file.
/// </summary>
public sealed partial class KeysEmitterTests
{
    private const string TestNamespace = "namespace DynaSharp.UnitTests.Mapping.KeyEntities;";

    private const string ShapeNamespace = "namespace KeyShapes;";

    private const string CutFile = "KeyShapes.KeyFormats.g";

    private const string BuilderSnapshotPrefix = "KeyShapes.KeyFormats.Keys.";

    private static string KeyShapes => File.ReadAllText(Path.Combine(SnapshotComparer.ProjectDirectory, "Mapping", "KeyModels.cs"))
        .Replace(TestNamespace, ShapeNamespace, StringComparison.Ordinal);

    [Test]
    public void Run_EveryKeyShape_MatchesTheVerifiedSnapshots()
    {
        // Arrange, Act
        var output = GeneratorRun.Run(KeyShapes, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        foreach (var (name, text) in output.Result.Results[0].GeneratedSources.SelectMany(Snapshots))
        {
            SnapshotComparer.Verify(name, text);
        }
    }

    [Test]
    public async Task Run_KeyFormats_HasASnapshotPerBuilderAndNoOther()
    {
        // Arrange
        var output = GeneratorRun.Run(KeyShapes, new DynaSharpGenerator(), LanguageVersion.Latest);
        var builders = output.Result.Results[0].GeneratedSources
            .SelectMany(Snapshots)
            .Select(static snapshot => snapshot.Name)
            .Where(static name => name.StartsWith(BuilderSnapshotPrefix, StringComparison.Ordinal));

        // Act
        var files = Directory.GetFiles(Path.Combine(SnapshotComparer.ProjectDirectory, "Generators", "Snapshots"), BuilderSnapshotPrefix + "*.verified.cs")
            .Select(static path => Path.GetFileName(path).Replace(".verified.cs", string.Empty, StringComparison.Ordinal));

        // Assert (the comparer only looks at what is generated, so a file that a builder left behind, or a cut that found nothing, shows here)
        await Assert.That(files).IsEquivalentTo(builders);
    }

    [Test]
    [Arguments(LanguageVersion.CSharp11)]
    [Arguments(LanguageVersion.Latest)]
    public async Task Run_EveryKeyShape_CompilesWithoutWarnings(LanguageVersion language)
    {
        // Arrange, Act
        var output = GeneratorRun.Run(KeyShapes, new DynaSharpGenerator(), language);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Emit_EntityWithIndexes_WritesAConstantPerIndex()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), GlobalIndexKey("ByStatus", PartitionKey = "A"), GlobalIndexKey("orders-by_date", PartitionKey = "B"), LocalIndexKey("GSI1", "C")]
            public partial class E { }
            """;
        var model = Analysis.Model(Source, "global::Shop.E");

        // Act
        var body = Body(model);

        // Assert
        await Assert.That(body).Contains("""
                public static class Indexes
                {
                    /// <summary>The index <c>ByStatus</c>.</summary>
                    public const string ByStatus = "ByStatus";

                    /// <summary>The index <c>orders-by_date</c>.</summary>
                    public const string OrdersByDate = "orders-by_date";

                    /// <summary>The index <c>GSI1</c>.</summary>
                    public const string Gsi1 = "GSI1";
                }
            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task Emit_EntityWithoutAnIndex_WritesNoIndexesClass()
    {
        // Arrange
        var model = Analysis.Model("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { }""", "global::Shop.E");

        // Act
        var body = Body(model);

        // Assert
        await Assert.That(body.Contains("class Indexes", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments("indexes", "IndexIndexes")]
    [Arguments("equals", "IndexEquals")]
    [Arguments("to-string", "IndexToString")]
    [Arguments("get_hash.code", "IndexGetHashCode")]
    [Arguments("get-type", "IndexGetType")]
    [Arguments("MemberwiseClone", "IndexMemberwiseClone")]
    [Arguments("reference-equals", "IndexReferenceEquals")]
    [Arguments("finalize", "IndexFinalize")]
    [Arguments("2024", "Index2024")]
    [Arguments("---", "Index")]
    public async Task Run_IndexNameThatNeedsAnotherConstant_CompilesWithoutWarnings(string indexName, string constant)
    {
        // Arrange
        var source = $$"""
            using DynaSharp.Mapping;

            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("{{indexName}}", PartitionKey = "A")]
            public partial class E
            {
                public const string Name = E.Indexes.{{constant}};
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    [Arguments(LanguageVersion.CSharp11)]
    [Arguments(LanguageVersion.Latest)]
    public async Task Run_EntitiesWithIndexesOverABase_CompilesWithoutWarnings(LanguageVersion language)
    {
        // Arrange (a base with indexes needs new on the class, and one without must not get it)
        const string Source = """
            using DynaSharp.Mapping;

            [DynamoDbEntity, Discriminator("B"), PartitionKey("B"), GlobalIndexKey("Gsi", PartitionKey = "A")]
            public partial class WithIndex { }

            [DynamoDbEntity, Discriminator("C"), PartitionKey("C"), GlobalIndexKey("Other", PartitionKey = "A")]
            public partial class Hides : WithIndex { }

            [DynamoDbEntity, Discriminator("D"), PartitionKey("D")]
            public partial class WithoutIndex { }

            [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("Gsi", PartitionKey = "A")]
            public partial class Plain : WithoutIndex { }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), language);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Emit_Document_WritesHandlesAndNoKeysClass()
    {
        // Arrange
        var model = Analysis.ModelOfWholeFile(TestSources.SectionFourOrder, "global::Shop.Address");

        // Act
        var body = Body(model);

        // Assert
        await Assert.That((body.Contains("class Attrs", StringComparison.Ordinal), body.Contains("class Keys", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    [Test]
    public async Task Emit_AnyType_StartsWithABlankLineAndUsesOnlyLineFeeds()
    {
        // Arrange
        var model = Analysis.ModelOfWholeFile(TestSources.SectionFourOrder, "global::Shop.Order");

        // Act
        var body = Body(model);

        // Assert (every part of a body starts so, and the entity emitter cuts off the first character)
        await Assert.That((body[0], body.Contains('\r', StringComparison.Ordinal))).IsEqualTo(('\n', false));
    }

    [Test]
    public async Task Emit_FixedWidthTemplate_UsesTheExactLengthAndAVariableOneTheCap()
    {
        // Arrange
        var model = Analysis.ModelOfWholeFile(TestSources.SectionFourOrder, "global::Shop.Order");

        // Act
        var body = Body(model);

        // Assert ("ORDER#" 6, ms 24, "#" 1, Guid 36; STATUS is an enum name)
        await Assert.That(body.Contains("BufferLength = 67;", StringComparison.Ordinal) && body.Contains("BufferLength = 256;", StringComparison.Ordinal)).IsTrue();
    }

    // Every character of a generated file is in one snapshot: a builder in its own, and what is left in the one of the file.
    private static IEnumerable<(string Name, string Text)> Snapshots(GeneratedSourceResult source)
    {
        var name = source.HintName.Replace(".g.cs", ".g", StringComparison.Ordinal);
        var text = source.SourceText.ToString();
        if (name != CutFile)
        {
            return [(name, text)];
        }

        var builders = Builder().Matches(text).Select(static match => (BuilderSnapshotPrefix + match.Groups["name"].Value, match.Value));
        return [.. builders, (name, Builder().Replace(text, string.Empty))];
    }

    private static string Body(TypeModel type)
    {
        var body = new StringBuilder();
        KeysEmitter.Emit(new IndentedCode(body), type);
        return body.ToString();
    }

    // What one run of KeyBuilderEmitter writes: a blank line, the record of the parsed values and the builder, whose closing brace is the first at its indent.
    [GeneratedRegex(@"^\n(?: {8}///[^\n]*\n)+ {8}\[[^\n]*\n {8}public readonly record struct (?<name>\w+)Parts\([^\n]*\n\n(?: {8}///[^\n]*\n)+ {8}\[[^\n]*\n {8}public static class \k<name>\n.*?^ {8}\}\n", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Builder();
}
