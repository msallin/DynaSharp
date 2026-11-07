using System.Globalization;
using System.Reflection;
using DynaSharp.Generators;
using DynaSharp.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// DYNA217 and DYNA218: what a delegate of RetryOnConflictAsync holds that makes the retry useless. Every snippet has to compile, because an analyzer sees little of what does not bind, and a test that
/// expects no report would pass for that reason. The agreement of DYNA218 with the run time is here, on the text of the snippets themselves; the one of DYNA217 is in <see cref="ConflictRetryTests"/>.
/// </summary>
public sealed class ConflictRetryAnalyzerTests
{
    private const string NestedMessage = "DYNA217 Warning: RetryOnConflictAsync runs inside the delegate of another RetryOnConflictAsync, so the runs multiply and an exhausted inner retry counts as a conflict of the outer one. Move the follow-up write after the outer call.";

    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        public partial record Order
        {
            [PartitionKey]
            public required string Id { get; init; }

            public int Total { get; init; }

            public string? Remark;

            [Version]
            public long Revision { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TAB")]
        public partial class Tab
        {
            [PartitionKey]
            public required string Id { get; set; }

            [Version]
            public long Revision { get; set; }

            public void Refresh(Tab other) => Revision = other.Revision;
        }

        [DynamoDbEntity]
        [Discriminator("NOTE")]
        public partial record Note
        {
            [PartitionKey]
            public required string Id { get; init; }
        }

        [DynamoDbDocument]
        public partial record Address(string Street);

        public sealed class Holder
        {
            public Order? Item { get; set; }

            public long Count;

            public long Inner;
        }

        public interface ISaver
        {
            ValueTask<int> SaveAsync(CancellationToken token);
        }

        public sealed class OwnRetry
        {
            public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation) => Task.CompletedTask;

            public void IfVersion(long version)
            {
            }
        }

        [DynamoDbEntity]
        [Discriminator("REPO")]
        public partial record Repository
        {
            [PartitionKey]
            public required string Id { get; init; }

            public Task<int> SaveAsync(DynamoDbClient client) => client.Table("Shop", "pk").RetryOnConflictAsync(static _ => new ValueTask<int>(1));
        }
        """;

    private const string Fields = """
        private static readonly DynamoDbClient client = null!;
        private static readonly Table<Order> orders = null!;
        private static readonly Table<Tab> tabs = null!;
        private static readonly Table<Note> notes = null!;
        private static readonly Table<Address> addresses = null!;
        private static readonly Order.Keys.PrimaryKey key = default;
        private static readonly Order field = null!;
        private static Order Property => null!;
        private static long Seen => 1;
        private static Order Build() => null!;
        private static Holder Box() => new();
        private static void Refresh(Tab tab, Tab fresh) { }
        private static ValueTask<int> Done(CancellationToken token) => new(1);
        private static int Count(Func<Task<int>> count) => 2;

        """;

    private const string MoreMethods = """
        private static async ValueTask<int> First(CancellationToken ct) => await Second(ct);
        private static async ValueTask<int> Second(CancellationToken ct) => await orders.RetryOnConflictAsync(Done, ct);
        private static ValueTask<int> Plain(CancellationToken ct) => new(1);
        private static ValueTask<int> OwnName(CancellationToken ct) => new(new OwnRetry().RetryOnConflictAsync(_ => default).Id);
        private static ValueTask<int> Dynamic(CancellationToken ct) { ((dynamic)orders).RetryOnConflictAsync(null); return new(1); }
        private static readonly Func<Task<int>> InAField = () => orders.RetryOnConflictAsync(Done);
        """;

    private const string CallerOfSave = "private static Task<int> CallSave() => orders.RetryOnConflictAsync(ct => SaveAsync(ct));\n";

    private const string SaveWithRetry = "private static async ValueTask<int> SaveAsync(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](Done, ct);\n";

    private const string Library = """
        using System.Threading;
        using System.Threading.Tasks;
        using DynaSharp;

        namespace Elsewhere;

        public static class Saving
        {
            public static ValueTask<int> SaveAsync(DynamoDbClient client, CancellationToken token) => new(client.Table("Shop", "pk").RetryOnConflictAsync(static _ => new ValueTask<int>(1), token));
        }
        """;

    private const string SecondFile = """
        using System.Threading;
        using System.Threading.Tasks;
        using DynaSharp;

        namespace Shop;

        public static class Saving
        {
            public static ValueTask<int> SaveAsync(DynamoDbClient client, CancellationToken token) => new(client.Table("Shop", "pk").RetryOnConflictAsync(static _ => new ValueTask<int>(1), token));
        }
        """;

    // The mistake of DYNA218 and the code that fixes it, which the tests below report on and run. OptOut keeps the items of the stub free of a discriminator.
    private const string Runnable = """

        [DynamoDbEntity]
        [Discriminator(OptOut = true)]
        public partial record Order
        {
            [PartitionKey]
            public required string Id { get; init; }

            public int Total { get; init; }

            [Version]
            public long Revision { get; init; }
        }
        """;

    private const string ReadOutside = """
        public static async Task RunAsync(DynamoDbClient client)
        {
            var orders = client.Table<Order>("Shop");
            var order = await orders.Get(Order.Keys.Of("a")).Consistent().ItemAsync();
            await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]! with { Total = 2 }).SendAsync(ct));
        }
        """;

    private const string ReadInside = """
        public static async Task RunAsync(DynamoDbClient client)
        {
            var orders = client.Table<Order>("Shop");
            await orders.RetryOnConflictAsync(async ct =>
            {
                var order = await orders.Get(Order.Keys.Of("a")).Consistent().ItemAsync(ct);
                await orders.Put(order! with { Total = 2 }).SendAsync(ct);
            });
        }
        """;

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""await orders.RetryOnConflictAsync(ct => new ValueTask<int>([|orders.RetryOnConflictAsync|](Done, ct)));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync|](Done, ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => { await [|orders.RetryOnConflictAsync|](Done, maxAttempts: 3, cancellationToken: ct); });""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync<int>|](Done, ct), maxAttempts: 2);""")]
    [Arguments("""await orders.RetryOnConflictAsync(delegate (CancellationToken ct) { return new ValueTask<int>([|orders.RetryOnConflictAsync|](Done, ct)); });""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await Task.Run(async () => await [|orders.RetryOnConflictAsync|](Done, ct)));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await Task.Run(() => Task.Run(() => [|orders.RetryOnConflictAsync|](Done, ct))));""")]
    [Arguments("""async ValueTask<int> Local(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](Done, ct); await orders.RetryOnConflictAsync(Local);""")]
    [Arguments("""async ValueTask<int> Local(CancellationToken ct) { return await [|orders.RetryOnConflictAsync|](Done, ct); } await orders.RetryOnConflictAsync(Local);""")]
    [Arguments("""async ValueTask<int> Local(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](Done, ct); await orders.RetryOnConflictAsync(ct => Local(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync|](Done, ConflictKind.TransactionConflict, cancellationToken: ct), ConflictKind.TransactionConflict);""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync|](Done, ConflictKind.ConditionalCheckFailed, 3, null, ct));""")]
    public async Task RetryOnConflictAsync_RetryInsideTheDelegate_IsReported(string statements)
    {
        // Arrange
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage));
    }

    [Test]
    public async Task RetryOnConflictAsync_MethodThatTheDelegateCalls_IsReportedAtTheRetryInTheMethod()
    {
        // Arrange
        var source = Source(
            "await orders.RetryOnConflictAsync(ct => SaveAsync(ct));",
            "private static async ValueTask<int> SaveAsync(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](Done, ct);");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage));
    }

    [Test]
    [Arguments("await orders.RetryOnConflictAsync(ct => Generic<int>(ct));", "private static async ValueTask<T> Generic<T>(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](_ => new ValueTask<T>(default(T)!), ct);")]
    [Arguments("await orders.RetryOnConflictAsync(ct => Twice(ct));", "private static ValueTask<int> Twice(CancellationToken ct) { async ValueTask<int> Inner() => await [|orders.RetryOnConflictAsync|](Done, ct); return Inner(); }")]
    [Arguments("await orders.RetryOnConflictAsync(ct => InLambda(ct));", "private static async ValueTask<int> InLambda(CancellationToken ct) => await Task.Run(() => [|orders.RetryOnConflictAsync|](Done, ct));")]
    [Arguments("await orders.RetryOnConflictAsync(async ct => { await Split(ct); });", "private static partial ValueTask Split(CancellationToken ct); private static partial async ValueTask Split(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](Done, ct);")]
    public async Task RetryOnConflictAsync_RetryInAShapeOfMethodThatTheDelegateCalls_IsReported(string statements, string members)
    {
        // Arrange
        var source = Source(statements, members).Replace("public static class Code", "public static partial class Code", StringComparison.Ordinal);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage));
    }

    [Test]
    [Arguments(CallerOfSave + SaveWithRetry)]
    [Arguments(SaveWithRetry + CallerOfSave)]
    public async Task RetryOnConflictAsync_CallAndRetryInEitherOrder_IsReported(string members)
    {
        // Arrange (the retry and the call meet in whichever order they are analyzed, so one after the other, and the two orders of the source give both)
        var source = AnalyzerRun.Member(members + Fields, Entities);
        var options = new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, concurrentAnalysis: false, logAnalyzerExecutionTime: false);

        // Act
        var report = await AnalyzerRun.Compile(source).WithAnalyzers([new ConflictRetryAnalyzer()], options).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(string.Join("\n", report.Select(Analysis.Describe))).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage));
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryInARetryInARetry_ReportsEachInnerOneOnce()
    {
        // Arrange (the innermost is found from both retries around it)
        var source = Source("""await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync|](async inner => await [|orders.RetryOnConflictAsync|](Done, inner), ct));""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage, NestedMessage));
    }

    [Test]
    public async Task RetryOnConflictAsync_TwoRetriesCallOneMethod_ReportsItOnce()
    {
        // Arrange
        var source = Source(
            "await orders.RetryOnConflictAsync(ct => SaveAsync(ct)); await orders.RetryOnConflictAsync(ct => SaveAsync(ct));",
            "private static async ValueTask<int> SaveAsync(CancellationToken ct) => await [|orders.RetryOnConflictAsync|](Done, ct);");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage));
    }

    [Test]
    public async Task RetryOnConflictAsync_MethodInAnotherFile_IsReportedThere()
    {
        // Arrange
        var source = Whole("await orders.RetryOnConflictAsync(ct => Saving.SaveAsync(client, ct));");
        var compilation = AnalyzerRun.Compile(source).AddSyntaxTrees(CSharpSyntaxTree.ParseText(SecondFile, GeneratorRun.ParseOptions(LanguageVersion.Latest), "Saving.cs"));

        // Act
        var report = await compilation.WithAnalyzers([new ConflictRetryAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(string.Join(",", report.Select(static diagnostic => $"{diagnostic.Id} {diagnostic.Location.SourceTree!.FilePath}"))).IsEqualTo("DYNA217 Saving.cs");
    }

    [Test]
    [Arguments("""await orders.RetryOnConflictAsync(Done); await orders.RetryOnConflictAsync(Done);""")]
    [Arguments("""var result = await orders.RetryOnConflictAsync(Done); await orders.RetryOnConflictAsync(ct => new ValueTask<int>(result));""")]
    [Arguments("""Func<CancellationToken, Task<int>> inner = ct => orders.RetryOnConflictAsync(Done, ct); await orders.RetryOnConflictAsync(ct => new ValueTask<int>(inner(ct)));""")]
    [Arguments("""Func<CancellationToken, ValueTask<int>> run = async ct => await orders.RetryOnConflictAsync(Done, ct); await orders.RetryOnConflictAsync(run);""")]
    [Arguments("""await orders.RetryOnConflictAsync(Done, maxAttempts: Count(() => orders.RetryOnConflictAsync(Done)));""")]
    [Arguments("""await orders.RetryOnConflictAsync(ct => First(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(Second);""")]
    [Arguments("""await orders.RetryOnConflictAsync(ct => default(ISaver)!.SaveAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(ct => Plain(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(ct => OwnName(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(ct => Dynamic(ct));""")]
    [Arguments("""await new OwnRetry().RetryOnConflictAsync(async ct => await orders.RetryOnConflictAsync(Done, ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await new OwnRetry().RetryOnConflictAsync(_ => default));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.RetryOnConflictAsync(Done, maxAttempts: 1, cancellationToken: ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.RetryOnConflictAsync(Done, ct), maxAttempts: 1);""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.RetryOnConflictAsync(Done, ConflictKind.TransactionConflict, 1, null, ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.RetryOnConflictAsync(Done, ct), ConflictKind.TransactionConflict, maxAttempts: 1);""")]
    public async Task RetryOnConflictAsync_NoRetryInsideARun_IsNotReported(string statements)
    {
        // Arrange
        var source = Source(statements, MoreMethods);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task RetryOnConflictAsync_MethodOfATypeWithARetryInAnotherMethod_IsNotReported()
    {
        // Arrange (the retry is in the model, which a shared fixture does not analyze, and there this would pass with no sighting of the retry to tell apart from the call)
        var source = Whole("""await orders.RetryOnConflictAsync(ct => new ValueTask<string>(new Repository { Id = "a" }.ToString()));""", MoreMethods);

        // Act
        var report = await WholeReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task RetryOnConflictAsync_MethodOfAnAssemblyFromMetadata_IsNotReported()
    {
        // Arrange
        var compilation = AnalyzerRun.Compile(Whole("await orders.RetryOnConflictAsync(ct => Elsewhere.Saving.SaveAsync(client, ct));")).AddReferences(Analysis.Library(Library));

        // Act
        var report = await compilation.WithAnalyzers([new ConflictRetryAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task RetryOnConflictAsync_MethodOfAProjectThatAnIdeHoldsAsSource_IsNotReported()
    {
        // Arrange (the symbol has syntax, of a tree that this compilation does not hold)
        var compilation = AnalyzerRun.Compile(Whole("await orders.RetryOnConflictAsync(ct => Elsewhere.Saving.SaveAsync(client, ct));")).AddReferences(Analysis.ProjectReference(Library));

        // Act
        var report = await compilation.WithAnalyzers([new ConflictRetryAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Update(key).Set(Order.Attrs.Total, 1).IfVersion([|order|]!.Revision).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion([|order|]!.Revision).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await client.TransactWrite().Update(orders, key).Set(Order.Attrs.Total, 1).IfVersion([|order|]!.Revision).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await client.TransactWrite().Check(orders, key).IfVersion([|order|]!.Revision).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]! with { Total = 1 }).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]! with { Remark = "a" }).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Delete([|order|]!).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await client.TransactWrite().Put(orders, [|order|]!).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await client.TransactWrite().Put(orders, [|order|]!, out _).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(ct => orders.Put([|order|]!).SendAsync(ct));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(delegate (CancellationToken ct) { return orders.Put([|order|]!).SendAsync(ct); });""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); async ValueTask Local(CancellationToken ct) { await orders.Put([|order|]!).SendAsync(ct); } await orders.RetryOnConflictAsync(Local);""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); ValueTask<WriteResult<Order>> Local(CancellationToken ct) => orders.Put([|order|]!).SendAsync(ct); await orders.RetryOnConflictAsync(Local);""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await Task.Run(async () => await orders.Put([|order|]!).SendAsync(ct)));""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put(order! with { Revision = [|order|].Revision }).SendAsync(ct), maxAttempts: 2);""", "order")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async (run, ct) => await orders.Put([|order|]!).SendAsync(ct));""", "order")]
    public async Task RetryOnConflictAsync_VersionOfAReadBeforeTheRetry_IsReported(string statements, string variable)
    {
        // Arrange
        var source = Source(statements);
        var lines = AnalyzerRun.Marks(source).Select(_ => Message(variable)).ToArray();

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, lines));
    }

    [Test]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Update(key).Set(Order.Attrs.Total, 1).IfVersion([|expected|]).SendAsync(ct));""", "expected")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Update(key).Set(Order.Attrs.Total, 1).IfVersion([|given|].Revision).SendAsync(ct));""", "given")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Delete([|given|]).SendAsync(ct));""", "given")]
    [Arguments("""var version = given.Revision; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion([|version|]).SendAsync(ct));""", "version")]
    [Arguments("""var version = Seen; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion([|version|]).SendAsync(ct));""", "version")]
    [Arguments("""int small = (int)expected; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion([|small|]).SendAsync(ct));""", "small")]
    [Arguments("""var holder = Box(); await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion([|holder|].Count).SendAsync(ct));""", "holder")]
    [Arguments("""var one = expected; var two = one; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion([|two|]).SendAsync(ct));""", "two")]
    public async Task RetryOnConflictAsync_VersionThatStaysTheSameFromRunToRun_IsReported(string statements, string variable)
    {
        // Arrange
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message(variable)));
    }

    [Test]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token).ConfigureAwait(false); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct));""")]
    [Arguments("""var order = (await orders.Get(key).ItemAsync(token))!; await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct));""")]
    [Arguments("""var order = (await orders.Get(key).SendAsync(token)).Item; await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token) ?? new Order { Id = "a" }; await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct));""")]
    [Arguments("""Order? order = null; order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct));""")]
    [Arguments("""if (await orders.Get(key).ItemAsync(token) is { } order) { await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct)); }""")]
    [Arguments("""if (await orders.Get(key).ItemAsync(token) is Order order) { await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct)); }""")]
    [Arguments("""if (await orders.Get(key).ItemAsync(token) is not { } order) { return; } await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct));""")]
    [Arguments("""await foreach (var order in orders.Query(key).ItemsAsync(token)) { await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct)); }""")]
    [Arguments("""var read = await orders.Get(key).ItemAsync(token); var order = read! with { Total = 1 }; await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]).SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await Task.Run(() => orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct)));""")]
    public async Task RetryOnConflictAsync_ItemOutOfARead_IsReportedHoweverItWasRead(string statements)
    {
        // Arrange
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("order")));
    }

    [Test]
    public async Task RetryOnConflictAsync_TwoWritesInOneRun_ReportsBoth()
    {
        // Arrange
        var source = Source("""
            var order = await orders.Get(key).ItemAsync(token);
            await orders.RetryOnConflictAsync(async ct =>
            {
                await orders.Update(key).Set(Order.Attrs.Total, 1).IfVersion([|order|]!.Revision).SendAsync(ct);
                await orders.Delete(key).IfVersion([|expected|]).SendAsync(ct);
            });
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("order"), Message("expected")));
    }

    [Test]
    public async Task RetryOnConflictAsync_StaleVersionInARetryInARetry_ReportsItOnce()
    {
        // Arrange (both retries find the write)
        var source = Source("""
            var order = await orders.Get(key).ItemAsync(token);
            await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync|](async inner => await orders.Put([|order|]!).SendAsync(inner), ct));
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage, Message("order")));
    }

    [Test]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct), ConflictKind.ConditionalCheckFailed);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct), ConflictKind.Any, 3, null, token);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct), ConflictKind.TransactionConflict | ConflictKind.ConditionalCheckFailed);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); var kinds = ConflictKind.TransactionConflict; await orders.RetryOnConflictAsync(async ct => await orders.Put([|order|]!).SendAsync(ct), kinds);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { await orders.Put([|order|]!).SendAsync(ct); }, retryOn: ConflictKind.ConditionalCheckFailed, cancellationToken: token);""")]
    public async Task RetryOnConflictAsync_RetryThatRepeatsAFailedCondition_ReportsTheStaleVersion(string statements)
    {
        // Arrange (a value that is not a constant may name a failed condition)
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("order")));
    }

    [Test]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put(order!).SendAsync(ct), ConflictKind.TransactionConflict);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Update(key).Set(Order.Attrs.Total, 1).IfVersion(order!.Revision).SendAsync(ct), ConflictKind.TransactionConflict, 8, null, token);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { await orders.Delete(order!).SendAsync(ct); }, retryOn: ConflictKind.TransactionConflict, cancellationToken: token);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await client.TransactWrite().Put(orders, order!).SendAsync(ct), (ConflictKind)2);""")]
    public async Task RetryOnConflictAsync_RetryThatLeavesAFailedConditionToTheCaller_IsNotReported(string statements)
    {
        // Arrange (a stale version fails the first run, and that failure is the answer: nothing runs again with it)
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task RetryOnConflictAsync_RetryInARetryThatLeavesAFailedConditionToTheCaller_IsStillReported()
    {
        // Arrange (the inner retry multiplies the runs whatever the outer one repeats)
        var source = Source("""
            var order = await orders.Get(key).ItemAsync(token);
            await orders.RetryOnConflictAsync(async ct => await [|orders.RetryOnConflictAsync|](async inner => await orders.Put([|order|]!).SendAsync(inner), ct), ConflictKind.TransactionConflict);
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert (the inner retry repeats a failed condition, so the stale version stands for that one)
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NestedMessage, Message("order")));
    }

    [Test]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => { var order = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Put(order! with { Total = 1 }).SendAsync(ct); });""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => { var order = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Update(key).Set(Order.Attrs.Total, 1).IfVersion(order!.Revision).SendAsync(ct); });""")]
    [Arguments("""Order? order = null; await orders.RetryOnConflictAsync(async ct => { order = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Put(order!).SendAsync(ct); });""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { order = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Put(order!).SendAsync(ct); });""")]
    [Arguments("""long version = 0; await orders.RetryOnConflictAsync(async ct => { version = (await orders.Get(key).Consistent().ItemAsync(ct))!.Revision; await orders.Delete(key).IfVersion(version).SendAsync(ct); });""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => { var version = (await orders.Get(key).Consistent().ItemAsync(ct))!.Revision + 0; await orders.Delete(key).IfVersion(version).SendAsync(ct); });""")]
    [Arguments("""await foreach (var read in orders.Query(key).ItemsAsync(token)) { await orders.RetryOnConflictAsync(async ct => { foreach (var order in new[] { read }) { await orders.Delete(key).IfVersion(1).SendAsync(ct); } }); }""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(0).SendAsync(ct));""")]
    [Arguments("""const long First = 3; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(First).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(field.Revision).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(Property.Revision).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(Seen).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Put(field).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(expected + 1).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Put(new Order { Id = "a", Revision = expected }).SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async (run, ct) => { var current = run == 1 ? order : await orders.Get(key).Consistent().ItemAsync(ct); await orders.Put(current!).SendAsync(ct); });""")]
    public async Task RetryOnConflictAsync_VersionThatARunReadsOrThatIsNoVariable_IsNotReported(string statements)
    {
        // Arrange
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""var order = new Order { Id = "a" }; await orders.RetryOnConflictAsync(async ct => await orders.Put(order).SendAsync(ct));""")]
    [Arguments("""Order order = new() { Id = "a" }; await orders.RetryOnConflictAsync(async ct => await orders.Put(order).SendAsync(ct));""")]
    [Arguments("""var order = new Order { Id = "a" }; await orders.RetryOnConflictAsync(async ct => await orders.Put(order with { Total = 1 }).SendAsync(ct));""")]
    [Arguments("""var built = new Order { Id = "a" }; var order = built; await orders.RetryOnConflictAsync(async ct => await orders.Put(order).SendAsync(ct));""")]
    [Arguments("""var order = Build(); await orders.RetryOnConflictAsync(async ct => await orders.Put(order).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Put(given).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => await orders.Put(given with { Total = 1 }).SendAsync(ct));""")]
    [Arguments("""var order = given; await orders.RetryOnConflictAsync(async ct => await orders.Put(order).SendAsync(ct));""")]
    [Arguments("""var read = await orders.Get(key).ItemAsync(token); var one = read; var two = one; await orders.RetryOnConflictAsync(async ct => await orders.Put(two!).SendAsync(ct));""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => { var counter = await orders.Get(key).Consistent().ItemAsync(ct); await client.TransactWrite().Update(orders, key).Set(Order.Attrs.Total, 1).IfVersion(counter!.Revision).Put(orders, given).SendAsync(ct); });""")]
    [Arguments("""long expectedNone = 0; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(expectedNone).SendAsync(ct));""")]
    [Arguments("""var order = new Order { Id = "a" }; await orders.RetryOnConflictAsync(async ct => await orders.Delete(key).IfVersion(order.Revision).SendAsync(ct));""")]
    public async Task RetryOnConflictAsync_NewItemBuiltOutsideTheDelegate_IsNotReported(string statements)
    {
        // Arrange (a first write at version 0 is correct there, for example beside a counter that the run reads, and an item out of a parameter cannot be told from it)
        var source = Source(statements);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { var fresh = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Put(order! with { Revision = fresh!.Revision }).SendAsync(ct); });""")]
    [Arguments("""await orders.RetryOnConflictAsync(async ct => { var fresh = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Put(given with { Revision = fresh!.Revision }).SendAsync(ct); });""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put(order! with { Revision = 0 }).SendAsync(ct));""")]
    [Arguments("""var read = await orders.Get(key).ItemAsync(token); var order = read! with { Revision = 0 }; await orders.RetryOnConflictAsync(async ct => await orders.Put(order).SendAsync(ct));""")]
    [Arguments("""var tab = (await tabs.Get(tabKey).ItemAsync(token))!; await orders.RetryOnConflictAsync(async ct => { var fresh = await tabs.Get(tabKey).Consistent().ItemAsync(ct); tab.Revision = fresh!.Revision; await tabs.Put(tab).SendAsync(ct); });""")]
    [Arguments("""var tab = (await tabs.Get(tabKey).ItemAsync(token))!; await orders.RetryOnConflictAsync(async ct => { tab.Revision += 1; await tabs.Put(tab).SendAsync(ct); });""")]
    [Arguments("""var tab = (await tabs.Get(tabKey).ItemAsync(token))!; await orders.RetryOnConflictAsync(async ct => { tab.Revision++; await tabs.Put(tab).SendAsync(ct); });""")]
    [Arguments("""var tab = (await tabs.Get(tabKey).ItemAsync(token))!; await orders.RetryOnConflictAsync(async ct => { var fresh = await tabs.Get(tabKey).Consistent().ItemAsync(ct); tab.Refresh(fresh!); await tabs.Put(tab).SendAsync(ct); });""")]
    [Arguments("""var tab = (await tabs.Get(tabKey).ItemAsync(token))!; await orders.RetryOnConflictAsync(async ct => { var fresh = await tabs.Get(tabKey).Consistent().ItemAsync(ct); Refresh(tab, fresh!); await tabs.Put(tab).SendAsync(ct); });""")]
    [Arguments("""var holder = new Holder(); holder.Item = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { holder.Item = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Delete(key).IfVersion(holder.Item!.Revision).SendAsync(ct); });""")]
    [Arguments("""Order? order = null; async ValueTask Load(CancellationToken ct) => order = await orders.Get(key).Consistent().ItemAsync(ct); await orders.RetryOnConflictAsync(async ct => { await Load(ct); await orders.Put(order!).SendAsync(ct); });""")]
    [Arguments("""Order? order = null; Func<CancellationToken, ValueTask> load = async ct => order = await orders.Get(key).Consistent().ItemAsync(ct); await orders.RetryOnConflictAsync(async ct => { await load(ct); await orders.Put(order!).SendAsync(ct); });""")]
    public async Task RetryOnConflictAsync_VersionThatARunBringsUpToDate_IsNotReported(string statements)
    {
        // Arrange (the item stays where it was, and its version does not)
        var source = Source(statements, "private static readonly Tab.Keys.PrimaryKey tabKey = default;");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""var read = await orders.Get(key).ItemAsync(token); var names = new[] { read! }; await orders.RetryOnConflictAsync(async ct => { foreach (var task in names.Select(order => new Func<ValueTask>(async () => await orders.Delete(key).IfVersion(order.Revision).SendAsync(ct)))) { await task(); } });""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put(order!).IgnoreVersion().SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await client.TransactWrite().Put(orders, order!).IgnoreVersion().SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Delete(order!).IgnoreVersion().SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { var fresh = await orders.Get(key).Consistent().ItemAsync(ct); await orders.Delete(order!).IfVersion(fresh!.Revision).SendAsync(ct); });""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.Put(order!).SendAsync(token);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.Put(order!).SendAsync(ct), maxAttempts: 1);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { await orders.Put(order!).SendAsync(ct); }, maxAttempts: 1, cancellationToken: token);""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); Func<CancellationToken, ValueTask> run = async ct => await orders.Put(order!).SendAsync(ct); await orders.RetryOnConflictAsync(run);""")]
    [Arguments("""var note = await notes.Get(noteKey).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await notes.Put(note!).SendAsync(ct));""")]
    [Arguments("""Address address = null!; await orders.RetryOnConflictAsync(async ct => await addresses.Put(address).SendAsync(ct));""")]
    [Arguments("""var item = (await shop.Get("a").SendAsync(token)).Item; await orders.RetryOnConflictAsync(async ct => await shop.Put(item!).SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => await orders.BatchWrite().Put(order!).SendAsync(ct));""")]
    [Arguments("""var order = await orders.Get(key).ItemAsync(token); await orders.RetryOnConflictAsync(async ct => { await client.TransactWrite().Delete(orders, key).IfVersion(1).SendAsync(ct); new OwnRetry().IfVersion(order!.Revision); });""")]
    public async Task RetryOnConflictAsync_NoVersionThatStaysTheSame_IsNotReported(string statements)
    {
        // Arrange
        var source = Source(statements, "private static readonly Note.Keys.PrimaryKey noteKey = default;");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task RetryOnConflictAsync_ItemOfATypeParameter_IsNotReported()
    {
        // Arrange (a generic helper has no model to look at)
        var source = Source(
            "await Helper(orders, field, token);",
            """
            private static async Task Helper<T>(Table<T> table, T item, CancellationToken token)
                where T : IDynamoDbEntity<T>
            {
                await orders.RetryOnConflictAsync(async ct => await table.Put(item).SendAsync(ct), token);
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task IsRetry_EveryTypeOfTheLibraryThatDeclaresIt_IsMatched()
    {
        // Arrange (internal types too, because a reader of the rule should not have to wonder)
        var compilation = AnalyzerRun.Compile(Whole(string.Empty));
        var all = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
        var declared = DeclaredRetries().GroupBy(static method => method.DeclaringType!).ToList();

        // Act
        var symbols = declared.SelectMany(group => SymbolsOf(all, group.Key)).ToList();

        // Assert
        await Assert.That((symbols.Count, symbols.All(ConflictRetryAnalyzer.IsRetry), declared.Any(static group => group.Key == typeof(Table<>)))).IsEqualTo((DeclaredRetries().Count(), true, true));
    }

    [Test]
    public async Task OperationParameter_EveryRetryOfTheLibrary_HasIt()
    {
        // Arrange
        var retries = DeclaredRetries();

        // Act
        var others = retries.Where(static method => method.GetParameters().Count(static parameter => parameter.Name == ConflictRetryAnalyzer.OperationParameter) != 1);

        // Assert
        await Assert.That(others).IsEmpty();
    }

    [Test]
    public async Task RetryOnParameter_EveryRetryThatTakesAConflictKind_NamesItSo()
    {
        // Arrange
        var retries = DeclaredRetries().Where(static method => method.GetParameters().Any(static parameter => parameter.ParameterType == typeof(ConflictKind))).ToList();

        // Act
        var others = retries.Where(static method => method.GetParameters().Single(static parameter => parameter.ParameterType == typeof(ConflictKind)).Name != ConflictRetryAnalyzer.RetryOnParameter);

        // Assert
        await Assert.That((retries.Count, others.Count())).IsEqualTo((4, 0));
    }

    [Test]
    public async Task ConditionalCheckFailed_NumberThatTheAnalyzerLooksFor_IsTheOneOfTheLibrary()
    {
        // Arrange (the analyzer cannot reference the library, so it holds the value as a number)
        var library = ConflictKind.ConditionalCheckFailed;

        // Act
        var analyzer = (ConflictKind)ConflictRetryAnalyzer.ConditionalCheckFailed;

        // Assert
        await Assert.That(analyzer).IsEqualTo(library);
    }

    [Test]
    public async Task Report_ReadOutsideTheDelegate_IsTheSnippetThatFailsEveryRun()
    {
        // Arrange
        var source = AnalyzerRun.Member(ReadOutside, Runnable);

        // Act
        var report = await WholeReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("order")));
    }

    [Test]
    public async Task Report_ReadInsideTheDelegate_IsTheSnippetThatPassesOnTheSecondRun()
    {
        // Arrange
        var source = AnalyzerRun.Member(ReadInside, Runnable);

        // Act
        var report = await WholeReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Run_ReadOutsideTheDelegate_FailsEveryRunWithTheSameBody()
    {
        // Arrange
        using var handler = ServiceWithAnotherWriter();
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await TestClient.CaptureAsync<ConditionalCheckFailedException>(() => RunAsync(ReadOutside, client));

        // Assert
        var bodies = PutBodies(handler);
        await Assert.That((bodies.Count, bodies.Distinct(StringComparer.Ordinal).Count())).IsEqualTo((5, 1));
    }

    [Test]
    public async Task Run_ReadInsideTheDelegate_GetsThroughOnTheSecondRun()
    {
        // Arrange
        using var handler = ServiceWithAnotherWriter();
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);

        // Act
        await RunAsync(ReadInside, client);

        // Assert
        await Assert.That(PutBodies(handler).Count).IsEqualTo(2);
    }

    private static string Message(string variable) => string.Create(CultureInfo.InvariantCulture, $"DYNA218 Warning: '{variable}' was read before RetryOnConflictAsync started, so every run sends the same version and fails the same way. Read it inside the delegate.");

    private static string Source(string statements, string members = "") =>
        AnalyzerRun.Member(Fields + members + "\npublic static async Task Run(Order given, long expected, CancellationToken token)\n{\n" + statements + "\n}\n");

    private static string Whole(string statements, string members = "") => Source(statements, members) + Entities;

    private static async Task<string> ReportAsync(string source)
    {
        ThrowIfThereAreAny(Shared.CompilerErrors(source));
        return await Shared.ReportWithAsync(source, new ConflictRetryAnalyzer());
    }

    private static async Task<string> WholeReportAsync(string source)
    {
        ThrowIfThereAreAny(AnalyzerRun.CompilerErrors(source));
        return await AnalyzerRun.ReportWithAsync(source, new ConflictRetryAnalyzer());
    }

    private static void ThrowIfThereAreAny(IReadOnlyList<(string Id, TextSpan Span)> errors)
    {
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"The snippet does not compile: {string.Join(", ", errors.Select(static error => error.Id))}");
        }
    }

    private static IEnumerable<MethodInfo> DeclaredRetries() => typeof(DynamoDbClient).Assembly.GetTypes().SelectMany(static type => type
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(static method => method.Name == ConflictRetryAnalyzer.RetryMethod));

    private static IEnumerable<IMethodSymbol> SymbolsOf(Compilation compilation, Type type) =>
        compilation.GetTypeByMetadataName(type.FullName!)!.GetMembers(ConflictRetryAnalyzer.RetryMethod).OfType<IMethodSymbol>();

    private static List<string> PutBodies(StubHandler handler) => [.. handler.Requests.Where(static request => request.Operation == "PutItem").Select(static request => request.BodyText)];

    // A service with one item that stands at version 1, until another writer takes it to 2 just before the first put arrives. A put holds if it carries the next version of what is stored,
    // as the condition on the version says, so what a run gets depends on what it read and not on a script.
    private static StubHandler ServiceWithAnotherWriter()
    {
        var stored = 1L;
        var lostToTheOther = false;
        return new StubHandler(request =>
        {
            if (request.Operation == "GetItem")
            {
                return StubResponse.Json("""{"Item":{"Id":{"S":"a"},"Total":{"N":"1"},"Revision":{"N":"VERSION"}}}""".Replace("VERSION", stored.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }

            if (!lostToTheOther)
            {
                lostToTheOther = true;
                stored = 2;
            }

            var next = long.Parse(request.BodyJson.GetProperty("Item").GetProperty("Revision").GetProperty("N").GetString()!, CultureInfo.InvariantCulture);
            if (next != stored + 1)
            {
                return StubResponse.ConditionalCheckFailed();
            }

            stored = next;
            return StubResponse.Json("{}");
        });
    }

    // The very text that the analyzer was given, compiled and run, so a rule and the run time cannot be about two different programs.
    private static async Task RunAsync(string member, DynamoDbClient client)
    {
        using var image = new MemoryStream();
        var result = AnalyzerRun.Compile(AnalyzerRun.Member(member, Runnable)).Emit(image);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        }

        var code = System.Reflection.Assembly.Load(image.ToArray()).GetType("Shop.Code")!;
        await (Task)code.GetMethod(nameof(RunAsync))!.Invoke(null, [client])!;
    }
}
