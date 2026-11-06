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
    [Arguments("Lps.LabelPrintServer", "Lps.LabelPrintServer")]
    [Arguments("My-App.2", "My_App._2")]
    [Arguments("a..b", "a._.b")]
    [Arguments("", "_")]
    public async Task NamespaceSegments_AssemblyName_GivesValidNames(string assemblyName, string expected)
    {
        // Arrange, Act
        var segments = RegistryBuilder.NamespaceSegments(assemblyName);

        // Assert
        await Assert.That(string.Join(".", segments)).IsEqualTo(expected);
    }

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
    [Arguments("namespace GeneratorInput { public static class [|DynamoDbEntities|] { } }", "GeneratorInput.DynamoDbEntities", "GeneratorInput.DynamoDbEntities")]
    [Arguments("public class [|GeneratorInput|] { }", "GeneratorInput", "GeneratorInput")]
    public async Task Run_TypeWithANameTheClassNeeds_ReportsDyna038(string clash, string type, string name)
    {
        // Arrange
        var source = "using DynaSharp.Mapping;\nnamespace Shop { " + Order + " }\n" + clash;

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.ExpectOfWholeFile(
            source,
            $"DYNA038 Error: Type '{type}' has the name '{name}', which the generated class DynamoDbEntities of this assembly needs for itself or for its namespace, the name of the assembly. Rename the type."));
    }

    [Test]
    public async Task Run_TypeWithANameTheClassNeeds_GeneratesNoClass()
    {
        // Arrange
        var source = "using DynaSharp.Mapping;\nnamespace Shop { " + Order + " }\nnamespace GeneratorInput { public static class DynamoDbEntities { } }";

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
            + "namespace GeneratorInput { public static class DynamoDbEntities { } }";

        // Act
        var report = Analysis.ReportOfWholeFile(source);

        // Assert
        await Assert.That((report.StartsWith("DYNA037 Info", StringComparison.Ordinal), report.Contains("DYNA038", StringComparison.Ordinal))).IsEqualTo((true, false));
    }
}
