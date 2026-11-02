using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA204: a command in a variable, used as a receiver when an earlier call has used it up on some path to here.</summary>
public sealed class CommandReuseAnalyzerTests
{
    private const string Message = "DYNA204 Warning: This command was used by an earlier call, so this one throws at run time. Assign what each call returns to the variable, or build the command again.";

    private const string Helpers = """

        public static class Audit
        {
            public static UpdateCommand<T> Stamped<T>(this UpdateCommand<T> command) where T : IDynamoDbDocument<T> => command.Set("at", 1);

            public static void Note(this QueryCommand<Item> query) { }

            public static void Deconstruct(this QueryCommand<Item> query, out QueryCommand<Item> next, out int count)
            {
                next = query;
                count = 1;
            }

            extension(QueryCommand<Item>)
            {
                public static QueryCommand<Item> operator +(QueryCommand<Item> query, int limit) => query.Limit(limit);

                public static QueryCommand<Item> operator ++(QueryCommand<Item> query) => query.Limit(1);
            }
        }
        """;

    [Test]
    [Arguments("""var q = shop.Query("a"); var a = q.Limit(1); var b = [|q|].Limit(2);""")]
    [Arguments("""var q = shop.Query("a"); var a = (q).Limit(1); var b = ([|q|]).Limit(2);""")]
    [Arguments("""var c = shop.Put(); var one = c.SendAsync(); var two = [|c|].SendAsync();""")]
    [Arguments("""var c = shop.Put(); _ = c.ToRequestJson(); _ = [|c|].ToRequestJson();""")]
    [Arguments("""var u = shop.Update("a"); var a = u.Stamped(); var b = [|u|].Set("x", 1);""")]
    [Arguments("""var u = shop.Update("a"); var a = u.Set("x", 1); var b = [|u|].Stamped();""")]
    [Arguments("""var u = shop.Update("a"); var a = u.If($"{Attr("a")} = {1}"); var b = [|u|].If($"{Attr("a")} = {2}");""")]
    [Arguments("""var q = shop.Query("a"); var a = q.Limit(1 + (shop is null ? 1 : 2)); var b = [|q|].Limit(2);""")]
    [Arguments("""var q = shop.Query("a"); q = q.Limit(1); var a = q.Descending(); var b = [|q|].Limit(2);""")]
    [Arguments("""var tx = client.TransactWrite(); var a = tx.Delete(shop, "a"); var b = [|tx|].Delete(shop, "b");""")]
    [Arguments("""var q = shop.Query("a"); var a = q.ItemAsync(); var b = [|q|].ItemAsync();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ToRequestJson(); _ = [|q|].ItemAsync();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ItemAsync(); _ = [|q|].Limit(1);""")]
    [Arguments("""var q = shop.Query("a"); var a = q.Limit(1); q += 1; var b = q.Limit(2); var c = [|q|].Limit(3);""")]
    public async Task Analyze_SecondReceiver_ReportsIt(string statements)
    {
        // Arrange
        var source = AnalyzerRun.Member("private static readonly DynamoDbClient client = null!;\npublic static void Run()\n{\n" + statements + "\n}\n", Helpers);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""var q = shop.Query("a"); var r = shop.Query("b"); var n = 0; (r, n) = q.Limit(1); _ = [|q|].Limit(2); _ = r.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); var n = 0; _ = q.ToRequestJson(); (q, n) = [|q|].Limit(1); _ = q.ToRequestJson();""")]
    public async Task Analyze_DeconstructionThatWritesNothingUsedUp_ReportsIt(string statements)
    {
        // Arrange
        var source = AnalyzerRun.Member("public static void Run()\n{\n" + statements + "\n}\n", Helpers);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(AnalyzerRun.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""var q = shop.Query("a"); var n = 0; (q, n) = q.Limit(1); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); (q, _) = q.Limit(1); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); var n = 0; (q, n) = (Flag ? q.Limit(1) : q.Limit(2)); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); var n = 0; for (var i = 0; i < 2; i++) { (q, n) = q.Limit(i); } _ = q.ToRequestJson();""")]
    [Arguments("""for (var i = 0; i < 2; i++) { var (q, n) = shop.Query("a"); _ = q.ToRequestJson(); }""")]
    [Arguments("""for (var i = 0; i < 2; i++) { (var q, var n) = shop.Query("a"); _ = q.ToRequestJson(); }""")]
    [Arguments("""for (var i = 0; i < 2; i++) { var (q, n) = Flag ? shop.Query("a") : shop.Query("b"); _ = q.ToRequestJson(); }""")]
    [Arguments("""var q = shop.Query("a"); var a = new int[1]; (q, a[0]) = q.Limit(1); _ = q.ToRequestJson();""")]
    public async Task Analyze_DeconstructionThatWritesTheVariable_ReportsNothing(string statements)
    {
        // Arrange (a tuple literal cannot hold a ref struct, CS9244, so this is the form such a write has)
        var source = AnalyzerRun.Member("private static readonly bool Flag = true;\npublic static void Run()\n{\n" + statements + "\n}\n", Helpers);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(AnalyzerRun.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_QueryByAGeneratedKey_ReportsTheSecondUse()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            """private static readonly Table<Order> orders = null!; public static void Run() { var q = orders.Query(default(Order.Keys.PrimaryKey)); _ = q.ItemAsync(); _ = [|q|].ItemAsync(); }""",
            """

            [DynamoDbEntity]
            [Discriminator("ORDER")]
            [PartitionKey("USER#{UserId}")]
            public partial class Order
            {
                public required string UserId { get; init; }
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_RenderedForALog_ReportsTheSend()
    {
        // Arrange (the shape that works until someone turns debug on)
        var source = AnalyzerRun.Member("""
            public static async Task Run(bool debug)
            {
                var command = shop.Put();
                if (debug)
                {
                    Console.WriteLine(command.ToRequestJson());
                }

                await [|command|].SendAsync();
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_AwaitOfAUsedCommand_ReportsTheAwait()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static async Task Run() { var command = shop.Update("a"); _ = command.ToRequestJson(); await [|command|]; }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_LoopThatDoesNotAssign_ReportsTheOneUse()
    {
        // Arrange (the second round of the loop finds it used)
        var source = AnalyzerRun.Statements("""
            var q = shop.Query("a");
            for (var i = 0; i < 2; i++)
            {
                var a = [|q|].Limit(i);
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_RetryInACatch_ReportsTheRetry()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""
            var q = shop.Query("a");
            try
            {
                _ = q.ToRequestJson();
            }
            catch (InvalidOperationException)
            {
                _ = [|q|].ToRequestJson();
            }
            """);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_ParameterOfAHelper_ReportsTheSecondUse()
    {
        // Arrange
        var source = AnalyzerRun.Member("""private static string Run(QueryCommand<Item> query) { var a = query.Limit(1); return [|query|].ToRequestJson(); }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_GenericCommand_ReportsTheSecondUse()
    {
        // Arrange
        var source = AnalyzerRun.Member("""private static TCommand Run<TCommand>(TCommand c) where TCommand : IConditional<TCommand>, allows ref struct { var a = c.If(Attr("a").Exists()); return [|c|].If(Attr("b").Exists()); }""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    public async Task Analyze_LocalOfALambda_ReportsTheSecondUse()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""Action act = () => { var q = shop.Query("a"); var a = q.Limit(1); var b = [|q|].Limit(2); };""");

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""var q = shop.Query("a"); q = q.Limit(1); q = q.Descending(); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); q = q.Limit(1); _ = q.ItemAsync();""")]
    [Arguments("""var q = shop.Query("a"); if (flag) { _ = q.ItemAsync(); } else { _ = q.ToRequestJson(); }""")]
    [Arguments("""var q = shop.Query("a"); _ = q.Limit(1).Descending().ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); if (flag) { q = q.Limit(1); } _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); if (flag) { _ = q.Limit(1); } else { _ = q.Limit(2); }""")]
    [Arguments("""var q = shop.Query("a"); if (flag) { _ = q.ToRequestJson(); return; } _ = q.Limit(2);""")]
    [Arguments("""var q = shop.Query("a"); if (flag) { _ = q.ToRequestJson(); throw new InvalidOperationException(); } _ = q.Limit(2);""")]
    [Arguments("""var q = shop.Query("a"); q = flag ? q.Limit(1) : q.Limit(2); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); switch (number) { case 1: _ = q.Limit(1); break; case 2: _ = q.Limit(2); break; default: _ = q.Limit(3); break; }""")]
    [Arguments("""var q = shop.Query("a"); for (var i = 0; i < 2; i++) { q = q.Limit(i); } _ = q.ToRequestJson();""")]
    [Arguments("""for (var i = 0; i < 2; i++) { var q = shop.Query("a"); _ = q.ToRequestJson(); }""")]
    [Arguments("""var q = shop.Query("a"); var a = q.Limit(1); q = a; _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ToRequestJson(); q = shop.Query("b"); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ToRequestJson(); Renew(ref q); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ToRequestJson(); Remake(out q); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); Look(q); Look(q);""")]
    [Arguments("""var q = shop.Query("a"); q.Note(); q.Note(); _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); var r = shop.Query("b"); _ = q.ToRequestJson(); _ = r.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); q = flag ? q.Limit(1) : q; q = q.Descending(); _ = q.ToRequestJson();""")]
    [Arguments("""var b = shop.BatchWrite(); for (var i = 0; i < 2; i++) { b = b.Put(new Item { ["pk"] = "a" + i }); } _ = b.ToRequestJson();""")]
    [Arguments("""var b = shop.BatchWrite(); for (var i = 0; i < 2; i++) { b = i % 2 == 0 ? b.Delete("a") : b.Put(new Item { ["pk"] = "a" }); } _ = b.ToRequestJson();""")]
    [Arguments("""var t = client.TransactWrite(); for (var i = 0; i < 2; i++) { t = t.Put(shop, new Item { ["pk"] = "a" }).IfNotExists(); } _ = t.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ToRequestJson(); q += 1; _ = q.ToRequestJson();""")]
    [Arguments("""var q = shop.Query("a"); _ = q.ToRequestJson(); q++; _ = q.ToRequestJson();""")]
    public async Task Analyze_EveryPathUsesItOnce_ReportsNothing(string statements)
    {
        // Arrange
        var source = AnalyzerRun.Member(
            "private static readonly DynamoDbClient client = null!;\nprivate static void Renew(ref QueryCommand<Item> query) { }\nprivate static void Remake(out QueryCommand<Item> query) => query = default;\nprivate static void Look(QueryCommand<Item> query) { }\n"
            + "public static void Run(bool flag, int number)\n{\n" + statements + "\n}\n",
            Helpers);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""private static void Run(DynamoDbWriter writer) { writer.WriteString("a"); writer.WriteString("b"); }""")]
    [Arguments("""public static void Run() { var create = shop.CreateTable(); create.IfNotExists(); create.WaitUntilActive(); }""")]
    [Arguments("""public static void Run() { var table = shop; var a = table.Query("a"); var b = table.Query("b"); _ = a.ToRequestJson(); _ = b.ToRequestJson(); }""")]
    [Arguments("""public static void Run() { var name = "a"; _ = name.Trim(); _ = name.Trim(); }""")]
    public async Task Analyze_ValueThatIsNoCommand_ReportsNothing(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(member);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CodeWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var source = "public static class Code { public static string Run(string a) => a.Trim() + a.Trim(); }";

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static Task<string> ReportAsync(string source) => AnalyzerRun.ReportWithAsync(source, new CommandReuseAnalyzer());
}
