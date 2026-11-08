using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA043, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class NameInFullCodeFixTests
{
    private const string Key = NameInFullCodeFix.Key;

    private const string Entity = """[DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")] public partial class E { public required string Id { get; set; } """;

    private static readonly CodeFixRun Run = new(new NameInFullCodeFix(), "DYNA043");

    [Test]
    [Arguments("public Keys Key { get; set; }", "public global::Shop.Keys Key { get; set; }")]
    [Arguments("public List<Keys> Many { get; set; } = [];", "public List<global::Shop.Keys> Many { get; set; } = [];")]
    [Arguments("public static int Code() => (int)Keys.Enter;", "public static int Code() => (int)global::Shop.Keys.Enter;")]
    public async Task Fix_NameOfAType_WritesItInFull(string member, string expected)
    {
        // Arrange
        var files = CodeFixRun.One("public enum Keys { None, Enter }\n" + Entity + member + " }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "public enum Keys { None, Enter }\n" + Entity + expected + " }");
    }

    [Test]
    public async Task Fix_NameOfAConstant_WritesItWithItsType()
    {
        // Arrange
        var files = CodeFixRun.One(
            "public static class Names { public const string TableName = \"orders\"; }\n"
            + "[DynamoDbEntity(Table = \"carts\"), Discriminator(\"E\"), PartitionKey(\"E\")] public partial class E { public static string Other => TableName; }");
        files[0].Item2 = "using static Shop.Names;\n" + files[0].Item2;

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).EndsWith("public static string Other => global::Shop.Names.TableName; }");
    }

    [Test]
    public async Task Fix_Result_CompilesWithoutTheError()
    {
        // Arrange
        var files = CodeFixRun.One("public enum Keys { None, Enter }\n" + Entity + "public Keys Key { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Name_OffersToWriteItInFull()
    {
        // Arrange
        var files = CodeFixRun.One("public enum Keys { None, Enter }\n" + Entity + "public Keys Key { get; set; } }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Write global::Shop.Keys"]);
    }

    [Test]
    public async Task RegisterCodeFixes_NameInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", CodeFixRun.Header + "public enum Keys { None, Enter }\n" + Entity + "public Keys Key { get; set; } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_WritesEveryNameInFull()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "public enum Keys { None, Enter }\n" + Entity + "public Keys Key { get; set; } public Keys Other { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).EndsWith("public global::Shop.Keys Key { get; set; } public global::Shop.Keys Other { get; set; } }");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new NameInFullCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA043"]);
    }
}
