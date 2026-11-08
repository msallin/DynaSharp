using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA239 and DYNA236, applied in a real workspace to what <see cref="EntityCommandAnalyzer"/> reports there, and analyzed again.</summary>
public sealed class SettleVersionCodeFixTests
{
    private const string Ignore = SettleVersionCodeFix.IgnoreKey;

    private const string IfVersion = SettleVersionCodeFix.IfVersionKey;

    private const string Batch = SettleVersionCodeFix.BatchKey;

    private const string Model = """

        [DynamoDbEntity(Table = "locks")]
        [Discriminator("LOCKED")]
        [PartitionKey("LOCK#{Id}")]
        public partial record Locked
        {
            public required string Id { get; init; }

            public string? Owner { get; init; }

            [Version]
            public long Revision { get; init; }
        }
        """;

    private const string Fields = """
        private static readonly Table<Locked> locks = null!;
        private static readonly DynamoDbClient client = null!;
        private static readonly Locked held = null!;
        private static readonly Locked.Keys.PrimaryKey lockKey = default;

        """;

    private static readonly CodeFixRun Fixture = new(new SettleVersionCodeFix(), analyzer: new EntityCommandAnalyzer()) { Model = Model };

    [Test]
    [Arguments(Ignore, """_ = locks.Update(lockKey).Set(Locked.Attrs.Owner, "o").ToRequestJson();""", """_ = locks.Update(lockKey).IgnoreVersion().Set(Locked.Attrs.Owner, "o").ToRequestJson();""")]
    [Arguments(IfVersion, """_ = locks.Update(lockKey).Set(Locked.Attrs.Owner, "o").ToRequestJson();""", """_ = locks.Update(lockKey).IfVersion(version).Set(Locked.Attrs.Owner, "o").ToRequestJson();""")]
    [Arguments(Ignore, "_ = locks.Delete(lockKey).ToRequestJson();", "_ = locks.Delete(lockKey).IgnoreVersion().ToRequestJson();")]
    [Arguments(IfVersion, "var delete = locks.Delete(lockKey); _ = delete.ToRequestJson();", "var delete = locks.Delete(lockKey).IfVersion(version); _ = delete.ToRequestJson();")]
    [Arguments(Ignore, """_ = client.TransactWrite().Update(locks, lockKey).Set(Locked.Attrs.Owner, "o").ToRequestJson();""", """_ = client.TransactWrite().Update(locks, lockKey).IgnoreVersion().Set(Locked.Attrs.Owner, "o").ToRequestJson();""")]
    [Arguments(IfVersion, "_ = client.TransactWrite().Delete(locks, lockKey).Put(locks, held).ToRequestJson();", "_ = client.TransactWrite().Delete(locks, lockKey).IfVersion(version).Put(locks, held).ToRequestJson();")]
    [Arguments(Ignore, "_ = client.TransactWrite().Update(locks, lockKey, out _).ToRequestJson();", "_ = client.TransactWrite().Update(locks, lockKey, out _).IgnoreVersion().ToRequestJson();")]
    public async Task Fix_WriteByKey_AddsTheClauseRightAfterTheVerb(string key, string statement, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixedSource = await Fixture.FixAsync(key, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Run(expected));
    }

    [Test]
    public async Task Fix_ChainWithOneCallPerLine_PutsTheClauseOnALineOfItsOwn()
    {
        // Arrange
        var source = Run("""
            _ = locks.Update(lockKey) // the owner
                .Set(Locked.Attrs.Owner, "o")
                .ToRequestJson();
            """);

        // Act
        var fixedSource = await Fixture.FixAsync(Ignore, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Run("""
            _ = locks.Update(lockKey) // the owner
                .IgnoreVersion()
                .Set(Locked.Attrs.Owner, "o")
                .ToRequestJson();
            """));
    }

    [Test]
    [Arguments("long version = 1;", "expectedVersion")]
    [Arguments("long version = 1, expectedVersion = 2;", "expectedVersion2")]
    [Arguments("long version = 1, expectedVersion = 2, expectedVersion2 = 3;", "expectedVersion3")]
    public async Task Titles_NameTakenInScope_OfferAPlaceholderThatNamesNothing(string locals, string placeholder)
    {
        // Arrange (a local that happens to be called so would compile with a value nobody chose)
        var source = Run(locals + " _ = locks.Delete(lockKey).ToRequestJson();");

        // Act
        var titles = await Fixture.TitlesAsync(source);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add .IgnoreVersion()", "Add .IfVersion(" + placeholder + ")"]);
    }

