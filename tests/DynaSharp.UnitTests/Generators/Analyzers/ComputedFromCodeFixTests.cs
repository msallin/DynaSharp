using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;
using static DynaSharp.UnitTests.Generators.Analyzers.CodeFixRun;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA114 and DYNA032, applied in a real workspace to what the generator and the analyzer report there, and run through both again.</summary>
public sealed class ComputedFromCodeFixTests
{
    private const string Entity = """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("P#{Id}")]""";

    private const string Index = """[GlobalIndexKey("ByName", PartitionKey = "NAME#{Full}")]""";

    private const string Stored = "    public required string Id { get; set; }\n    public required string First { get; set; }\n    public required string Last { get; set; }\n    [Version] public long Version { get; set; }\n";

    private static readonly CodeFixRun Declare = new(new ComputedFromCodeFix(), "DYNA114");

    private static readonly CodeFixRun Name = new(new ComputedFromCodeFix(), "DYNA032", new ComputedGetterAnalyzer());

    [Test]
    [Arguments("    public string Full => First + \" \" + Last;\n", "    [ComputedFrom(nameof(First), nameof(Last))]\n    public string Full => First + \" \" + Last;\n")]
    [Arguments("    public string Full { get { return Last + First + Last; } }\n", "    [ComputedFrom(nameof(Last), nameof(First))]\n    public string Full { get { return Last + First + Last; } }\n")]
    [Arguments("    public string Full { get => First + Version + nameof(Last); }\n", "    [ComputedFrom(nameof(First))]\n    public string Full { get => First + Version + nameof(Last); }\n")]
    [Arguments("    /// <summary>The name.</summary>\n    public string Full => First;\n", "    /// <summary>The name.</summary>\n    [ComputedFrom(nameof(First))]\n    public string Full => First;\n")]
    [Arguments("    [System.Obsolete] public string Full => First;\n", "    [System.Obsolete] [ComputedFrom(nameof(First))] public string Full => First;\n")]
    [Arguments("    [System.Obsolete, System.CLSCompliant(false)] public string Full => First;\n", "    [System.Obsolete, System.CLSCompliant(false)] [ComputedFrom(nameof(First))] public string Full => First;\n")]
    [Arguments("    [System.Obsolete]\n    public string Full => First;\n", "    [System.Obsolete]\n    [ComputedFrom(nameof(First))]\n    public string Full => First;\n")]
    [Arguments("    [field: System.NonSerialized] public string Full { get => First; }\n", "    [field: System.NonSerialized] [ComputedFrom(nameof(First))] public string Full { get => First; }\n")]
    public async Task Fix_ComputedPropertyInAnIndex_NamesWhatTheGetterReads(string property, string expected)
    {
        // Arrange
        var files = Person(Index, property);

        // Act
        var fixedFiles = await Declare.FixAsync("DYNA114", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(property, expected, StringComparison.Ordinal));
    }

