using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// The builders of keys that have no template: a plain key on a property, and a template of one bare placeholder over the property's own attribute. These compile the
/// use of a builder, so the type of what it returns is part of the test.
/// </summary>
public sealed class PlainKeyBuilderTests
{
    private const string Usings = """
        using System;
        using DynaSharp;
        using DynaSharp.Mapping;
        using DynaSharp.Serialization;

        public enum Plan { Free, Pro }

        public readonly record struct Grams(int Value);

        public readonly record struct GramsConverter : IDynamoDbConverter<Grams>
        {
            public static void Write(DynamoDbWriter writer, Grams value) => writer.WriteNumber(value.Value);

            public static Grams Read(ref DynamoDbReader reader, AttributeType type) => default;
        }

        """;

    [Test]
    [Arguments("string", "\"a\"")]
    [Arguments("int", "1")]
    [Arguments("long", "1L")]
    [Arguments("decimal", "1.5m")]
    [Arguments("byte[]", "new byte[] { 1 }")]
    [Arguments("Guid", "Guid.Empty")]
    [Arguments("Plan", "Plan.Pro")]
    [Arguments("DateTimeOffset", "DateTimeOffset.UnixEpoch")]
    public async Task Run_PlainPartitionKey_HasAnOfWithThePropertyType(string type, string value)
    {
        // Arrange
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator("E")]
            public partial class E
            {
                [PartitionKey] public required {{type}} Id { get; init; }

                public static PartitionKey<E> Key() => Keys.Pk.Of({{value}});
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
    public async Task Run_BarePlaceholderOfAnIndex_HasAnOfAtEveryLanguageVersion(LanguageVersion language)
    {
        // Arrange (the shape of a real service: a sparse index over the attribute of a nullable property)
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("THING#{Id}")]
            [GlobalIndexKey("ByOwner", PartitionKey = "{Owner}", SortKey = "{Rank}", Sparse = true, PartitionKeyAttributeName = "owner", SortKeyAttributeName = "rank")]
            public partial record Thing
            {
                public required string Id { get; init; }

                [DynamoDbAttribute("owner")]
                public string? Owner { get; init; }

                [DynamoDbAttribute("rank")]
                public int? Rank { get; init; }

                public static PartitionKey<Thing> Key() => Keys.ByOwnerPk.Of("a");

                public static AttributeValue Sort() => Keys.ByOwnerSk.Of(5);
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), language);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Run_PlainKeyWithAConverter_TakesTheTypeOfTheProperty()
    {
        // Arrange
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator("E")]
            public partial class E
            {
                [PartitionKey, DynamoDbConverter(typeof(GramsConverter))] public required Grams Id { get; init; }

                public static PartitionKey<E> Key() => Keys.Pk.Of(new Grams(1));
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Run_PlainSortKey_GivesTheValueForAConditionOnTheSortKey()
    {
        // Arrange
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator("E")]
            public partial class E
            {
                [PartitionKey] public required string Id { get; init; }

                [SortKey] public required DateTimeOffset At { get; init; }

                public static SortKey From(DateTimeOffset at) => SortKey.Ge(Keys.Sk.Of(at));
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Run_PlainKeyStoredAsAString_HasAFormatForAListOfPartitions()
    {
        // Arrange
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator("E")]
            public partial class E
            {
                [PartitionKey] public required Plan Id { get; init; }

                [SortKey] public required Guid Other { get; init; }

                public static string Text() => Keys.Pk.Format(Plan.Pro) + Keys.Sk.Format(Guid.Empty);
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    [Arguments("int", "1")]
    [Arguments("byte[]", "new byte[] { 1 }")]
    public async Task Run_PlainKeyNotStoredAsAString_HasNoFormat(string type, string value)
    {
        // Arrange (a list of strings would query a number with a string, which DynamoDB rejects)
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator("E")]
            public partial class E
            {
                [PartitionKey] public required {{type}} Id { get; init; }

                public static string Text() => Keys.Pk.Format({{value}});
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(string.Join("\n", output.Problems)).Contains("CS0117");
    }

    [Test]
    public async Task Run_TemplateKeyBesideAPlainOne_KeepsItsOwnBuilder()
    {
        // Arrange (the message of a real service: a plain partition key under a sort key with a template)
        var source = $$"""
            {{Usings}}
            [DynamoDbEntity, Discriminator("E"), SortKey("MSG#{At:ticks}")]
            public partial class E
            {
                [PartitionKey] public required string Id { get; init; }

                public required DateTimeOffset At { get; init; }

                public static PartitionKey<E> Key() => Keys.Pk.Of("a");

                public static string Sort(DateTimeOffset at) => Keys.Sk.Format(at);
            }
            """;

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }
}
