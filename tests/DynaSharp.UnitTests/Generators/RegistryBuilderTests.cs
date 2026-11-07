using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>When an assembly gets <c>IncludingAll()</c>, and what it is told when it does not: DYNA037 and DYNA038.</summary>
public sealed class RegistryBuilderTests
{
    private const string Order = """
        [DynamoDbEntity, Discriminator("ORDER"), PartitionKey("ORDER#{Id}"), SortKey("ORDER")]
        public partial class Order { public string Id { get; set; } = "id"; }

        """;

    [Test]
    public async Task Run_EntitiesOfTwoTables_ReportsDyna037()
    {
        // Arrange
        const string Source = Order + """
            [DynamoDbEntity, Discriminator("SESSION"), PartitionKey("SESSION#{Id}", AttributeName = "id")]
            public partial class Session { public string Id { get; set; } = "id"; }

            [DynamoDbEntity, Discriminator("CART"), PartitionKey("CART#{Id}"), SortKey("CART")]
            public partial class Cart { public string Id { get; set; } = "id"; }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(
            "DYNA037 Info [0..0]: No IncludingAll() is generated for the entities of this assembly: they declare the keys of more than one table, 'pk' and 'sk' of Shop.Cart, 'id' of Shop.Session. Name the entities of a table with Including<T>().");
    }

    [Test]
    public async Task Run_EntityNestedInAPrivateType_ReportsDyna037()
    {
        // Arrange
        const string Source = Order + """
            public partial class Tests
            {
                [DynamoDbEntity, Discriminator("ROW"), PartitionKey("ROW#{Id}"), SortKey("ROW")]
                private partial class Row { public string Id { get; set; } = "id"; }
            }
            """;

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(
            "DYNA037 Info [0..0]: No IncludingAll() is generated for the entities of this assembly: 'Shop.Tests.Row' is private to a type around it, so no class outside that type can name it. Name the entities of a table with Including<T>().");
    }

    [Test]
    public async Task Run_TypeWithANameTheClassNeeds_ReportsDyna038()
    {
        // Arrange
        const string Source = "using DynaSharp.Mapping;\nnamespace Shop { " + Order + " }\nnamespace DynaSharp { public static class [|DynamoDbEntities|] { } }";

        // Act
        var report = Analysis.ReportOfWholeFile(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(
            Source,
            "DYNA038 Error: Type 'DynaSharp.DynamoDbEntities' has the name of the class that the generator writes for IncludingAll() of this assembly. Rename the type."));
    }

    [Test]
    public async Task Run_TypeWithANameTheClassNeeds_GeneratesNoClass()
    {
        // Arrange
        var source = "using DynaSharp.Mapping;\nnamespace Shop { " + Order + " }\nnamespace DynaSharp { public static class DynamoDbEntities { } }";

        // Act
        var result = GeneratorRun.Generate(source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(result.Results[0].GeneratedSources.Select(static generated => generated.HintName)).IsEquivalentTo(["Shop.Order.g.cs"]);
    }

    [Test]
    public async Task Run_TypeWithANameTheClassNeedsAndEntitiesOfTwoTables_ReportsOnlyDyna037()
    {
        // Arrange (there is no class to clash with)
        var source = "using DynaSharp.Mapping;\nnamespace Shop { " + Order
            + "[DynamoDbEntity, Discriminator(\"S\"), PartitionKey(\"S#{Id}\")] public partial class Session { public string Id { get; set; } = \"id\"; } }\n"
            + "namespace DynaSharp { public static class DynamoDbEntities { } }";

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That((report.StartsWith("DYNA037 Info", StringComparison.Ordinal), report.Contains("DYNA038", StringComparison.Ordinal))).IsEqualTo((true, false));
    }
}