    [Test]
    public async Task Fix_PropertyOnTheLineOfTheBrace_AddsTheAttributeOnThatLine()
    {
        // Arrange
        var files = One(Entity + "\n" + Index + "\npublic partial class Person { public required string Id { get; set; } public required string First { get; set; } public string Full => First; }\n");

        // Act
        var fixedFiles = await Declare.FixAsync("DYNA114", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("{ get; set; } [ComputedFrom(nameof(First))] public string Full => First; }");
    }

    [Test]
    public async Task Fix_PlainKeyOfAnIndex_NamesWhatTheGetterReads()
    {
        // Arrange
        var files = Person("""[GlobalIndexKey("ByName", PartitionKey = "{Full}", PartitionKeyAttributeName = "Full")]""", "    [DynamoDbAttribute(\"Full\")] public string Full => First;\n");

        // Act
        var fixedFiles = await Declare.FixAsync("DYNA114", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("    [DynamoDbAttribute(\"Full\")] [ComputedFrom(nameof(First))] public string Full => First;\n");
    }

    [Test]
    public async Task Fix_AttributesOfAnotherNamespace_NamesComputedFromInFull()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", "namespace Shop;\n\n[DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.Discriminator(OptOut = true), DynaSharp.Mapping.PartitionKey(\"P#{Id}\"), DynaSharp.Mapping.GlobalIndexKey(\"ByName\", PartitionKey = \"N#{Full}\")]\npublic partial class Person\n{\n    public required string Id { get; set; }\n    public required string First { get; set; }\n    public string Full => First;\n}\n")];

        // Act
        var fixedFiles = await Declare.FixAsync("DYNA114", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("    [global::DynaSharp.Mapping.ComputedFrom(nameof(First))]\n    public string Full => First;\n");
    }

    [Test]
    public async Task Fix_Declare_HasNoDiagnosticLeft()
    {
        // Arrange
        var files = Person(Index, "    public string Full => First + \" \" + Last;\n");

        // Act
        var fixedFiles = await Declare.FixAsync("DYNA114", files);

        // Assert
        await Assert.That(await Declare.LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    [Arguments("    public string Full => \"x\";\n")]
    [Arguments("    public string Full => Version.ToString();\n")]
    [Arguments("    public string Full { get; } = \"x\";\n")]
    public async Task RegisterCodeFixes_GetterReadsNothingToName_OffersNothing(string property)
    {
        // Arrange
        var files = Person(Index, property);

        // Act
        var titles = await Declare.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_ComputedPropertyOfACompiledBase_OffersNothing()
    {
        // Arrange
        var library = Analysis.Library("namespace Lib;\n\npublic class Base\n{\n    public string First { get; set; } = \"\";\n\n    public string Full => First;\n}\n");
        var files = One(Entity + "\n" + Index + "\npublic partial class Person : Lib.Base\n{\n    public required string Id { get; set; }\n}\n");

        // Act
        var titles = await new CodeFixRun(new ComputedFromCodeFix(), "DYNA114") { Libraries = [library] }.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_ComputedPropertyInAnIndex_OffersTheAttribute()
    {
        // Arrange
        var files = Person(Index, "    public string Full => First + Last.Length;\n");

        // Act
        var titles = await Declare.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add [ComputedFrom(nameof(First), nameof(Last))]"]);
    }

    [Test]
    public async Task Fix_ReadThatComputedFromDoesNotName_AddsIt()
    {
        // Arrange
        var files = Person(Index, "    [ComputedFrom(nameof(First))] public string Full => First + Last;\n");

        // Act
        var fixedFiles = await Name.FixAsync("DYNA032", files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("    [ComputedFrom(nameof(First), nameof(Last))] public string Full => First + Last;\n");
    }

    [Test]
    public async Task Fix_Name_HasNoDiagnosticLeft()
    {
        // Arrange
        var files = Person(Index, "    [ComputedFrom(nameof(First))] public string Full => First + Last;\n");

        // Act
        var fixedFiles = await Name.FixAsync("DYNA032", files);

        // Assert
        await Assert.That(await Name.LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_ReadThatComputedFromDoesNotName_OffersToNameIt()
    {
        // Arrange
        var files = Person(Index, "    [ComputedFrom(nameof(First))] public string Full => First + Last;\n");

        // Act
        var titles = await Name.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add nameof(Last) to [ComputedFrom]"]);
    }

    [Test]
    public async Task FixAll_TwoReadsOfOneGetter_NamesBothInTheirOrder()
    {
        // Arrange
        var files = Person(Index, "    public required string Middle { get; set; }\n    [ComputedFrom(nameof(First))] public string Full => First + Middle + Last;\n");

        // Act
        var fixedFiles = await Name.FixAllAsync("DYNA032", FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).Contains("[ComputedFrom(nameof(First), nameof(Middle), nameof(Last))]");
    }

    [Test]
    public async Task RegisterCodeFixes_AttributeOfACompiledBase_OffersNothing()
    {
        // Arrange
        var library = Analysis.Library("namespace Lib;\n\npublic class Base\n{\n    public string First { get; set; } = \"\";\n\n    public string Last { get; set; } = \"\";\n\n    [DynaSharp.Mapping.ComputedFrom(nameof(First))] public virtual string Full => First;\n}\n");
        var files = One(Entity + "\n" + Index + "\npublic partial class Person : Lib.Base\n{\n    public required string Id { get; set; }\n\n    public override string Full => First + Last;\n}\n");

        // Act
        var titles = await new CodeFixRun(new ComputedFromCodeFix(), "DYNA032", new ComputedGetterAnalyzer()) { Libraries = [library] }.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixableDiagnosticIds_AreTheRulesOfComputedFrom()
    {
        // Arrange
        var fix = new ComputedFromCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA114", "DYNA032"]);
    }

    private static (string, string)[] Person(string index, string members) =>
        One(Entity + "\n" + index + "\npublic partial class Person\n{\n" + Stored + members + "}\n");
}
