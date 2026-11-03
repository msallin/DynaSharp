using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA235: IfVersion on a Put, which has none, where the compiler only says that the command has no such method.</summary>
public sealed class VersionClauseAnalyzerTests
{
    private const string Versioned = "A Put of 'Locked' checks its version by itself: it writes only where the stored item holds the version of the entity, and stores the next one, so it has no IfVersion. Put the entity as it was read, or call IgnoreVersion() to write without the check.";

    private const string Unversioned = "'Plain' has no [Version], so a Put has no version to check. Mark a long property with [Version], or state the condition with If(...).";

    private const string Model = """
        [DynamoDbEntity]
        [Discriminator("LOCKED")]
        [PartitionKey("LOCK#{Id}")]
        public partial record Locked
        {
            public required string Id { get; init; }

            [Version]
            public long Revision { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("PLAIN")]
        [PartitionKey("PLAIN#{Id}")]
        public partial record Plain
        {
            public required string Id { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string Street);

        """;

    private const string Run = """
        public static void Run(DynamoDbClient client, Table<Locked> locks, Table<Plain> plains, Table<Address> addresses, Locked held, Plain plain, Locked.Keys.PrimaryKey key, string[] args)
        {

        """;

    private static readonly AnalyzerFixture Fixture = new(Model);

    [Test]
    [Arguments("""_ = locks.Put(held).[|IfVersion|](1);""", Versioned)]
    [Arguments("""_ = locks.Put(held).ReturnOld().[|IfVersion|](1);""", Versioned)]
    [Arguments("""_ = locks.Put(held with { Revision = 2 }).[|IfVersion|](2).IfNotExists();""", Versioned)]
    [Arguments("""_ = client.TransactWrite().Put(locks, held).[|IfVersion|](1);""", Versioned)]
    [Arguments("""_ = client.TransactWrite().Put(shop, new Item()).Put(locks, held).[|IfVersion|](1);""", Versioned)]
    [Arguments("""_ = plains.Put(plain).[|IfVersion|](1);""", Unversioned)]
    [Arguments("""_ = client.TransactWrite().Put(plains, plain).[|IfVersion|](1);""", Unversioned)]
    public async Task Analyze_IfVersionOnAPut_ReportsTheName(string statement, string message)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new VersionClauseAnalyzer());

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA235 Error: " + message));
    }

    [Test]
    [Arguments("""_ = locks.Update(key).IfVersion(1);""")]
    [Arguments("""_ = locks.Delete(key).IfVersion(1);""")]
    [Arguments("""_ = client.TransactWrite().Update(locks, key).IfVersion(1);""")]
    [Arguments("""_ = client.TransactWrite().Check(locks, key).IfVersion(1);""")]
    [Arguments("""_ = shop.Put(new Item()).IfVersion(1);""")]
    [Arguments("""_ = client.TransactWrite().Put(shop, new Item()).IfVersion(1);""")]
    [Arguments("""_ = addresses.Put(new Address("s")).IfVersion(1);""")]
    [Arguments("""PutStep step = default; _ = step.IfVersion(1);""")]
    [Arguments("""_ = locks.Put(held).IgnoreVersion();""")]
    [Arguments("""_ = locks.Put(held).Version(1);""")]
    [Arguments("""_ = held.IfVersion(1);""")]
    [Arguments("""_ = IfVersion(1);""")]
    [Arguments("""_ = null.IfVersion(1);""")]
    [Arguments("""_ = args.IfVersion(1);""")]
    [Arguments("""_ = shop.Get("a").IfVersion(1);""")]
    public async Task Analyze_IfVersionWhereItExistsOrNoPutOfAnEntity_ReportsNothing(string statement)
    {
        // Arrange (an update, a delete and a check have IfVersion; Item, a document and a step in a variable are the compiler's alone)
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var report = await Fixture.ReportWithAsync(source, new VersionClauseAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""_ = locks.Put(held).IfVersion(1);""")]
    [Arguments("""_ = locks.Put(held).IfVersion("x");""")]
    public async Task Analyze_IfVersionOfTheConsumer_ReportsNothing(string statement)
    {
        // Arrange (an extension method of one's own that binds, or that the compiler explains for its arguments)
        var source = AnalyzerRun.Member(Run + statement + "\n}", "public static class Mine { public static PutCommand<T> IfVersion<T>(this PutCommand<T> command, long version) where T : IDynamoDbDocument<T> => command; }");

        // Act
        var report = await AnalyzerRun.ReportWithAsync(source + Model, new VersionClauseAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_GenericHelper_ReportsNothing()
    {
        // Arrange
        var source = AnalyzerRun.Member("public static void Run<T>(Table<T> table, T item) where T : IDynamoDbEntity<T> => _ = table.Put(item).IfVersion(1);");

        // Act
        var report = await Fixture.ReportWithAsync(source, new VersionClauseAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilationWithoutTheLibrary_ReportsNothing()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation("public static class Code { public static void Run(object o) => o.IfVersion(1); }", Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest)
            .RemoveAllReferences().AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        // Act
        var diagnostics = await compilation.WithAnalyzers([new VersionClauseAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("""_ = locks.Put(held).IfVersion(1);""")]
    [Arguments("""_ = plains.Put(plain).IfVersion(1);""")]
    [Arguments("""_ = client.TransactWrite().Put(locks, held).IfVersion(1);""")]
    public async Task Compile_TheSameMistake_FailsWithTheErrorTheRuleStandsBeside(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Member(Run + statement + "\n}");

        // Act
        var errors = Fixture.CompilerErrors(source);

        // Assert (CS1061 where no IfVersion is in scope, CS0411 where the one of an update is, as in a project with implicit usings)
        await Assert.That(errors.Any(static error => error.Id is "CS1061" or "CS0411")).IsTrue();
    }
}
