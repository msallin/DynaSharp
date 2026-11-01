using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>What the pipeline finds and hands on, before any emitter exists.</summary>
public sealed class DynaSharpGeneratorTests
{
    private static readonly string Source = TestSources.Compose(TestSources.Order, TestSources.Address);

    [Test]
    public async Task Run_EntityAndDocument_EachStepHoldsTheModelOfTheType()
    {
        // Arrange, Act
        var run = Run(LanguageVersion.Latest);

        // Assert
        await Assert.That((Models(run, TrackingNames.Entities), Models(run, TrackingNames.Documents)))
            .IsEqualTo(("global::Shop.Order", "global::Shop.Address"));
    }

    [Test]
    public async Task Run_TypeWithoutEitherAttribute_IsNotPickedUp()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation("namespace Shop { public class Plain { } }", LanguageVersion.Latest);

        // Act
        var run = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true)
            .RunGenerators(compilation).GetRunResult().Results[0];

        // Assert
        await Assert.That((Models(run, TrackingNames.Entities), Models(run, TrackingNames.Documents))).IsEqualTo((string.Empty, string.Empty));
    }

    [Test]
    public async Task Run_Diagnostic_IsLocatedInTheRealTree()
    {
        // Arrange: only a location in a tree of the compilation reaches #pragma and .editorconfig.
        var compilation = GeneratorRun.CreateCompilation(TestSources.Invoice, LanguageVersion.Latest);

        // Act
        var result = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGenerators(compilation).GetRunResult();

        // Assert
        await Assert.That(ReferenceEquals(result.Diagnostics.Single().Location.SourceTree, compilation.SyntaxTrees[0])).IsTrue();
    }

    [Test]
    public async Task Run_TypesThatDifferOnlyByCase_KeepsTheOutputOfBoth()
    {
        // Arrange (the driver compares hint names ignoring case, and a clash costs all output: CS8785)
        const string Source = """
            namespace Shop
            {
                [DynaSharp.Mapping.DynamoDbDocument]
                public partial class Order { public string Id { get; set; } = ""; }

                [DynaSharp.Mapping.DynamoDbDocument]
                public partial class ORDER { public int Id { get; set; } }
            }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((string.Join(", ", output.Problems), Hints(output))).IsEqualTo((string.Empty, "Shop.ORDER.0.g.cs, Shop.Order.1.g.cs"));
    }

    [Test]
    public async Task Run_EntityAndDocumentThatDifferOnlyByCase_KeepsTheOutputOfBoth()
    {
        // Arrange
        const string Source = """
            namespace Shop
            {
                using DynaSharp.Mapping;

                [DynamoDbEntity, Discriminator("ORDER"), PartitionKey("ORDER")]
                public partial class Order { }

                [DynamoDbDocument]
                public partial class ORDER { }
            }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((string.Join(", ", output.Problems), Hints(output))).IsEqualTo((string.Empty, "Shop.ORDER.0.g.cs, Shop.Order.1.g.cs"));
    }

    [Test]
    public async Task Run_TypesWithoutAClash_KeepTheirHintNames()
    {
        // Arrange, Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(Hints(output)).IsEqualTo("Shop.Address.g.cs, Shop.Order.g.cs");
    }

    private static string Hints(GeneratorRunOutput output) =>
        string.Join(", ", output.Result.Results[0].GeneratedSources.Select(static source => source.HintName).Order(StringComparer.Ordinal));

    private static GeneratorRunResult Run(LanguageVersion language) => GeneratorRun
        .CreateDriver(new DynaSharpGenerator(), language, trackSteps: true)
        .RunGenerators(GeneratorRun.CreateCompilation(Source, language))
        .GetRunResult()
        .Results[0];

    // "global::Shop.Order", one per type, joined by ", ". A step that found nothing is not reported at all.
    private static string Models(GeneratorRunResult run, string step) => string.Join(
        ", ",
        (run.TrackedSteps.TryGetValue(step, out var steps) ? steps : [])
            .SelectMany(s => s.Outputs)
            .Select(output => ((BuildResult)output.Value).Type!.FullName));
}
