using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA034, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class UnreachableSetterCodeFixTests
{
    private const string Protected = UnreachableSetterCodeFix.ProtectedKey;

    private const string Ignore = UnreachableSetterCodeFix.IgnoreKey;

    private const string Derived = "\n[DynamoDbEntity, Discriminator(\"T\"), PartitionKey(\"T#{Id}\")] public partial class T : Root { public required string Id { get; init; } }";

    private static readonly CodeFixRun Run = new(new UnreachableSetterCodeFix(), "DYNA034");

    [Test]
    [Arguments("public string Locked { get; private set; } = \"\";", Protected, "public string Locked { get; protected set; } = \"\";")]
    [Arguments("public string Locked { get; private init; } = \"\";", Protected, "public string Locked { get; protected init; } = \"\";")]
    [Arguments("public string Locked { get; private set; } = \"\";", Ignore, "[DynamoDbIgnore] public string Locked { get; private set; } = \"\";")]
    public async Task Fix_SetterTheDerivedTypeCannotReach_MakesItProtectedOrIgnoresTheProperty(string property, string key, string expected)
    {
        // Arrange
        var files = CodeFixRun.One("public class Root { " + property + " }" + Derived);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(CodeFixRun.Header + "public class Root { " + expected + " }" + Derived);
    }

    [Test]
    [Arguments(Protected)]
    [Arguments(Ignore)]
    public async Task Fix_Result_LeavesNoWarning(string key)
    {
        // Arrange
        var files = CodeFixRun.One("public class Root { public string Locked { get; private set; } = \"\"; }" + Derived);

        // Act
        var fixedFiles = await Run.FixAsync(key, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_Property_OffersBothInTheOrderOfTheMessage()
    {
        // Arrange
        var files = CodeFixRun.One("public class Root { public string Locked { get; private set; } = \"\"; }" + Derived);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Make the setter of Locked protected", "Add [DynamoDbIgnore] to Locked"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task RegisterCodeFixes_BaseInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Root.g.cs", CodeFixRun.Header + "public class Root { public string Locked { get; private set; } = \"\"; }"), ("Order.cs", CodeFixRun.Header + Derived)];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Solution_EditsTheBaseOnceForTwoDerivedTypes()
    {
        // Arrange
        (string, string)[] files =
        [
            ("Order.cs", CodeFixRun.Header + "public class Root { public string Locked { get; private set; } = \"\"; }" + Derived),
            ("U.cs", CodeFixRun.Header + "[DynamoDbEntity, Discriminator(\"U\"), PartitionKey(\"U#{Id}\")] public partial class U : Root { public required string Id { get; init; } }"),
        ];

        // Act
        var fixedFiles = await Run.FixAllAsync(Protected, FixAllScope.Solution, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Order.cs"]).StartsWith(CodeFixRun.Header + "public class Root { public string Locked { get; protected set; } = \"\"; }");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new UnreachableSetterCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA034"]);
    }
}