    [Test]
    public async Task Fix_IfVersion_DoesNotCompileUntilTheAuthorPutsTheVersion()
    {
        // Arrange
        var source = Run("_ = locks.Delete(lockKey).ToRequestJson();");

        // Act
        var fixedSource = await Fixture.FixAsync(IfVersion, source);

        // Assert (the placeholder names nothing)
        await Assert.That(await Fixture.CompilerErrorsAsync(fixedSource)).IsEquivalentTo(["CS0103"]);
    }

    [Test]
    public async Task Fix_IfVersionWithTheVersionRead_HasNoDiagnosticLeftAndCompiles()
    {
        // Arrange
        var source = Run("_ = locks.Delete(lockKey).ToRequestJson();");

        // Act
        var fixedSource = (await Fixture.FixAsync(IfVersion, source)).Replace("IfVersion(version)", "IfVersion(held.Revision)", StringComparison.Ordinal);

        // Assert
        await Assert.That((await Fixture.ReportAsync(fixedSource), (await Fixture.CompilerErrorsAsync(fixedSource)).Count)).IsEqualTo((string.Empty, 0));
    }

    [Test]
    public async Task Fix_IgnoreVersion_HasNoDiagnosticLeftAndCompiles()
    {
        // Arrange
        var source = Run("""_ = locks.Update(lockKey).Set(Locked.Attrs.Owner, "o").ToRequestJson();""");

        // Act
        var fixedSource = await Fixture.FixAsync(Ignore, source);

        // Assert
        await Assert.That((await Fixture.ReportAsync(fixedSource), (await Fixture.CompilerErrorsAsync(fixedSource)).Count)).IsEqualTo((string.Empty, 0));
    }

    [Test]
    [Arguments("_ = locks.BatchWrite().Put(held).ToRequestJson();", "_ = locks.BatchWrite().Put(held).IgnoreVersion().ToRequestJson();")]
    [Arguments("_ = locks.BatchWrite().DeleteAll([lockKey]).Put(held).ToRequestJson();", "_ = locks.BatchWrite().DeleteAll([lockKey]).IgnoreVersion().Put(held).ToRequestJson();")]
    public async Task Fix_BatchOfAVersionedEntity_AddsIgnoreVersionAfterTheFirstWrite(string statement, string expected)
    {
        // Arrange
        var source = Run(statement);

        // Act
        var fixedSource = await Fixture.FixAsync(Batch, source);

        // Assert
        await Assert.That((fixedSource, await Fixture.ReportAsync(fixedSource))).IsEqualTo((Run(expected), string.Empty));
    }

    [Test]
    public async Task Titles_Batch_OfferIgnoreVersionAlone()
    {
        // Arrange (writing the items one by one is a rewrite, not an edit)
        var source = Run("_ = locks.BatchWrite().Put(held).ToRequestJson();");

        // Act
        var titles = await Fixture.TitlesAsync(source);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add .IgnoreVersion() to the batch"]);
    }

    [Test]
    [Arguments(FixAllScope.Document)]
    [Arguments(FixAllScope.Project)]
    public async Task FixAll_StepsOfOneTransaction_SettlesEach(FixAllScope scope)
    {
        // Arrange
        var source = Run("_ = client.TransactWrite().Update(locks, lockKey).Delete(locks, lockKey).ToRequestJson(); _ = locks.Delete(lockKey).ToRequestJson();");

        // Act
        var fixedSource = await Fixture.FixAllAsync(Ignore, scope, source);

        // Assert
        await Assert.That(fixedSource).IsEqualTo(Run("_ = client.TransactWrite().Update(locks, lockKey).IgnoreVersion().Delete(locks, lockKey).IgnoreVersion().ToRequestJson(); _ = locks.Delete(lockKey).IgnoreVersion().ToRequestJson();"));
    }

    [Test]
    public async Task FixableDiagnosticIds_AreTheRules()
    {
        // Arrange
        var fix = new SettleVersionCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA239", "DYNA236"]);
    }

    [Test]
    public async Task GetFixAllProvider_FixesAllInOneGo()
    {
        // Arrange
        var fix = new SettleVersionCodeFix();

        // Act
        var provider = fix.GetFixAllProvider();

        // Assert
        await Assert.That(provider).IsEqualTo(WellKnownFixAllProviders.BatchFixer);
    }

    private static string Run(string statements) => AnalyzerRun.Member(Fields + "public static void Run()\n{\n" + statements + "\n}\n");
}
