using System.Text.RegularExpressions;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// Which key of an index an update can copy from an attribute, <c>SET key = attribute</c>: only where the key is the stored text of the attribute byte for byte. Each case puts one shape in the sort key
/// of a sparse index, over a property stored as <c>stored</c>, and reads <c>CopyOf</c> from the metadata the generator writes for that key.
/// </summary>
public sealed partial class KeyTemplateEmitterTests
{
    private const string Source = EmitterShapes.Usings + """
        namespace Copy
        {
            public enum Level { Low, High }

            [DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]
            public enum Mood { Calm, Loud }

            [DynamoDbEntity]
            [Discriminator("COPY")]
            [PartitionKey("COPY#{Id}")]
            [SortKey("DATA")]
            [GlobalIndexKey("ByIt", PartitionKey = "T#{Trigger}", SortKey = "@TEMPLATE", Sparse = true)]
            public partial record Copied
            {
                public required string Id { get; init; }

                public string? Trigger { get; init; }

                @PROPERTY
            }
        }
        """;

    [Test]
    [Arguments("public string? Value { get; init; }", "{Value}")]
    [Arguments("public string? Value { get; init; }", "{Value:raw}")]
    [Arguments("public Guid? Value { get; init; }", "{Value}")]
    [Arguments("public DateOnly? Value { get; init; }", "{Value}")]
    [Arguments("public DateTimeOffset? Value { get; init; }", "{Value}")]
    [Arguments("public DateTimeOffset? Value { get; init; }", "{Value:ticks}")]
    [Arguments("public DateTime? Value { get; init; }", "{Value}")]
    [Arguments("public Level? Value { get; init; }", "{Value}")]
    [Arguments("public Mood? Value { get; init; }", "{Value}")]
    public async Task Lines_OneBarePlaceholderOfTheTextOfItsAttribute_CopiesTheAttribute(string property, string template)
    {
        // Arrange, Act
        var copyOf = CopyOfTheSortKey(property, template);

        // Assert
        await Assert.That(copyOf).IsEqualTo("stored");
    }

    [Test]
    [Arguments("public string? Value { get; init; }", "V#{Value}")]
    [Arguments("public string? Value { get; init; }", "{Value}#{Id}")]
    [Arguments("public string? Value { get; init; }", "{Value}#")]
    [Arguments("public DateTimeOffset? Value { get; init; }", "{Value:ms}")]
    [Arguments("public DateTimeOffset? Value { get; init; }", "{Value:yyyy-MM-dd}")]
    [Arguments("public DateTime? Value { get; init; }", "{Value:us}")]
    [Arguments("[DynamoDbConverter(typeof(SdkDateTimeConverter))] public DateTime? Value { get; init; }", "{Value}")]
    [Arguments("[DynamoDbConverter(typeof(EpochSecondsConverter))] public DateTimeOffset? Value { get; init; }", "{Value}")]
    [Arguments("public int? Value { get; init; }", "{Value}")]
    [Arguments("public int? Value { get; init; }", "{Value:d6}")]
    [Arguments("public long? Value { get; init; }", "{Value:n12}")]
    [Arguments("public decimal? Value { get; init; }", "{Value}")]
    [Arguments("public Level? Value { get; init; }", "{Value:d3}")]
    public async Task Lines_AnyOtherShape_CopiesNothing(string property, string template)
    {
        // Arrange, Act
        var copyOf = CopyOfTheSortKey(property, template);

        // Assert
        await Assert.That(copyOf).IsNull();
    }

    [Test]
    public async Task Lines_InputOnlyAKeyHolds_CopiesNothing()
    {
        // Arrange, Act (a [KeyOnly] property has no attribute to copy from)
        var copyOf = CopyOfTheSortKey("[KeyOnly] public required string Value { get; init; }", "{Value}", attribute: string.Empty);

        // Assert
        await Assert.That(copyOf).IsNull();
    }

    [Test]
    public async Task Lines_PropertyUnderItsOwnName_CopiesThatName()
    {
        // Arrange, Act
        var copyOf = CopyOfTheSortKey("public string? Value { get; init; }", "{Value}", attribute: string.Empty);

        // Assert
        await Assert.That(copyOf).IsEqualTo("Value");
    }

    private static string? CopyOfTheSortKey(string property, string template, string attribute = "[DynamoDbAttribute(\"stored\")] ")
    {
        var source = Source.Replace("@TEMPLATE", template, StringComparison.Ordinal).Replace("@PROPERTY", attribute + property, StringComparison.Ordinal);
        var result = GeneratorRun.Generate(source, new DynaSharpGenerator(), LanguageVersion.Latest);
        var generated = string.Concat(result.Results[0].GeneratedSources.Select(static file => file.SourceText.ToString()));
        var line = generated.Split('\n').Single(static line => line.Contains("new __Map.DynamoDbKeyTemplate(\"byitsk\"", StringComparison.Ordinal));
        var match = CopyOf().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("CopyOf = \"([^\"]*)\"")]
    private static partial Regex CopyOf();
}
