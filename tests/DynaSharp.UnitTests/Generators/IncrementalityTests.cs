using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// The pipeline recomputes only what a change touches. Reasons: Unchanged and Cached mean the step reused its result, New and
/// Modified mean it ran with a different input, Removed means the input is gone.
/// </summary>
public sealed class IncrementalityTests
{
    private const string Reused = "Entities: Unchanged; Documents: Unchanged; Models: Cached; Diagnostics: ; output: Cached";

    private const string Status = """
        namespace Shop
        {
            public enum Status { Open }
        }
        """;

    private const string Converter = """
        namespace Shop
        {
            using System;
            using DynaSharp;
            using DynaSharp.Serialization;

            public readonly struct MoneyConverter : IDynamoDbConverter<decimal>
            {
                public static void Write(DynamoDbWriter writer, decimal value) { }

                public static decimal Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
        }
        """;

    private const string Ticket = """
        namespace Shop
        {
            using DynaSharp.Mapping;

            [DynamoDbEntity]
            [Discriminator("TICKET")]
            [PartitionKey("TICKET#{Id}")]
            public partial class Ticket
            {
                public string Id { get; set; } = "";

                public Status Status { get; set; }

                [DynamoDbConverter(typeof(MoneyConverter))]
                public decimal Price { get; set; }
            }
        }
        """;

    private static readonly string Source = TestSources.Compose(TestSources.Order, TestSources.Address);

    private static CSharpParseOptions Options => GeneratorRun.ParseOptions(LanguageVersion.Latest);

    [Test]
    public async Task Run_UnrelatedFileAdded_ReusesEveryStep()
    {
        // Arrange
        var edit = (CSharpCompilation compilation) => compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("// unrelated", Options));

        // Act
        var run = RunTwice(Source, edit);

