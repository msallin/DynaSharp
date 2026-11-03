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

    // The largest source of the generator tests, and three tests ask the same of it at this version.
    private static readonly SharedResult<GeneratorRunOutput> AtLatest = new(static () => GeneratorRun.Run(KeyShapes, new DynaSharpGenerator(), LanguageVersion.Latest));

    private static string KeyShapes => File.ReadAllText(Path.Combine(SnapshotComparer.ProjectDirectory, "Mapping", "KeyModels.cs"))
        .Replace(TestNamespace, ShapeNamespace, StringComparison.Ordinal);

    [Test]
    public void Run_EveryKeyShape_MatchesTheVerifiedSnapshots()
    {
        // Arrange, Act
        var output = AtLatest.Value;

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
        var output = AtLatest.Value;
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
        var output = language == LanguageVersion.Latest ? AtLatest.Value : GeneratorRun.Run(KeyShapes, new DynaSharpGenerator(), language);

        // Assert (a driver does not read the #pragma of RawTwin, which has this warning on purpose)
        await Assert.That(output.Problems.Where(static problem => !problem.Contains("warning DYNA120", StringComparison.Ordinal))).IsEmpty();
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

        // Assert ("ORDER#" 6, ticks 28, "#" 1, Guid 36; STATUS is an enum name)
        await Assert.That(body.Contains("BufferLength = 71;", StringComparison.Ordinal) && body.Contains("BufferLength = 256;", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments("string", "Pk", "string id")]
    [Arguments("int", "Pk", "int id")]
    [Arguments("decimal", "Pk", "decimal id")]
    [Arguments("global::System.DateTime", "Pk", "global::System.DateTime id")]
    public async Task Emit_PartitionKeyThatCanRejectAValue_HasTryOf(string type, string builder, string signature)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}")]
            public partial class E { public required {{type}} Id { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(source, "global::Shop.E"));

        // Assert
        await Assert.That(body)
            .Contains($"public static bool TryOf({signature}, out PrimaryKey key)")
            .And.Contains($"public static bool TryOf({signature}, out global::DynaSharp.PartitionKey<global::Shop.E> key)");
    }

    [Test]
    [Arguments("global::System.Guid")]
    [Arguments("global::System.DateOnly")]
    [Arguments("global::System.DateTimeOffset")]
    public async Task Emit_PartitionKeyWhoseValuesAllFit_HasNoTryOf(string type)
    {
        // Arrange
        var source = $$"""
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}")]
            public partial class E { public required {{type}} Id { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(source, "global::Shop.E"));

        // Assert
        await Assert.That(body.Contains("TryOf", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Emit_KeyOfLiterals_HasNoTryOf()
    {
        // Arrange
        var model = Analysis.Model("""[DynamoDbEntity, Discriminator("E"), PartitionKey("A"), SortKey("B")] public partial class E { }""", "global::Shop.E");

        // Act
        var body = Body(model);

        // Assert
        await Assert.That(body.Contains("TryOf", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Emit_PlainKeys_HaveNoTryOf()
    {
        // Arrange (a plain key goes through its converter, and DynamoDB is what rejects it)
        const string Source = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [PartitionKey] public required string Id { get; set; } [SortKey] public required int Slot { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body.Contains("TryOf", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Emit_PlainPartitionKeyBesideATemplate_ChecksTheTemplateAlone()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), SortKey("S#{Name}")]
            public partial class E { [PartitionKey] public required string Id { get; set; } public required string Name { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body).Contains("public static bool TryOf(string id, string name, out PrimaryKey key)").And.Contains("if (Sk.Check(false, name))");
    }

    [Test]
    public async Task Emit_BothKeysCanRejectAValue_ChecksBothWithTheParametersOfEach()
    {
        // Arrange (one property in both keys is one parameter)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}"), SortKey("B#{Id}#{Name}")]
            public partial class E { public required string Id { get; set; } public required string Name { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body).Contains("if (Pk.Check(false, id) && Sk.Check(false, id, name))");
    }

    [Test]
    public async Task Emit_SortKeyOfAnIndex_HasNoCheck()
    {
        // Arrange (only the keys of the table are in Keys.Of)
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}"), GlobalIndexKey("Gsi", PartitionKey = "P", SortKey = "S#{Name}")]
            public partial class E { public required string Id { get; set; } public required string Name { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body.Split("internal static bool Check").Length).IsEqualTo(2);
    }

    [Test]
    public async Task Run_PlaceholderCalledKey_TakesTheNameOfTheOutParameter()
    {
        // Arrange
        const string Source = """
            using DynaSharp.Mapping;

            [DynamoDbEntity, Discriminator("E"), PartitionKey("appointment#{Key}", AttributeName = "Id"), SortKey("S#{Key_}")]
            public partial class E { public required string Key { get; set; } public required string Key_ { get; set; } }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.CSharp11);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Emit_KeyThatCanRejectAValue_ChecksInOf()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}"), SortKey("B#{Id}#{Name}")]
            public partial class E { public required string Id { get; set; } public required string Name { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body).Contains("Pk.Check(true, id);").And.Contains("Sk.Check(true, id, name);");
    }

    [Test]
    public async Task Emit_KeyWhoseValuesAllFit_ChecksNothingInOf()
    {
        // Arrange
        const string Source = """
            [DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}")]
            public partial class E { public required global::System.Guid Id { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body).Contains("public static PrimaryKey Of(global::System.Guid id) => new PrimaryKey(id);");
    }

    [Test]
    public async Task Emit_PartitionKeyWithTryOf_SaysSoInTheDocOfOf()
    {
        // Arrange
        const string Source = """[DynamoDbEntity, Discriminator("E"), PartitionKey("A#{Id}")] public partial class E { public required string Id { get; set; } }""";

        // Act
        var body = Body(Analysis.Model(Source, "global::Shop.E"));

        // Assert
        await Assert.That(body.Split("<c>TryOf</c>").Length).IsEqualTo(3);
    }

    [Test]
    [Arguments("A#{Day:d3}#{Id}", "int from, int to", "Prefix(from), Prefix(to)")]
    [Arguments("{Shelf}#{Row:d3}#{Id}", "string shelf, int from, int to", "Prefix(shelf, from), Prefix(shelf, to)")]
    [Arguments("{Day:d3}#end", "int from, int to", "Prefix(from), Prefix(to)")]
    [Arguments("{Day:d3}#{Id:raw}", "int from, int to", "Prefix(from), Prefix(to)")]
    [Arguments("{Name} {Id}", "string from, string to", "Prefix(from), Prefix(to)")]
    public async Task Emit_KeyThatGoesOnBehindAField_HasRangeOverPrefixes(string template, string parameters, string bounds)
    {
        // Arrange
        var body = Body(Analysis.Model(Source(template), "global::Shop.E"));

        // Act
        var range = $"public static global::DynaSharp.SortKeyCondition Range({parameters}) => global::DynaSharp.SortKeyCondition.Between({bounds});";

        // Assert
        await Assert.That(body).Contains(range);
    }

    [Test]
    [Arguments("{Day:d3}")]
    [Arguments("A#{Name}")]
    [Arguments("A#{Name:raw}")]
    [Arguments("{Day:d3}#")]
    [Arguments("A{Day:d3}#")]
    [Arguments("{Name}#{Id}")]
    [Arguments("{Count:plain}#{Id}")]
    [Arguments("A#{Plan}#{Id}")]
    [Arguments("PROFILE")]
    public async Task Emit_KeyWithNothingBehindAnOrderedField_HasNoRange(string template)
    {
        // Arrange, Act
        var body = Body(Analysis.Model(Source(template), "global::Shop.E"));

        // Assert (with only a delimiter behind the field, the key at the upper bound is that bound, and BETWEEN takes it)
        await Assert.That(body.Contains(" Range(", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Emit_PlainSortKey_HasNoRange()
    {
        // Arrange
        const string Plain = """
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [PartitionKey] public required string Id { get; set; } [SortKey] public required int Slot { get; set; } }
            """;

        // Act
        var body = Body(Analysis.Model(Plain, "global::Shop.E"));

        // Assert
        await Assert.That(body.Contains(" Range(", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments("[DynamoDbConverter(typeof(SdkDateTimeConverter))] public required DateTime At { get; set; }", true)]
    [Arguments("[DynamoDbConverter(typeof(BoolAsNumberConverter))] public required bool At { get; set; }", true)]
    [Arguments("[DynamoDbConverter(typeof(OffsetTimestampConverter))] public required DateTimeOffset At { get; set; }", false)]
    public async Task Emit_PlainSortKeyWithAnOptInConverter_HasTheConditionsOfItsOrder(string member, bool hasRanges)
    {
        // Arrange (text of a fixed width and a number order as the values do, and a time at its own offset does not)
        var source = $$"""
            [DynamoDbEntity, Discriminator("E")]
            public partial class E { [PartitionKey] public required string Id { get; set; } [SortKey] {{member}} }
            """;

        // Act
        var body = Body(Analysis.Model(source, "global::Shop.E"));

        // Assert
        await Assert.That((body.Contains(" Eq(", StringComparison.Ordinal), body.Contains(" Between(", StringComparison.Ordinal))).IsEqualTo((true, hasRanges));
    }

    [Test]
    public async Task Emit_BetweenOnAKeyThatGoesOn_PointsAtRange()
    {
        // Arrange, Act
        var body = Body(Analysis.Model(Source("A#{Day:d3}#{Id}"), "global::Shop.E"));

        // Assert
        await Assert.That((body.Contains("<c>Range</c>", StringComparison.Ordinal), body.Contains("SortKeyCondition.Between(Keys.", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    [Test]
    public async Task Emit_BetweenWithoutARange_DoesNotPointAtOne()
    {
        // Arrange, Act
        var body = Body(Analysis.Model(Source("{Day:d3}#"), "global::Shop.E"));

        // Assert
        await Assert.That((body.Contains(" Between(int from, int to)", StringComparison.Ordinal), body.Contains("<c>Range</c>", StringComparison.Ordinal))).IsEqualTo((true, false));
    }

    private static string Source(string sortKey) => $$"""
        public enum Plan { Free, Pro }
        [DynamoDbEntity, Discriminator("E"), PartitionKey("P"), SortKey("{{sortKey}}")]
        public partial class E
        {
            public int Day { get; set; }
            public int Row { get; set; }
            public int Count { get; set; }
            public Plan Plan { get; set; }
            public string Id { get; set; } = "i";
            public string Name { get; set; } = "n";
            public string Shelf { get; set; } = "s";
        }
        """;

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
