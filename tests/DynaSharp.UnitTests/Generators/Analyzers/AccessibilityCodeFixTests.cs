using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA016, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class AccessibilityCodeFixTests
{
    private const string Key = AccessibilityCodeFix.Key;

    private static readonly CodeFixRun Run = new(new AccessibilityCodeFix(), "DYNA016");

    [Test]
    [Arguments("private enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; }", "internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; }")]
    [Arguments("protected enum Hidden { One } [DynamoDbAttribute(\"x\")] internal System.Collections.Generic.List<Hidden> X { get; set; } = [];", "internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal System.Collections.Generic.List<Hidden> X { get; set; } = [];")]
    [Arguments("private protected enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden[] X { get; set; } = [];", "internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden[] X { get; set; } = [];")]
    [Arguments("enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; }", "internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; }")]
    [Arguments("[System.Flags] enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; }", "[System.Flags] internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; }")]
    [Arguments("sealed class Hidden { } [DynamoDbAttribute(\"x\")] internal Hidden? X { get; set; }", "internal sealed class Hidden { } [DynamoDbAttribute(\"x\")] internal Hidden? X { get; set; }")]
    [Arguments("record Hidden(int V); [DynamoDbAttribute(\"x\")] internal Hidden? X { get; set; }", "internal record Hidden(int V); [DynamoDbAttribute(\"x\")] internal Hidden? X { get; set; }")]
    [Arguments("private enum Hidden { One } [DynamoDbAttribute(\"x\")] internal System.Collections.Generic.Dictionary<Hidden, dynamic> X { get; set; } = [];", "internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal System.Collections.Generic.Dictionary<Hidden, dynamic> X { get; set; } = [];")]
    [Arguments("delegate int Hidden(); [DynamoDbAttribute(\"x\")] internal Hidden? X { get; set; }", "internal delegate int Hidden(); [DynamoDbAttribute(\"x\")] internal Hidden? X { get; set; }")]
    public async Task Fix_PropertyOfAPrivateType_MakesTheTypeInternal(string members, string expected)
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { " + members + " }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "[DynamoDbDocument] public partial class A { " + expected + " }");
    }

    [Test]
    public async Task Fix_FileLocalType_MakesItInternal()
    {
        // Arrange
        var files = CodeFixRun.One("file enum Hidden { One }\n[DynamoDbDocument] public partial class A { [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).StartsWith(CodeFixRun.Header + "internal enum Hidden { One }\n");
    }

    [Test]
    public async Task Fix_TypeInAPrivateContainingType_RaisesTheContainingType()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { private static class Outer { public enum Hidden { One } } [DynamoDbAttribute(\"x\")] internal Outer.Hidden X { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("internal static class Outer { public enum Hidden { One } }");
    }

    [Test]
    public async Task Fix_PlainKeyOfAPublicEntity_MakesTheTypePublic()
    {
        // Arrange
        var files = CodeFixRun.One("internal enum Kind { A }\n[DynamoDbEntity, Discriminator(\"E\")] public partial class E { [PartitionKey] public Kind Id { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).StartsWith(CodeFixRun.Header + "public enum Kind { A }\n");
    }

    [Test]
    public async Task Fix_InputOfATemplateOfAPublicEntity_MakesTheTypePublic()
    {
        // Arrange
        var files = CodeFixRun.One("internal enum Hidden { One }\n[DynamoDbEntity, Discriminator(\"E\"), PartitionKey(\"A#{V}\")] public partial class E { [DynamoDbAttribute(\"v\")] internal Hidden V { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).StartsWith(CodeFixRun.Header + "public enum Hidden { One }\n");
    }

    [Test]
    public async Task Fix_TypeInPartsInTwoFiles_EditsThePartsThatNameAnAccessibility()
    {
        // Arrange
        (string, string)[] files =
        [
            ("A.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { private partial class Part { } [DynamoDbAttribute(\"x\")] internal Part? X { get; set; } }"),
            ("B.cs", CodeFixRun.Header + "public partial class A { partial class Part { } }"),
        ];

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles).IsEquivalentTo(new Dictionary<string, string> { ["A.cs"] = CodeFixRun.Header + "[DynamoDbDocument] public partial class A { internal partial class Part { } [DynamoDbAttribute(\"x\")] internal Part? X { get; set; } }" });
    }

    [Test]
    public async Task Fix_Result_CompilesWithoutTheError()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { private enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; } }");

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_TwoTypes_NamesBoth()
    {
        // Arrange
        var files = CodeFixRun.One("[DynamoDbDocument] public partial class A { private enum One { A } private enum Two { B } [DynamoDbAttribute(\"x\")] internal System.Collections.Generic.Dictionary<One, Two> X { get; set; } = []; }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make One and Two internal"]);
    }

    [Test]
    public async Task RegisterCodeFixes_TypeInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [.. CodeFixRun.One("[DynamoDbDocument] public partial class A { [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; } }"), ("Hidden.g.cs", "namespace Shop;\n\npublic partial class A { private enum Hidden { One } }")];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_RaisesATypeTwoPropertiesShareOnce()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + "[DynamoDbDocument] public partial class A { private enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X { get; set; } [DynamoDbAttribute(\"y\")] internal Hidden Y { get; set; } }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("{ internal enum Hidden { One } [DynamoDbAttribute(\"x\")] internal Hidden X");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new AccessibilityCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA016"]);
    }
}
