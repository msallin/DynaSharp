using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA033 and DYNA046, applied in a real workspace to the diagnostics the analyzer and the generator report there, and run through them again.</summary>
public sealed class RemoveEnumAttributeCodeFixTests
{
    private const string Key = RemoveEnumAttributeCodeFix.Key;

    private const string Flags = "[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)]\n[System.Flags]\npublic enum Access { None = 0, Read = 1 }";

    private const string FlagsWithName = "[System.Flags]\npublic enum Access\n{\n    None = 0,\n    [DynamoDbEnumName(\"READ\")]\n    Read = 1,\n}";

    private static readonly CodeFixRun Run = new(new RemoveEnumAttributeCodeFix(), "DYNA033", new EnumAttributeAnalyzer());

    private static readonly CodeFixRun NameRun = new(new RemoveEnumAttributeCodeFix(), "DYNA046", new EnumAttributeAnalyzer());

    [Test]
    public async Task Fix_NameOnAMemberOfAFlagsEnum_RemovesItsLineAndLeavesNoWarning()
    {
        // Arrange
        var files = CodeFixRun.One(FlagsWithName);

        // Act
        var fixedFiles = await NameRun.FixAsync(RemoveEnumAttributeCodeFix.NameKey, files);
        var left = await NameRun.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That((fixedFiles["Order.cs"], left.Count)).IsEqualTo((CodeFixRun.Header + "[System.Flags]\npublic enum Access\n{\n    None = 0,\n    Read = 1,\n}", 0));
    }

    [Test]
    public async Task RegisterCodeFixes_NameOnAMemberOfAFlagsEnum_OffersToRemoveIt()
    {
        // Arrange
        var files = CodeFixRun.One(FlagsWithName);

        // Act
        var titles = await NameRun.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Remove [DynamoDbEnumName]"]);
    }

    [Test]
    public async Task Fix_AttributeOfAFlagsEnum_RemovesItsLine()
    {
        // Arrange
        var files = CodeFixRun.One(Flags);

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "[System.Flags]\npublic enum Access { None = 0, Read = 1 }");
    }

    [Test]
    public async Task Fix_Result_HasNoWarningLeft()
    {
        // Arrange
        var files = CodeFixRun.One(Flags);

        // Act
        var fixedFiles = await Run.FixAsync(Key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Property_OffersNothing()
    {
        // Arrange (the attribute works for the other properties of the enum)
        var files = CodeFixRun.One("[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)] public enum Level { Low }\n[DynamoDbDocument] public partial class A { public required Level Level { get; set; } }");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_RemovesEach()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", CodeFixRun.Header + Flags + "\n[DynamoDbEnum(UnknownName = UnknownEnumName.CountsAsMissing)] [System.Flags] public enum Other { None = 0 }")];

        // Act
        var fixedFiles = await Run.FixAllAsync(Key, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).DoesNotContain("DynamoDbEnum");
    }

    [Test]
    public async Task FixableDiagnosticIds_AreTheRules()
    {
        // Arrange
        var fix = new RemoveEnumAttributeCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA033", "DYNA046"]);
    }
}
