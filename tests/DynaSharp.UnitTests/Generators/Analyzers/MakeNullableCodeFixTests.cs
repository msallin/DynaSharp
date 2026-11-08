using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA024, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class MakeNullableCodeFixTests
{
    private const string Key = MakeNullableCodeFix.Key;

    private static readonly CodeFixRun Run = new(() => new MakeNullableCodeFix(), "DYNA024");

    [Test]
    [Arguments("public partial class A { public Nothing X { get; set; } }", "public partial class A { public Nothing? X { get; set; } }")]
    [Arguments("public partial record A(Nothing X);", "public partial record A(Nothing? X);")]
    public async Task Fix_PropertyOfAnEnumWithoutMembers_MakesItNullable(string type, string expected)
    {
        // Arrange
        var files = CodeFixRun.One("public enum Nothing { }\n[DynamoDbDocument] " + type);

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "public enum Nothing { }\n[DynamoDbDocument] " + expected);
    }

    [Test]
    public async Task Fix_Result_CompilesWithoutTheError()
    {
        // Arrange
        var files = CodeFixRun.One("public enum Nothing { }\n[DynamoDbDocument] public partial class A { public Nothing X { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Property_NamesIt()
    {
        // Arrange
        var files = CodeFixRun.One("public enum Nothing { }\n[DynamoDbDocument] public partial class A { public Nothing Level { get; set; } }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make Level nullable"]);
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyOfABaseTypeInALibrary_OffersNothing()
    {
        // Arrange (the diagnostic stands on the type, which does not declare the property)
        var run = new CodeFixRun(() => new MakeNullableCodeFix(), "DYNA024") { Libraries = [Analysis.Library("namespace Lib; public enum Nothing { } public class Base { public Nothing X { get; set; } }")] };
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A : Lib.Base { }");

        // Act
        var titles = await run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_PropertyInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Order.g.cs", CodeFixRun.Header + "public enum Nothing { }\n[DynamoDbDocument] public partial class A { public Nothing X { get; set; } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_MakesEachNullable()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "public enum Nothing { }\n[DynamoDbDocument] public partial class A { public Nothing X { get; set; } public Nothing Y { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).EndsWith("{ public Nothing? X { get; set; } public Nothing? Y { get; set; } }");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new MakeNullableCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA024"]);
    }
}
