using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA237, applied in a real workspace to what the analyzer reports there, and analyzed and compiled again.</summary>
public sealed class DateTimeKindCodeFixTests
{
    private const string Key = DateTimeKindCodeFix.Key;

    private const string Header = "using DynaSharp.Mapping;\n\nnamespace Shop;\n\n";

    private const string Model = """
        [DynamoDbEntity, Discriminator("SHIFT"), PartitionKey("SHIFT#{Id}")]
        public partial record Shift
        {
            public required string Id { get; init; }

            public required System.DateTime Start { get; init; }
        }

        public static class Code
        {
            public static Shift Plan() => new Shift { Id = "a", Start =
        """;

    private static readonly CodeFixRun Run = new(new DateTimeKindCodeFix(), "DYNA237", new DateTimeKindAnalyzer());

    [Test]
    [Arguments("new DateTime(2026, 1, 15)", "new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)")]
    [Arguments("new DateTime(2026, 1, 15, 8, 30, 0)", "new DateTime(2026, 1, 15, 8, 30, 0, DateTimeKind.Utc)")]
    [Arguments("new DateTime(2026, 1, 15, 8, 30, 0, 250)", "new DateTime(2026, 1, 15, 8, 30, 0, 250, DateTimeKind.Utc)")]
    [Arguments("new DateTime(638_000_000_000_000_000)", "new DateTime(638_000_000_000_000_000, DateTimeKind.Utc)")]
    [Arguments("new DateTime(new DateOnly(2026, 1, 15), new TimeOnly(8, 30))", "new DateTime(new DateOnly(2026, 1, 15), new TimeOnly(8, 30), DateTimeKind.Utc)")]
    [Arguments("new(2026, 1, 15)", "new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)")]
    public async Task Fix_ConstructorWithoutAKind_PassesUtc(string creation, string expected)
    {
        // Arrange
        var source = "using System;\n" + Header + Model + " " + creation + " };\n}\n";

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo("using System;\n" + Header + Model + " " + expected + " };\n}\n");
    }

    [Test]
    public async Task Fix_FileWithoutTheSystemNamespace_NamesTheKindInFull()
    {
        // Arrange
        var source = Header + Model + " new System.DateTime(2026, 1, 15) };\n}\n";

        // Act
        var fixedSource = await Run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).Contains("new System.DateTime(2026, 1, 15, 0, 0, 0, global::System.DateTimeKind.Utc)");
    }

    [Test]
    public async Task Fix_Result_HasNothingLeftToReport()
    {
        // Arrange
        var source = "using System;\n" + Header + Model + " new DateTime(2026, 1, 15) };\n}\n";

        // Act
        var fixedFiles = await Run.FixAsync(Key, ("Code.cs", source));
        var reported = await Run.ReportedAsync(fixedFiles);

        // Assert
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    [Arguments("new DateTime(year: 2026, month: 1, day: 15)")]
    [Arguments("new DateTime(2026, 1, 15, new System.Globalization.GregorianCalendar())")]
    public async Task RegisterCodeFixes_NamedArgumentsOrACalendar_OffersNothing(string creation)
    {
        // Arrange
        var source = "using System;\n" + Header + Model + " " + creation + " };\n}\n";

        // Act
        var (solution, diagnostics) = await Run.RunAsync([("Code.cs", source)]);
        var actions = await Run.ActionsAsync(solution, diagnostics.Single());

        // Assert
        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Constructor_OffersUtc()
    {
        // Arrange
        var source = "using System;\n" + Header + Model + " new DateTime(2026, 1, 15) };\n}\n";

        // Act
        var titles = await Run.TitlesAsync(("Code.cs", source));

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Pass DateTimeKind.Utc"]);
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Solution)]
    public async Task FixAll_TwoConstructors_PassesUtcToEach(FixAllScope scope)
    {
        // Arrange
        var source = "using System;\n" + Header + Model + " new DateTime(2026, 1, 15) };\n\n    public static Shift Late() => new Shift { Id = \"b\", Start = new DateTime(2026, 1, 15, 22, 0, 0) };\n}\n";

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, scope, [("Code.cs", source)]);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Code.cs"]).IsEqualTo(source
            .Replace("(2026, 1, 15)", "(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)", StringComparison.Ordinal)
            .Replace("(2026, 1, 15, 22, 0, 0)", "(2026, 1, 15, 22, 0, 0, DateTimeKind.Utc)", StringComparison.Ordinal));
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new DateTimeKindCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA237"]);
    }
}
