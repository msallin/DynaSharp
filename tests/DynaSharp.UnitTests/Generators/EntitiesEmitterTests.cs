using DynaSharp.Generators;
using DynaSharp.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>What <c>IncludingAll()</c> of an assembly looks like, that it compiles, and what it does to a table.</summary>
public sealed class EntitiesEmitterTests
{
    private const string Order = """
        namespace Shop
        {
            using DynaSharp.Mapping;

            [DynamoDbEntity, Discriminator("ORDER"), PartitionKey("CUSTOMER#{CustomerId}"), SortKey("ORDER#{Number:d6}")]
            [GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "ORDER#{Number:d6}")]
            public partial record Order
            {
                public required string CustomerId { get; init; }

                public required int Number { get; init; }

                public string Status { get; init; } = "open";
            }
        }
        """;

    private const string Customer = """
        namespace Shop.Accounts
        {
            using DynaSharp.Mapping;

            [DynamoDbEntity, Discriminator("CUSTOMER"), PartitionKey("CUSTOMER#{Id}"), SortKey("PROFILE")]
            [GlobalIndexKey("ByEmail", PartitionKey = "EMAIL#{Email}")]
            internal partial record Customer
            {
                public required string Id { get; init; }

                public required string Email { get; init; }
            }
        }
        """;

    private const string Run = """
        namespace Shop
        {
            using System.Threading;
            using System.Threading.Tasks;
            using DynaSharp;

            public static class Code
            {
                public static async Task<CreateTableResult> RunAsync(DynamoDbClient client) => await client.Table("Shop", "pk", "sk").CreateTable().IncludingAll().SendAsync(CancellationToken.None);
            }
        }
        """;

    [Test]
    public void Run_Entities_MatchesTheVerifiedSnapshot()
    {
        // Arrange, Act
        var result = GeneratorRun.Generate(Order + Customer, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        SnapshotComparer.Verify("IncludingAll", Generated(result));
    }

    [Test]
    public async Task Run_CodeInTheGlobalNamespace_FindsIncludingAllWithUsingDynaSharp()
    {
        // Arrange (the global namespace is where top-level statements stand, which no namespace of the assembly covers)
        const string Global = """
            using DynaSharp;

            public static class Setup
            {
                public static CreateTableCommand Command(DynamoDbClient client) => client.Table("Shop", "pk", "sk").CreateTable().IncludingAll();
            }
            """;

        // Act
        var output = GeneratorRun.Run(Global + Order + Customer, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    [Arguments(LanguageVersion.CSharp11)]
    [Arguments(LanguageVersion.Latest)]
    public async Task Run_Entities_CompilesWithoutWarnings(LanguageVersion language)
    {
        // Arrange, Act
        var output = GeneratorRun.Run(Order + Customer + Run, new DynaSharpGenerator(), language);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Run_SourcesInAnotherOrder_GeneratesTheSameFile()
    {
        // Arrange
        var first = Generated(GeneratorRun.Generate(Order + Customer, new DynaSharpGenerator(), LanguageVersion.Latest));

        // Act
        var second = Generated(GeneratorRun.Generate(Customer + Order, new DynaSharpGenerator(), LanguageVersion.Latest));

        // Assert
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    [Arguments("Lps.LabelPrintServer")]
    [Arguments("My-App.2.class")]
    [Arguments(null)]
    public async Task Run_AnyAssemblyName_PutsTheClassInDynaSharp(string? assemblyName)
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation(Order, LanguageVersion.Latest).WithAssemblyName(assemblyName);

        // Act
        GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        // Assert (and it compiles)
        var errors = output.GetDiagnostics().Count(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) + diagnostics.Length;
        await Assert.That((Generated(output).Contains("namespace DynaSharp;", StringComparison.Ordinal), errors)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Run_DocumentsAlone_GeneratesNoClass()
    {
        // Arrange
        const string Source = "namespace Shop { [DynaSharp.Mapping.DynamoDbDocument] public partial class Address { public string City { get; set; } = \"\"; } }";

        // Act
        var result = GeneratorRun.Generate(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That((result.Diagnostics.Length, result.Results[0].GeneratedSources.Count(static source => source.HintName == EntitiesEmitter.HintName))).IsEqualTo((0, 0));
    }

    [Test]
    public async Task IncludingAll_OnATableWithoutAModel_CreatesTheIndexesOfEveryEntity()
    {
        // Arrange (the very generated code, compiled and run against the fake)
        var compilation = GeneratorRun.CreateCompilation(Order + Customer + Run, LanguageVersion.Latest);
        GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        using var image = new MemoryStream();
        var emitted = output.Emit(image);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
        }

        var code = System.Reflection.Assembly.Load(image.ToArray()).GetType("Shop.Code")!;
        using var client = new FakeDynamoDb().CreateClient();

        // Act
        var result = await (Task<CreateTableResult>)code.GetMethod("RunAsync")!.Invoke(null, [client])!;

        // Assert
        await Assert.That(string.Join(", ", result.Table.GlobalSecondaryIndexes!.Select(static index => index.IndexName).Order(StringComparer.Ordinal))).IsEqualTo("ByEmail, ByStatus");
    }

    private static string Generated(GeneratorDriverRunResult result) =>
        result.Results[0].GeneratedSources.Single(static source => source.HintName == EntitiesEmitter.HintName).SourceText.ToString();

    private static string Generated(Compilation compilation) =>
        compilation.SyntaxTrees.Single(static tree => tree.FilePath.EndsWith(EntitiesEmitter.HintName, StringComparison.Ordinal)).ToString();
}
