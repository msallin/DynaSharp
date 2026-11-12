using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;
using static DynaSharp.UnitTests.Generators.Analyzers.CodeFixRun;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes that rewrite a placeholder, applied in a real workspace to what the generator reports there, and run through the generator again.</summary>
public sealed class KeyPlaceholderCodeFixTests
{
    private const string Entity = """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("O#{Id}")]""";

    [Test]
    public async Task RegisterCodeFixes_IntegerWithoutFormatInASortKey_OffersTheWidthOfItsMaximumAndPlain()
    {
        // Arrange
        var files = Order("\"Q#{Qty}\"", "    public int Qty { get; set; }\n");

        // Act
        var titles = await Run("DYNA108").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Change to {Qty:d10}", "Change to {Qty:n10}", "Change to {Qty:plain}"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_DecimalWithoutFormatInASortKey_OffersPlain()
    {
        // Arrange
        var files = Order("\"P#{Price}\"", "    public decimal Price { get; set; }\n");

        // Act
        var titles = await Run("DYNA108").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Change to {Price:plain}"]);
    }

    [Test]
    [Arguments("\"Q#{Qty}\"", "\"Q#{Qty:d5}\"")]
    [Arguments("@\"Q#{Qty}\"", "@\"Q#{Qty:d5}\"")]
    [Arguments("\"\"\"Q#{Qty}\"\"\"", "\"\"\"Q#{Qty:d5}\"\"\"")]
    [Arguments("\"{{Qty}}#{Qty}\"", "\"{{Qty}}#{Qty:d5}\"")]
    [Arguments("\"{Q}#{Qty}\"", "\"{Q}#{Qty:d5}\"")]
    public async Task Fix_Digits_WritesTheFormatIntoTheLiteral(string sortKey, string expected)
    {
        // Arrange
        var files = Order(sortKey, "    public required string Q { get; set; }\n    public ushort Qty { get; set; }\n");

        // Act
        var fixedFiles = await Run("DYNA108").FixAsync("DYNA108.d", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(sortKey, expected, StringComparison.Ordinal));
    }

    [Test]
    [Arguments("DYNA108.d")]
    [Arguments("DYNA108.n")]
    [Arguments("DYNA108.plain")]
    public async Task Fix_Result_HasNoDiagnosticLeft(string key)
    {
        // Arrange
        var files = Order("\"Q#{Qty}\"", "    public long Qty { get; set; }\n");

        // Act
        var fixedFiles = await Run("DYNA108").FixAsync(key, files);

        // Assert
        await Assert.That(await Run("DYNA108").LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task Fix_FileWithCrLf_KeepsCrLf()
    {
        // Arrange
        var source = Order("\"Q#{Qty}\"", "    public int Qty { get; set; }\n")[0].Item2.Replace("\n", "\r\n", StringComparison.Ordinal);

        // Act
        var fixedFiles = await Run("DYNA108").FixAsync("DYNA108.plain", ("Order.cs", source));

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(source.Replace("{Qty}", "{Qty:plain}", StringComparison.Ordinal));
    }

    [Test]
    public async Task RegisterCodeFixes_TemplateOfAConstant_OffersNothing()
    {
        // Arrange
        var files = Order("Template", "    private const string Template = \"Q#{Qty}\";\n    public int Qty { get; set; }\n");

        // Act
        var titles = await Run("DYNA108").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_EntityOfAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", Order("\"Q#{Qty}\"", "    public int Qty { get; set; }\n")[0].Item2)];

        // Act
        var titles = await Run("DYNA108").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    [Arguments("int", "{Qty:x2}#x", new[] { "Change to {Qty}", "Change to {Qty:d10}", "Change to {Qty:n10}" })]
    [Arguments("byte", "{Qty:x2}", new[] { "Change to {Qty}", "Change to {Qty:d3}", "Change to {Qty:n3}" })]
    [Arguments("decimal", "{Qty:d2}", new[] { "Change to {Qty}" })]
    [Arguments("System.Guid", "{Qty:N}", new[] { "Change to {Qty}" })]
    [Arguments("string", "{Qty:upper}", new[] { "Change to {Qty}", "Change to {Qty:raw}" })]
    [Arguments(
        "System.DateTimeOffset",
        "{Qty:HH}",
        new[] { "Change to {Qty:yyyy}", "Change to {Qty:yyyy-MM}", "Change to {Qty:yyyy-MM-dd}", "Change to {Qty:yyyy-MM-ddTHH}", "Change to {Qty:yyyy-MM-ddTHH:mm}", "Change to {Qty:s}", "Change to {Qty:ms}", "Change to {Qty:us}", "Change to {Qty}" })]
    public async Task RegisterCodeFixes_FormatThatDoesNotFit_OffersEachFormatOfTheType(string type, string placeholder, string[] expected)
    {
        // Arrange
        var files = Order("\"Q#" + placeholder + "\"", "    public " + type + " Qty { get; set; } = default!;\n");

        // Act
        var titles = await Run("DYNA104").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_StringThatDoesNotEndTheTemplate_OffersNoRaw()
    {
        // Arrange
        var files = Order("\"Q#{Name:upper}#{Id}\"", "    public required string Name { get; set; }\n");

        // Act
        var titles = await Run("DYNA104").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Change to {Name}"]);
    }

    [Test]
    [Arguments("[System.Flags] public enum Status { A = 1, B = 2, C = 4, D = 8 }", "{Status:x}", "DYNA104", "Change to {Status:d2}")]
    [Arguments("[System.Flags] public enum Status { A = 1, B = 2, C = 4, D = 8 }", "{Status}", "DYNA113", "Change to {Status:d2}")]
    [Arguments("public enum Status { A = 1, B = 1, C = 120 }", "{Status}", "DYNA113", "Change to {Status:d3}")]
    [Arguments("public enum Status { }", "{Status}", "DYNA124", "Change to {Status:d1}")]
    public async Task RegisterCodeFixes_EnumWhoseNamesAreNotOneToOne_OffersTheDigitsOfItsLargestValue(string declaration, string placeholder, string id, string expected)
    {
        // Arrange
        var files = Order("\"S#" + placeholder + "\"", "    public Status Status { get; set; }\n", declaration + "\n");

        // Act
        var titles = await Run(id).TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo([expected]);
    }

    [Test]
    public async Task RegisterCodeFixes_EnumWithANegativeMember_OffersNothing()
    {
        // Arrange
        var files = Order("\"S#{Status}\"", "    public Status Status { get; set; }\n", "public enum Status { A = -1, B = -1 }\n");

        // Act
        var titles = await Run("DYNA113").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    [Arguments("DYNA104", "\"S#{Status:x}\"", "DYNA104.d")]
    [Arguments("DYNA113", "\"S#{Status}\"", "DYNA113.d")]
    public async Task Fix_EnumWhoseNamesAreNotOneToOne_HasNoDiagnosticLeft(string id, string sortKey, string key)
    {
        // Arrange
        var files = Order(sortKey, "    public Status Status { get; set; }\n", "public enum Status { A = 1, B = 1 }\n");

        // Act
        var fixedFiles = await Run(id).FixAsync(key, files);

        // Assert
        await Assert.That(await Run(id).LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task Fix_TruncatedTimestampAtTheEnd_DropsTheFormat()
    {
        // Arrange
        var files = Order("\"AT#{At:ms}\"", "    public System.DateTimeOffset At { get; set; }\n");

        // Act
        var fixedFiles = await Run("DYNA116").FixAsync("DYNA116.none", files);

        // Assert
        await Assert.That((fixedFiles["Order.cs"].Contains("[SortKey(\"AT#{At}\")]", StringComparison.Ordinal), (await Run("DYNA116").LeftAsync(fixedFiles, files)).Count)).IsEqualTo((true, 0));
    }

    [Test]
    [Arguments("{Custmer}", new[] { "Change to {Customer}" })]
    [Arguments("{customer:raw}", new[] { "Change to {Customer:raw}" })]
    [Arguments("{Nate}", new[] { "Change to {Name}", "Change to {Note}" })]
    [Arguments("{Quantites}", new[] { "Change to {Quantity}" })]
    public async Task RegisterCodeFixes_UnknownProperty_OffersTheCloseSpellings(string placeholder, string[] expected)
    {
        // Arrange
        var files = Order("\"C#" + placeholder + "\"", "    public required string Customer { get; set; }\n    public required string Name { get; set; }\n    public required string Note { get; set; }\n    public int Quantity { get; set; }\n");

        // Act
        var titles = await Run("DYNA102").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_UnknownPropertyFarFromEveryName_OffersNothing()
    {
        // Arrange
        var files = Order("\"C#{Warehouse}\"", "    public required string Customer { get; set; }\n");

        // Act
        var titles = await Run("DYNA102").TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task Fix_UnknownProperty_HasNoDiagnosticLeft()
    {
        // Arrange
        var files = Order("\"C#{Custmer}\"", "    public required string Customer { get; set; }\n");

        // Act
        var fixedFiles = await Run("DYNA102").FixAsync("DYNA102.0", files);

        // Assert
        await Assert.That(await Run("DYNA102").LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    [Arguments(FixAllScope.Solution)]
    public async Task FixAll_Plain_FixesEveryPlaceholderOfTheScope(FixAllScope scope)
    {
        // Arrange
        var first = Order("\"Q#{Qty}#{Size}\"", "    public int Qty { get; set; }\n    public int Size { get; set; }\n");

        // Act
        var fixedFiles = await Run("DYNA108").FixAllAsync("DYNA108.plain", scope, first);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("[SortKey(\"Q#{Qty:plain}#{Size:plain}\")]");
    }

    [Test]
    public async Task FixAll_Solution_FixesEachProject()
    {
        // Arrange
        var first = Order("\"Q#{Qty}\"", "    public int Qty { get; set; }\n");
        var second = Order("\"Q#{Qty}\"", "    public int Qty { get; set; }\n");

        // Act
        var fixedFiles = await Run("DYNA108").FixAllAsync("DYNA108.d", FixAllScope.Solution, first, second);

        // Assert
        await Assert.That(fixedFiles.Keys).IsEquivalentTo(["Consumer0/Order.cs", "Consumer1/Order.cs"]);
    }

    [Test]
    [Arguments("DYNA104", "{X:yyyy-MM}", 0, "DYNA104.yyyy-MM")]
    [Arguments("DYNA104", "{X:s}", 0, "DYNA104.s")]
    [Arguments("DYNA104", "{X:n10}", 0, "DYNA104.n")]
    [Arguments("DYNA116", "{X}", 0, "DYNA116.none")]
    [Arguments("DYNA102", "{Y}", 2, "DYNA102.2")]
    public async Task Key_Replacement_IsTheKindOfItsFormat(string id, string placeholder, int rank, string expected)
    {
        // Arrange
        // Act
        var key = KeyPlaceholderCodeFix.Key(id, placeholder, rank);

        // Assert
        await Assert.That(key).IsEqualTo(expected);
    }

    [Test]
    public async Task FixableDiagnosticIds_AreTheRulesOfAPlaceholder()
    {
        // Arrange
        var fix = new KeyPlaceholderCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA102", "DYNA104", "DYNA108", "DYNA113", "DYNA116", "DYNA124"]);
    }

    private static CodeFixRun Run(string id) => new(new KeyPlaceholderCodeFix(), id);

    private static (string, string)[] Order(string sortKey, string members, string outside = "") =>
        One(Entity + "\n[SortKey(" + sortKey + ")]\npublic partial class Order\n{\n    public required string Id { get; set; }\n" + members + "}\n" + outside);
}
