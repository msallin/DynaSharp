using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;
using static DynaSharp.UnitTests.Generators.Analyzers.CodeFixRun;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA123, applied in a real workspace to what the generator reports there, and run through the generator again.</summary>
public sealed class KeyOnlyCodeFixTests
{
    private const string Entity = """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("A#{Key:raw}", AttributeName = "Id")]""";

    private static readonly CodeFixRun Run = new(new KeyOnlyCodeFix(), "DYNA123");

    [Test]
    [Arguments("    public required string Key { get; set; }\n", "    [KeyOnly]\n    public required string Key { get; set; }\n")]
    [Arguments("    /// <summary>The key.</summary>\n    public required string Key { get; set; }\n", "    /// <summary>The key.</summary>\n    [KeyOnly]\n    public required string Key { get; set; }\n")]
    [Arguments("    [System.ComponentModel.Description(\"k\")]\n    public required string Key { get; set; }\n", "    [System.ComponentModel.Description(\"k\")]\n    [KeyOnly]\n    public required string Key { get; set; }\n")]
    public async Task Fix_RawInputWithoutKeyOnly_AddsKeyOnly(string property, string expected)
    {
        // Arrange
        var files = Appointment(property);

        // Act
        var fixedFiles = await Run.FixAsync(KeyOnlyCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(property, expected, StringComparison.Ordinal));
    }

    [Test]
    public async Task Fix_FileWithCrLf_KeepsCrLf()
    {
        // Arrange
        var source = Appointment("    public required string Key { get; set; }\n")[0].Item2.Replace("\n", "\r\n", StringComparison.Ordinal);

        // Act
        var fixedFiles = await Run.FixAsync(KeyOnlyCodeFix.Key, ("Order.cs", source));

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(source.Replace("    public required string Key", "    [KeyOnly]\r\n    public required string Key", StringComparison.Ordinal));
    }

    [Test]
    public async Task Fix_ParameterOfARecord_AddsKeyOnlyForTheProperty()
    {
        // Arrange
        var files = One(Entity + "\npublic partial record Appointment(string Key);\n");

        // Act
        var fixedFiles = await Run.FixAsync(KeyOnlyCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("public partial record Appointment([property: KeyOnly] string Key);");
    }

    [Test]
    public async Task Fix_Result_HasNoDiagnosticLeft()
    {
        // Arrange
        var files = Appointment("    public required string Key { get; set; }\n");

        // Act
        var fixedFiles = await Run.FixAsync(KeyOnlyCodeFix.Key, files);

        // Assert
        await Assert.That(await Run.LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_RawInputWithoutKeyOnly_OffersKeyOnly()
    {
        // Arrange
        var files = Appointment("    public required string Key { get; set; }\n");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add [KeyOnly]"]);
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyOfACompiledBase_OffersNothing()
    {
        // Arrange
        var library = Analysis.Library("namespace Lib;\n\npublic class Base\n{\n    public string Key { get; set; } = \"\";\n}\n");
        var files = One(Entity + "\npublic partial class Appointment : Lib.Base\n{\n}\n");

        // Act
        var titles = await new CodeFixRun(new KeyOnlyCodeFix(), "DYNA123") { Libraries = [library] }.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_TwoEntities_MarksBoth()
    {
        // Arrange
        (string, string)[] files = [.. Appointment("    public required string Key { get; set; }\n"), ("B.cs", Header + Entity + "\npublic partial class Visit\n{\n    public required string Key { get; set; }\n}\n")];

        // Act
        var fixedFiles = await Run.FixAllAsync(KeyOnlyCodeFix.Key, FixAllScope.Project, files);

        // Assert
        await Assert.That(fixedFiles.Keys).IsEquivalentTo(["Consumer0/Order.cs", "Consumer0/B.cs"]);
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new KeyOnlyCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA123"]);
    }

    private static (string, string)[] Appointment(string members) => One(Entity + "\npublic partial class Appointment\n{\n" + members + "}\n");
}
