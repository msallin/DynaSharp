using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA020, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class DeclareDiscriminatorCodeFixTests
{
    private const string Value = DeclareDiscriminatorCodeFix.ValueKey;

    private const string OptOut = DeclareDiscriminatorCodeFix.OptOutKey;

    private const string Project = DeclareDiscriminatorCodeFix.ProjectKey;

    private const string Header = "using DynaSharp.Mapping;\n\nnamespace Shop;\n\n";

    private const string Members = " { public required string Id { get; set; } }";

    private static readonly CodeFixRun Run = new(new DeclareDiscriminatorCodeFix(), "DYNA020");

    [Test]
    [Arguments(Value, """[DynamoDbEntity, Discriminator("ORDER_LINE"), PartitionKey("LINE#{Id}")]""")]
    [Arguments(OptOut, """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("LINE#{Id}")]""")]
    public async Task Fix_EntityFirstInAList_AddsTheDiscriminatorRightAfterIt(string key, string expected)
    {
        // Arrange
        var files = One("""[DynamoDbEntity, PartitionKey("LINE#{Id}")] public partial class OrderLine""" + Members);

        // Act
        var fixedFiles = await FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Header + expected + " public partial class OrderLine" + Members);
    }

    [Test]
    public async Task Fix_EntityLastInAList_AppendsTheDiscriminator()
    {
        // Arrange
        var files = One("""[PartitionKey("E#{Id}"), DynamoDbEntity(Table = "shop")] public partial class Entry""" + Members);

        // Act
        var fixedFiles = await FixAsync(Value, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Header + """[PartitionKey("E#{Id}"), DynamoDbEntity(Table = "shop"), Discriminator("ENTRY")] public partial class Entry""" + Members);
    }

    [Test]
    public async Task Fix_ListWithOneAttributePerLine_PutsTheDiscriminatorOnALineOfItsOwn()
    {
        // Arrange
        var files = One("[DynamoDbEntity,\n    PartitionKey(\"E#{Id}\")]\npublic partial class Entry" + Members);

        // Act
        var fixedFiles = await FixAsync(Value, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Header + "[DynamoDbEntity,\n    Discriminator(\"ENTRY\"),\n    PartitionKey(\"E#{Id}\")]\npublic partial class Entry" + Members);
    }

    [Test]
    public async Task Fix_EntityAloneOnItsLineInANestedType_AddsALineWithItsIndent()
    {
        // Arrange
        var files = One("public static partial class Outer\n{\n    /// <summary>An entry.</summary>\n    [DynamoDbEntity]\n    [PartitionKey(\"E#{Id}\")]\n    public partial class Entry" + Members + "\n}");

        // Act
        var fixedFiles = await FixAsync(Value, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Header + "public static partial class Outer\n{\n    /// <summary>An entry.</summary>\n    [DynamoDbEntity]\n    [Discriminator(\"ENTRY\")]\n    [PartitionKey(\"E#{Id}\")]\n    public partial class Entry" + Members + "\n}");
    }

    [Test]
    public async Task Fix_EntityAloneInAListOnTheLineOfTheType_AddsAListOnThatLine()
    {
        // Arrange
        var files = One("""[DynamoDbEntity] [PartitionKey("E#{Id}")] public partial class Entry""" + Members);

        // Act
        var fixedFiles = await FixAsync(OptOut, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(Header + """[DynamoDbEntity] [Discriminator(OptOut = true)] [PartitionKey("E#{Id}")] public partial class Entry""" + Members);
    }

    [Test]
    public async Task Fix_FileWithCrLf_KeepsCrLf()
    {
        // Arrange
        var source = (Header + "[DynamoDbEntity]\n[PartitionKey(\"E#{Id}\")]\npublic partial class Entry" + Members + "\n").Replace("\n", "\r\n", StringComparison.Ordinal);

        // Act
        var fixedFiles = await FixAsync(Value, [("Order.cs", source)]);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(source.Replace("[DynamoDbEntity]\r\n", "[DynamoDbEntity]\r\n[Discriminator(\"ENTRY\")]\r\n", StringComparison.Ordinal));
    }

    [Test]
    public async Task Fix_PartialTypeInTwoFiles_EditsThePartThatIsAnEntity()
    {
        // Arrange
        (string, string)[] files = [("A.cs", Header + "public partial class Entry { public string Note { get; set; } = \"\"; }"), ("B.cs", Header + """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members)];

        // Act
        var fixedFiles = await FixAsync(Value, files);

        // Assert
        await Assert.That(fixedFiles.Keys).IsEquivalentTo(["B.cs"]);
    }

    [Test]
    public async Task Fix_AttributeThatDoesNotBindBeforeTheEntity_FindsTheEntity()
    {
        // Arrange
        var files = One("""[Missing, DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members);

        // Act
        var fixedFiles = await FixAsync(Value, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("""[Missing, DynamoDbEntity, Discriminator("ENTRY"), PartitionKey("E#{Id}")]""");
    }

    [Test]
    public async Task Fix_AttributesNamedInFull_NamesTheDiscriminatorTheSameWay()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", """[DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.PartitionKey("E#{Id}")] public partial class Entry""" + Members)];

        // Act
        var fixedFiles = await FixAsync(Value, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).StartsWith("""[DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.Discriminator("ENTRY"), """);
    }

    [Test]
    public async Task Fix_AnotherTypeTakesTheName_NamesDynaSharpsInFull()
    {
        // Arrange
        var files = One(
            """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members
            + "\n[System.AttributeUsage(System.AttributeTargets.Class)] public sealed class DiscriminatorAttribute : System.Attribute { public bool OptOut { get; set; } }");

        // Act
        var fixedFiles = await FixAsync(OptOut, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("[DynamoDbEntity, global::DynaSharp.Mapping.Discriminator(OptOut = true), ");
    }

    [Test]
    [Arguments(Value)]
    [Arguments(OptOut)]
    [Arguments(Project)]
    public async Task Fix_Result_HasNoDiscriminatorErrorLeft(string key)
    {
        // Arrange
        var files = One("""[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members);

        // Act
        var fixedFiles = await FixAsync(key, files);
        var (_, diagnostics) = await RunAsync([.. fixedFiles.Select(file => (file.Key, file.Value))]);

        // Assert
        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Id)).IsEmpty();
    }

    [Test]
    public async Task Fix_ProjectWithoutDefaults_AddsThemAfterTheUsings()
    {
        // Arrange
        var files = One("""[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members);

        // Act
        var fixedFiles = await FixAsync(Project, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(
            "using DynaSharp.Mapping;\n\n[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]\n\nnamespace Shop;\n\n" + """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members);
    }

    [Test]
    public async Task Fix_ProjectWithoutDefaultsOrUsingsOrLineEnd_AddsThemAtTheTop()
    {
        // Arrange
        (string, string)[] files = [("Usings.cs", "global using DynaSharp.Mapping;\n"), ("Order.cs", """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members)];

        // Act
        var fixedFiles = await FixAsync(Project, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo("[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]\n\n" + """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members);
    }

    [Test]
    public async Task Fix_ProjectWithOtherAttributesOfTheAssembly_AddsThemThere()
    {
        // Arrange
        (string, string)[] files = [.. One("""[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members), ("AssemblyInfo.cs", "[assembly: System.CLSCompliant(false)]\n")];

        // Act
        var fixedFiles = await FixAsync(Project, files);

        // Assert
        await Assert.That(fixedFiles["AssemblyInfo.cs"]).IsEqualTo("[assembly: System.CLSCompliant(false)]\n[assembly: DynaSharp.Mapping.DynamoDbDefaults(DiscriminatorOptOut = true)]\n");
    }

    [Test]
    [Arguments("[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase)]", "[assembly: DynamoDbDefaults(Naming = AttributeNaming.CamelCase, DiscriminatorOptOut = true)]")]
    [Arguments("[assembly: DynamoDbDefaults(DiscriminatorOptOut = false, OmitEmpty = true)]", "[assembly: DynamoDbDefaults(DiscriminatorOptOut = true, OmitEmpty = true)]")]
    [Arguments("[assembly: DynamoDbDefaults]", "[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]")]
    [Arguments("[assembly: DynamoDbDefaults()]", "[assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]")]
    [Arguments("[assembly: DynamoDbDefaults(\"positional\")]", "[assembly: DynamoDbDefaults(\"positional\", DiscriminatorOptOut = true)]")]
    public async Task Fix_ProjectWithDefaultsInAnotherFile_SetsTheOptOutThere(string defaults, string expected)
    {
        // Arrange
        (string, string)[] files = [.. One("""[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members), ("Defaults.cs", "using DynaSharp.Mapping;\n\n" + defaults + "\n")];

        // Act
        var fixedFiles = await FixAsync(Project, files);

        // Assert
        await Assert.That(fixedFiles).IsEquivalentTo(new Dictionary<string, string> { ["Defaults.cs"] = "using DynaSharp.Mapping;\n\n" + expected + "\n" });
    }

    [Test]
    public async Task RegisterCodeFixes_EntityOfAnEditableFile_OffersTheThreeInTheOrderOfTheMessage()
    {
        // Arrange
        var files = One("""[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class OrderLine""" + Members);

        // Act
        var titles = await TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(
            ["Add [Discriminator(\"ORDER_LINE\")]", "Add [Discriminator(OptOut = true)]", "Opt out every entity of the project with [assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_EntityOfAGeneratedFileAlone_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", Header + """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members)];

        // Act
        var titles = await TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_EntityOfAFileWithAnAutoGeneratedHeader_OffersTheProjectAlone()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", "// <auto-generated/>\n" + Header + """[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members), ("AssemblyInfo.cs", "[assembly: System.CLSCompliant(false)]\n")];

        // Act
        var titles = await TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Opt out every entity of the project with [assembly: DynamoDbDefaults(DiscriminatorOptOut = true)]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_DefaultsInAGeneratedFile_OffersTheTypeAlone()
    {
        // Arrange
        (string, string)[] files = [.. One("""[DynamoDbEntity, PartitionKey("E#{Id}")] public partial class Entry""" + Members), ("Defaults.g.cs", "[assembly: DynaSharp.Mapping.DynamoDbDefaults(OmitEmpty = true)]\n")];

        // Act
        var titles = await TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add [Discriminator(\"ENTRY\")]", "Add [Discriminator(OptOut = true)]"]);
    }

    [Test]
    public async Task FixAll_Value_GivesEachEntityItsOwn()
    {
        // Arrange
        (string, string)[] files = [("A.cs", Header + """[DynamoDbEntity, PartitionKey("A#{Id}")] public partial class Cart""" + Members), ("B.cs", Header + """[DynamoDbEntity, PartitionKey("B#{Id}")] public partial class Session""" + Members)];

        // Act
        var fixedFiles = await FixAllAsync(Value, FixAllScope.Project, [files]);

        // Assert
        await Assert.That((fixedFiles["Consumer0/A.cs"].Contains("Discriminator(\"CART\")", StringComparison.Ordinal), fixedFiles["Consumer0/B.cs"].Contains("Discriminator(\"SESSION\")", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_Project_AddsTheDefaultsOnce(FixAllScope scope)
    {
        // Arrange
        (string, string)[] files = [("B.cs", Header + """[DynamoDbEntity, PartitionKey("B#{Id}")] public partial class Session""" + Members), ("A.cs", Header + """[DynamoDbEntity, PartitionKey("A#{Id}")] public partial class Cart""" + Members)];

        // Act
        var fixedFiles = await FixAllAsync(Project, scope, [files]);

        // Assert
        await Assert.That(fixedFiles.Keys).IsEquivalentTo(["Consumer0/A.cs"]);
    }

    [Test]
    public async Task FixAll_ProjectInASolution_AddsTheDefaultsOncePerProjectThatNeedsThem()
    {
        // Arrange
        (string, string)[] first = [("A.cs", Header + """[DynamoDbEntity, PartitionKey("A#{Id}")] public partial class Cart""" + Members), ("B.cs", Header + """[DynamoDbEntity, PartitionKey("B#{Id}")] public partial class Session""" + Members)];
        (string, string)[] second = [("C.cs", Header + """[DynamoDbEntity, PartitionKey("C#{Id}")] public partial class Visit""" + Members)];
        (string, string)[] third = [("D.cs", Header + "public static class Plain { }")];

        // Act
        var fixedFiles = await FixAllAsync(Project, FixAllScope.Solution, [first, second, third]);

        // Assert
        await Assert.That(fixedFiles.Keys).IsEquivalentTo(["Consumer0/A.cs", "Consumer1/C.cs"]);
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new DeclareDiscriminatorCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA020"]);
    }

    private static (string, string)[] One(string declarations) => [("Order.cs", Header + declarations)];

    private static Task<(Solution Solution, ImmutableArray<Diagnostic> Diagnostics)> RunAsync(params (string Path, string Text)[][] projects) => Run.RunAsync(projects);

    private static Task<List<string>> TitlesAsync((string, string)[] files) => Run.TitlesAsync(files);

    private static Task<Dictionary<string, string>> FixAsync(string key, (string, string)[] files) => Run.FixAsync(key, files);

    private static Task<Dictionary<string, string>> FixAllAsync(string key, FixAllScope scope, (string, string)[][] projects) => Run.FixAllAsync(key, scope, projects);
}
