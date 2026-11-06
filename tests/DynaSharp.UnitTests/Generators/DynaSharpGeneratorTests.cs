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
        await Assert.That((string.Join(", ", output.Problems), Hints(output))).IsEqualTo((string.Empty, "DynaSharp-DynamoDbEntities.g.cs, Shop.ORDER.0.g.cs, Shop.Order.1.g.cs"));
    }

    [Test]
    public async Task Run_TypesWithoutAClash_KeepTheirHintNames()
    {
        // Arrange, Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(Hints(output)).IsEqualTo("DynaSharp-DynamoDbEntities.g.cs, Shop.Address.g.cs, Shop.Order.g.cs");
    }

    [Test]
    public async Task Run_EntityWithoutADiscriminator_GeneratesWhatAnOptOutGenerates()
    {
        // Arrange
        var missing = TransportRecord(string.Empty, string.Empty);
        var optedOut = TransportRecord(", Discriminator(OptOut = true)", string.Empty);

        // Act
        var generated = GeneratedText(GeneratorRun.Generate(missing, new DynaSharpGenerator(), LanguageVersion.Latest));

        // Assert
        await Assert.That(generated).IsEqualTo(GeneratedText(GeneratorRun.Generate(optedOut, new DynaSharpGenerator(), LanguageVersion.Latest)));
    }

    [Test]
    public async Task Run_UseOfAnEntityWithoutADiscriminator_FailsOnDyna020Alone()
    {
        // Arrange (a service that forgot the attribute sees one error, not a CS0311 and a CS0426 on every use of the type)
        var source = TransportRecord(string.Empty, """
            public static class Transports
            {
                public static Table<TransportRecord> Open(DynamoDbClient client) => client.Table<TransportRecord>("Shop");

                public static string Legs(Table<TransportRecord> transports) => transports.Get(TransportRecord.Keys.Of("t1")).Select(TransportRecord.Attrs.Legs).ToRequestJson();
            }
            """);

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(string.Join("\n", output.Problems)).IsEqualTo("(7,26): error DYNA020: Entity 'TransportRecord' has no discriminator. Add [Discriminator(\"TRANSPORT_RECORD\")]. For a table written by other tools, opt out with [Discriminator(OptOut = true)], or with [assembly: DynamoDbDefaults(DiscriminatorOptOut = true)] for every entity of the project.");
    }

    [Test]
    public async Task Run_UseOfAnEntityWithAFormatThatDoesNotFit_FailsOnDyna104Alone()
    {
        // Arrange (a DateOnly is written as yyyy-MM-dd already, so the format is a slip of one placeholder, not a reason for an error on every use)
        const string Source = """
            namespace Shop
            {
                using System;
                using DynaSharp;
                using DynaSharp.Mapping;

                [DynamoDbEntity, Discriminator("CUSTOMER"), PartitionKey("CUSTOMER#{Id}"), SortKey("PROFILE")]
                public partial class Customer
                {
                    public required string Id { get; init; }
                }

                [DynamoDbEntity, Discriminator("ORDER"), PartitionKey("CUSTOMER#{CustomerId}"), SortKey("ORDER#{OrderDate:yyyy-MM-dd}")]
                public partial class Order
                {
                    public required string CustomerId { get; init; }

                    public required DateOnly OrderDate { get; init; }
                }

                public static class Orders
                {
                    public static string Of(DynamoDbClient client, DateOnly from, DateOnly to) =>
                        client.Table<Order>("Shop").Query(Order.Keys.Pk.Of("alice"), Order.Keys.Sk.Between(from, to)).ToRequestJson();

                    public static string All(DynamoDbClient client) =>
                        client.Table("Shop", "pk", "sk").Query(Customer.Keys.Pk.Of("alice")).Of<Customer, Order>().ToRequestJson();
                }
            }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(string.Join("\n", output.Problems)).IsEqualTo("(13,93): error DYNA104: Format 'yyyy-MM-dd' does not fit type 'System.DateOnly'. It takes no format, because it is written as yyyy-MM-dd already; a DateTime or DateTimeOffset takes the shorter ones, such as yyyy-MM.");
    }

    private static string TransportRecord(string discriminator, string use) => $$"""
        namespace Shop
        {
            using DynaSharp;
            using DynaSharp.Mapping;

            [DynamoDbEntity, PartitionKey("TRANSPORT#{Id}"){{discriminator}}]
            public partial class TransportRecord
            {
                public required string Id { get; init; }

                public int Legs { get; init; }
            }

            {{use}}
        }
        """;

    private static string GeneratedText(GeneratorDriverRunResult result) => string.Join("\n", result.GeneratedTrees.Select(static tree => tree.ToString()));

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
