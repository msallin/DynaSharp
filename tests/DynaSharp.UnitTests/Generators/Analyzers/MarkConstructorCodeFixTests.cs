using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA005, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class MarkConstructorCodeFixTests
{
    private const string Two = "public A(int x) { X = x; }\n    public A(int x, string note) { X = x; Note = note; }";

    private const string Members = "\n    public int X { get; set; }\n    public string Note { get; set; } = \"\";\n}";

    private static readonly CodeFixRun Run = new(new MarkConstructorCodeFix(), "DYNA005");

    [Test]
    public async Task RegisterCodeFixes_TwoConstructors_OffersEach()
    {
        // Arrange
        var files = Document(Two);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(
            ["Mark A(int) with [DynamoDbConstructor]", "Mark A(int, string) with [DynamoDbConstructor]"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fix_ConstructorOnALineOfItsOwn_PutsTheAttributeOnALineAboveIt()
    {
        // Arrange
        var files = Document(Two);

        // Act
        var fixedFiles = await Run.FixAsync(MarkConstructorCodeFix.KeyPrefix + "1", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("public A(int x) { X = x; }\n    [DynamoDbConstructor]\n    public A(int x, string note)");
    }

    [Test]
    public async Task Fix_TwoMarked_KeepsTheAttributeOnTheOneChosen()
    {
        // Arrange
        var files = Document("public A() { }\n    [DynamoDbConstructor] public A(int x) { X = x; }\n    [DynamoDbConstructor] public A(int x, string note) { X = x; Note = note; }");

        // Act
        var fixedFiles = await Run.FixAsync(MarkConstructorCodeFix.KeyPrefix + "0", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("[DynamoDbConstructor] public A(int x) { X = x; }\n    public A(int x, string note)");
    }

    [Test]
    public async Task RegisterCodeFixes_TwoMarked_OffersTheMarkedOnes()
    {
        // Arrange
        var files = Document("public A() { }\n    [DynamoDbConstructor] public A(int x) { X = x; }\n    [DynamoDbConstructor] public A(int x, string note) { X = x; Note = note; }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Mark A(int) with [DynamoDbConstructor]", "Mark A(int, string) with [DynamoDbConstructor]"]);
    }

    [Test]
    public async Task Fix_PrimaryConstructor_PutsTheAttributeOnTheTypeForTheMethod()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial record A(int X, string Note)\n{\n    public A(int x) : this(x, \"\") { }\n}");

        // Act
        var fixedFiles = await Run.FixAsync(MarkConstructorCodeFix.KeyPrefix + "0", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).StartsWith(CodeFixRun.Header + "[DynamoDbDocument] [method: DynamoDbConstructor] public partial record A(int X, string Note)");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Fix_Result_CompilesWithoutTheError(int index)
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial record A(int X, string Note)\n{\n    public A(int x) : this(x, \"\") { }\n}");

        // Act
        var fixedFiles = await Run.FixAsync(MarkConstructorCodeFix.KeyPrefix + index, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task Fix_FileWithoutTheUsing_NamesTheAttributeInFull()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", "namespace Shop;\n\n[DynaSharp.Mapping.DynamoDbDocument]\npublic partial class A\n{\n    " + Two + Members)];

        // Act
        var fixedFiles = await Run.FixAsync(MarkConstructorCodeFix.KeyPrefix + "0", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("[global::DynaSharp.Mapping.DynamoDbConstructor]\n    public A(int x)");
    }

    [Test]
    public async Task RegisterCodeFixes_ConstructorInAGeneratedFile_OffersTheOthers()
    {
        // Arrange
        (string, string)[] files = [.. Document("public A(int x) { X = x; }"), ("A.g.cs", "namespace Shop;\n\npublic partial class A\n{\n    public A(int x, string note) { X = x; Note = note; }\n}")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Mark A(int) with [DynamoDbConstructor]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_MarkedConstructorInAGeneratedFile_OffersNoneThatRemovesIt()
    {
        // Arrange
        (string, string)[] files =
        [
            .. Document("[DynamoDbConstructor] public A(int x) { X = x; }"),
            ("A.g.cs", "using DynaSharp.Mapping;\n\nnamespace Shop;\n\npublic partial class A\n{\n    [DynamoDbConstructor] public A(int x, string note) { X = x; Note = note; }\n}"),
        ];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Mark A(int, string) with [DynamoDbConstructor]"]);
    }

    [Test]
    public async Task GetFixAllProvider_IsNone()
    {
        // Arrange
        var fix = new MarkConstructorCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsNull();
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new MarkConstructorCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA005"]);
    }

    private static (string, string)[] Document(string constructors) => CodeFixRun.One("[DynamoDbDocument]\npublic partial class A\n{\n    " + constructors + Members);
}