        // Assert
        await Assert.That(Summary(run)).IsEqualTo(Reused);
    }

    [Test]
    public async Task Run_UnrelatedFileEdited_ReusesEveryStep()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation(Source, LanguageVersion.Latest).AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Other { }", Options, "Other.cs"));
        var driver = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true).RunGenerators(compilation);
        var edited = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees[1], CSharpSyntaxTree.ParseText("class Other { int X; }", Options, "Other.cs"));

        // Act
        var run = driver.RunGenerators(edited).GetRunResult().Results[0];

        // Assert
        await Assert.That(Summary(run)).IsEqualTo(Reused);
    }

    [Test]
    public async Task Run_MethodBodyEditedInTheEntityFile_ModelIsUnchangedAndOutputIsCached()
    {
        // Arrange
        var edit = (CSharpCompilation compilation) => Replace(compilation, 0, "=> 1;", "=> 2;");

        // Act
        var run = RunTwice(Source, edit);

        // Assert
        await Assert.That(Summary(run)).IsEqualTo(Reused);
    }

    [Test]
    public async Task Run_PropertyAddedToOneEntity_ThatEntityIsModifiedAndTheOtherIsCached()
    {
        // Arrange
        var edit = (CSharpCompilation compilation) => compilation
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Shop { public partial class Order { public string Added { get; set; } = \"\"; } }", Options, "Added.cs"));

        // Act
        var run = RunTwice(TestSources.Compose(TestSources.Order, TestSources.Address, Status, Converter, Ticket), edit);

        // Assert
        await Assert.That((ReasonsOf(run, TrackingNames.Entities), ReasonsOf(run, TrackingNames.Models))).IsEqualTo(("Modified, Unchanged", "Cached, Modified"));
    }

    [Test]
    public async Task Run_EnumMemberAddedInAnotherFile_TheEntityUsingItIsModified()
    {
        // Arrange
        var first = CSharpSyntaxTree.ParseText(Status, Options, "Status.cs");
        var compilation = GeneratorRun.CreateCompilation(TestSources.Compose(TestSources.Order, TestSources.Address, Converter, Ticket), LanguageVersion.Latest)
            .AddSyntaxTrees(first);
        var driver = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true).RunGenerators(compilation);
        var edited = compilation.ReplaceSyntaxTree(first, CSharpSyntaxTree.ParseText(Status.Replace("Open", "Open, Closed", StringComparison.Ordinal), Options, "Status.cs"));

        // Act
        var run = driver.RunGenerators(edited).GetRunResult().Results[0];

        // Assert
        await Assert.That((ReasonsOf(run, TrackingNames.Entities), ReasonsOf(run, TrackingNames.Models))).IsEqualTo(("Modified, Unchanged", "Cached, Modified"));
    }

    [Test]
    public async Task Run_UnrelatedMemberAddedToACustomConverter_ModelIsUnchanged()
    {
        // Arrange
        var first = CSharpSyntaxTree.ParseText(Converter, Options, "Converter.cs");
        var compilation = GeneratorRun.CreateCompilation(TestSources.Compose(TestSources.Order, TestSources.Address, Status, Ticket), LanguageVersion.Latest)
            .AddSyntaxTrees(first);
        var driver = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true).RunGenerators(compilation);
        var changed = Converter.Replace("public static void Write", "public static int Other() => 1;\n\n public static void Write", StringComparison.Ordinal);
        var edited = compilation.ReplaceSyntaxTree(first, CSharpSyntaxTree.ParseText(changed, Options, "Converter.cs"));

        // Act
        var run = driver.RunGenerators(edited).GetRunResult().Results[0];

        // Assert
        await Assert.That((ReasonsOf(run, TrackingNames.Entities), OutputReasons(run))).IsEqualTo(("Unchanged", "Cached"));
    }

    [Test]
    public async Task Run_EntityAdded_ReportsTheNewTypeAndKeepsTheDocumentUnchanged()
    {
        // Arrange
        var edit = (CSharpCompilation compilation) => compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(TestSources.Invoice, Options, "Invoice.cs"));

        // Act
        var run = RunTwice(Source, edit);

        // Assert
        await Assert.That((ReasonsOf(run, TrackingNames.Entities), ReasonsOf(run, TrackingNames.Documents))).IsEqualTo(("New, Unchanged", "Unchanged"));
    }

    [Test]
    public async Task Run_EveryTrackedStep_HoldsNoSymbolsAndNoPlainArrays()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation(TestSources.Compose(TestSources.SectionFourOrder, TestSources.Invoice), LanguageVersion.Latest);
        var driver = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true).RunGenerators(compilation);

        // Act
        var run = driver.GetRunResult().Results[0];
        var violations = TrackingStepNames()
            .SelectMany(name => run.TrackedSteps[name])
            .SelectMany(step => step.Outputs)
            .SelectMany(output => TrackedOutputWalker.Violations(output.Value))
            .ToList();

        // Assert
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Run_EveryTrackingName_IsReportedWithAtLeastOneOutput()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation(TestSources.Compose(TestSources.Order, TestSources.Address, TestSources.Invoice), LanguageVersion.Latest);
        var driver = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true).RunGenerators(compilation);

        // Act
        var run = driver.GetRunResult().Results[0];
        var empty = TrackingStepNames().Where(name => !run.TrackedSteps.TryGetValue(name, out var steps) || steps.All(step => step.Outputs.Length == 0));

        // Assert
        await Assert.That(empty).IsEmpty();
    }

    private static IEnumerable<string> TrackingStepNames() => typeof(TrackingNames)
        .GetFields()
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!);

    private static GeneratorRunResult RunTwice(string source, Func<CSharpCompilation, CSharpCompilation> edit)
    {
        var compilation = GeneratorRun.CreateCompilation(source, LanguageVersion.Latest);
        var driver = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest, trackSteps: true).RunGenerators(compilation);
        driver = driver.RunGenerators(edit(compilation));
        return driver.GetRunResult().Results[0];
    }

    private static CSharpCompilation Replace(CSharpCompilation compilation, int tree, string oldText, string newText)
    {
        var old = compilation.SyntaxTrees[tree];
        return compilation.ReplaceSyntaxTree(old, CSharpSyntaxTree.ParseText(old.ToString().Replace(oldText, newText, StringComparison.Ordinal), Options));
    }

    // One line for the whole run: what each tracked step did, as "Entities: New, Unchanged; ...".
    private static string Summary(GeneratorRunResult run) => string.Join(
        "; ",
        TrackingStepNames().Select(name => $"{name}: {ReasonsOf(run, name)}").Append($"output: {OutputReasons(run)}"));

    // The distinct reasons of the named step, sorted and joined: "New, Unchanged".
    private static string ReasonsOf(GeneratorRunResult run, string name) => Describe(run.TrackedSteps.TryGetValue(name, out var steps) ? steps : []);

    private static string OutputReasons(GeneratorRunResult run) => Describe(run.TrackedOutputSteps.Values.SelectMany(steps => steps));

    private static string Describe(IEnumerable<IncrementalGeneratorRunStep> steps) => string.Join(
        ", ",
        steps.SelectMany(step => step.Outputs).Select(output => output.Reason.ToString()).Distinct().Order(StringComparer.Ordinal));
}
