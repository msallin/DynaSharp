using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA019, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class EnableNullableCodeFixTests
{
    private const string Key = EnableNullableCodeFix.Key;

    private const string Usings = "using DynaSharp.Mapping;\n\nnamespace Shop;\n\n";

    private const string Entity = """[DynamoDbEntity, Discriminator("ORDER"), PartitionKey("ORDER#{Id}")] public partial class Order { public required string Id { get; set; } }""";

    private static readonly CodeFixRun Disabled = new(new EnableNullableCodeFix(), "DYNA019", nullable: NullableContextOptions.Disable);

    private static readonly CodeFixRun Enabled = new(new EnableNullableCodeFix(), "DYNA019");

    [Test]
    public async Task Fix_ProjectWithNullableDisabled_AddsTheDirectiveAtTheTop()
    {
        // Arrange
        var source = Usings + Entity;

        // Act
        var fixedSource = await Disabled.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo("#nullable enable\n\n" + Usings + Entity);
    }

    [Test]
    public async Task Fix_FileWithAHeaderComment_AddsTheDirectiveAfterIt()
    {
        // Arrange
        var source = "// Copyright (c) Shop.\r\n\r\n" + Usings.Replace("\n", "\r\n", StringComparison.Ordinal) + Entity;

        // Act
        var fixedSource = await Disabled.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo("// Copyright (c) Shop.\r\n\r\n#nullable enable\r\n\r\n" + Usings.Replace("\n", "\r\n", StringComparison.Ordinal) + Entity);
    }

    [Test]
    public async Task Fix_FileThatStartsWithADirective_AddsTheDirectiveBeforeIt()
    {
        // Arrange
        var source = "// Header.\n#region Model\n" + Usings + Entity + "\n#endregion\n";

        // Act
        var fixedSource = await Disabled.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).StartsWith("// Header.\n#nullable enable\n\n#region Model\n");
    }

    [Test]
    public async Task Fix_IndentedFirstTokenOnOneLine_AddsTheDirectiveBeforeTheIndent()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", "  " + Entity), ("Usings.cs", "global using DynaSharp.Mapping;")];

        // Act
        var fixedFiles = await Disabled.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo("#nullable enable\n\n  " + Entity);
    }

    [Test]
    [Arguments("#nullable disable", "#nullable enable", false)]
    [Arguments("#nullable disable annotations", "#nullable enable annotations", false)]
    [Arguments("#nullable restore", "#nullable enable", true)]
    public async Task Fix_DirectiveBeforeTheType_EnablesThatDirective(string directive, string expected, bool restoresADisabledProject)
    {
        // Arrange
        var run = restoresADisabledProject ? Disabled : Enabled;
        var source = Usings + "#nullable enable\n" + directive + "\n#nullable disable warnings\n" + Entity + "\n";

        // Act
        var fixedSource = await run.FixAsync(Key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Usings + "#nullable enable\n" + expected + "\n#nullable disable warnings\n" + Entity + "\n");
    }

    [Test]
    [Arguments(false, "Add #nullable enable at the top of the file")]
    [Arguments(true, "Change #nullable disable to #nullable enable")]
    public async Task RegisterCodeFixes_Type_NamesTheEdit(bool withDirective, string expected)
    {
        // Arrange
        var source = Usings + (withDirective ? "#nullable disable\n" : string.Empty) + Entity;

        // Act
        var titles = await (withDirective ? Enabled : Disabled).TitlesAsync(("Order.cs", source));

        // Assert
        await Assert.That(titles).IsEquivalentTo([expected]);
    }

    [Test]
    public async Task RegisterCodeFixes_GeneratedFile_OffersNothing()
    {
        // Arrange
        var source = Usings + Entity;

        // Act
        var titles = await Disabled.TitlesAsync(("Order.Designer.cs", source));

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Fix_Result_HasNothingLeftToReport(bool withDirective)
    {
        // Arrange
        var source = Usings + (withDirective ? "#nullable disable\n" : string.Empty) + Entity;

        // Act
        var fixedFiles = await (withDirective ? Enabled : Disabled).FixAsync(Key, ("Order.cs", source));
        var reported = await (withDirective ? Enabled : Disabled).ReportedAsync(fixedFiles);

        // Assert
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_TwoTypesInOneFile_AddsTheDirectiveOnce(FixAllScope scope)
    {
        // Arrange
        var source = Usings + Entity + "\n" + Entity.Replace("ORDER", "LINE", StringComparison.Ordinal).Replace("Order", "Line", StringComparison.Ordinal);

        // Act
        var fixedFiles = await Disabled.FixAllAsync(Key, scope, [("Order.cs", source)]);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).IsEqualTo("#nullable enable\n\n" + source);
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new EnableNullableCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA019"]);
    }
}
