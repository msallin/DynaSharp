using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>What the emitters generate for every shape of <see cref="EmitterShapes"/>, and that it compiles for a consumer with warnings as errors.</summary>
public sealed class EntityEmitterTests
{
    public static IEnumerable<(string Shape, LanguageVersion Language)> ShapesAtEachLanguageVersion() =>
        EmitterShapes.Names().SelectMany(static shape => new[] { (shape, LanguageVersion.CSharp11), (shape, LanguageVersion.Latest) });

    [Test]
    [MethodDataSource(typeof(EmitterShapes), nameof(EmitterShapes.Names))]
    public void Run_Shape_MatchesTheVerifiedSnapshots(string shape)
    {
        // Arrange, Act
        var result = GeneratorRun.Generate(EmitterShapes.All[shape], new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        foreach (var source in result.Results[0].GeneratedSources)
        {
            SnapshotComparer.Verify($"{shape}.{source.HintName.Replace(".g.cs", ".g", StringComparison.Ordinal)}", source.SourceText.ToString());
        }
    }

    [Test]
    [MethodDataSource(nameof(ShapesAtEachLanguageVersion))]
    public async Task Run_Shape_CompilesWithoutWarnings(string shape, LanguageVersion language)
    {
        // Arrange, Act
        var output = GeneratorRun.Run(EmitterShapes.All[shape], new DynaSharpGenerator(), language);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    [Arguments("sbyte")]
    [Arguments("byte")]
    [Arguments("short")]
    [Arguments("ushort")]
    [Arguments("int")]
    [Arguments("uint")]
    [Arguments("long")]
    [Arguments("ulong")]
    public async Task Run_VersionOfEachIntegerType_CompilesWithoutWarnings(string type)
    {
        // Arrange
        var source = EmitterShapes.Versioned.Replace("public short Revision", $"public {type} Revision", StringComparison.Ordinal);

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.CSharp11);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    [Arguments("sbyte")]
    [Arguments("byte")]
    [Arguments("short")]
    [Arguments("ushort")]
    [Arguments("int")]
    [Arguments("uint")]
    [Arguments("long")]
    [Arguments("ulong")]
    public async Task Run_VersionThatAllowsAnItemWithoutOneOfEachIntegerType_CompilesWithoutWarnings(string type)
    {
        // Arrange
        var source = EmitterShapes.VersionAllowed.Replace("public required short Revision", $"public required {type} Revision", StringComparison.Ordinal);

        // Act
        var output = GeneratorRun.Run(source, new DynaSharpGenerator(), LanguageVersion.CSharp11);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    [Test]
    public async Task Run_VersionOfABaseEntity_CompilesForBothEntities()
    {
        // Arrange
        const string Source = EmitterShapes.Usings + """
            namespace Shapes.VersionedBase
            {
                [DynamoDbEntity]
                [Discriminator("BASE")]
                [PartitionKey("{Id}")]
                public partial class Base
                {
                    public string Id { get; set; } = "none";

                    [Version]
                    public int Revision { get; set; }
                }

                [DynamoDbEntity]
                [Discriminator("DERIVED")]
                [PartitionKey("{Id}")]
                public partial class Derived : Base
                {
                    public string? Extra { get; set; }
                }
            }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), LanguageVersion.Latest);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }
}
