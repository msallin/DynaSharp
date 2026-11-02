using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// DYNA201 mirrors compiler rules, so each case runs the compiler too: what is reported must be what the compiler refuses, on its span, and
/// what compiles must stay silent.
/// </summary>
public sealed class CommandLifetimeAnalyzerTests
{
    private const string Message = "DYNA201 Warning: A DynaSharp command is built and sent in one expression. Compute values first, or move the chain into a method.";

    private const string Outside = """
        public delegate Task Handler(UpdateCommand<Item> command);

        public sealed class Stream : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => default;
        }
        """;

    private const string Helpers = """
        private static Task<int> ValueAsync() => Task.FromResult(1);
        private static Task<CancellationToken> TokenAsync() => Task.FromResult(default(CancellationToken));
        private static async IAsyncEnumerable<int> NumbersAsync() { await Task.Yield(); yield return 1; }
        private static void Make(out UpdateCommand<Item> command) => command = default;
        private static bool Take(UpdateCommand<Item> command) => true;

        """;

    private static readonly AnalyzerFixture Shared = new(Outside);

    [Test]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await Task.Delay(1); await [|c|].SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await c.SendAsync(); [|c|].ToRequestJson(); }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); for (var i = 0; i < 2; i++) { await [|c|].SendAsync(); } }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); L: await Task.Delay(1); [|c|].ToRequestJson(); goto L; }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await Task.Delay(1); _ = [|c|].Set("x", 1); }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await foreach (var i in NumbersAsync()) { [|c|].ToRequestJson(); } }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await using (var d = new Stream()) { } [|c|].ToRequestJson(); }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); try { await Task.Delay(1); } finally { [|c|].ToRequestJson(); } }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); try { await Task.Delay(1); } catch (Exception) { [|c|].ToRequestJson(); } }""", "CS4007")]
    [Arguments("""static async Task Run() { Make(out var c); await Task.Delay(1); [|c|].ToRequestJson(); }""", "CS4007")]
    [Arguments("""static async Task Run(bool f) { var c = shop.Update("a"); if (f) { await Task.Delay(1); } [|c|].ToRequestJson(); }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await Task.Delay(1); await [|c|]; }""", "CS4007")]
    [Arguments("""static async Task Run() { DynamoDbWriter w = default; await Task.Delay(1); _ = [|w|]; }""", "CS4007")]
    [Arguments("""static async IAsyncEnumerable<int> Run() { var c = shop.Update("a"); yield return 1; [|c|].ToRequestJson(); await Task.Delay(1); }""", "CS4007")]
    [Arguments("""static IEnumerable<int> Run() { var c = shop.Update("a"); yield return 1; [|c|].ToRequestJson(); }""", "CS4007")]
    [Arguments("""static IEnumerable<int> Run(UpdateCommand<Item> c) { [|c|].ToRequestJson(); yield return 1; }""", "CS4007")]
    [Arguments("""static void Run() { Func<Task> f = async () => { var c = shop.Update("a"); await Task.Delay(1); [|c|].ToRequestJson(); }; }""", "CS4007")]
    [Arguments("""static Func<Task> Run() { return async () => { var c = shop.Update("a"); await Task.Delay(1); [|c|].ToRequestJson(); }; }""", "CS4007")]
    [Arguments("""static async Task Run() { async Task L() { var c = shop.Update("a"); await Task.Delay(1); [|c|].ToRequestJson(); } await L(); }""", "CS4007")]
    [Arguments("""static async Task Run() { await [|shop.Update("a")|].Set("x", await ValueAsync()).SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run() { await [|shop.Update("a")|].SendAsync(await TokenAsync()); }""", "CS4007")]
    [Arguments("""static async Task Run() { await [|shop.Update("a").Set("y", 1)|].Set("x", await ValueAsync()).SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await [|c|].Set("x", await ValueAsync()).SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run() { await [|shop.Update("a")|].If($"{Attr("a")} = {await ValueAsync()}").SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run() { await TableExtensions.With([|shop.Put()|], "a", await ValueAsync()).SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run(DynamoDbClient client) { var s = client.TransactWrite().Delete(shop, "a"); await Task.Delay(1); await [|s|].SendAsync(); }""", "CS4007")]
    [Arguments("""static async Task Run(DynamoDbClient client) { var s = client.TransactWrite(); await Task.Delay(1); [|s|].Delete(shop, "a"); }""", "CS4007")]
    [Arguments("""static async Task Run(DynamoDbClient client) { TransactWriteCommand t = client.TransactWrite(); t = t.Delete(shop, "a"); await Task.Delay(1); await [|t|].SendAsync(); }""", "CS4007")]
    [Arguments("""static void Run() { var c = shop.Update("a"); Action a = () => [|c|].ToRequestJson(); }""", "CS8175")]
    [Arguments("""static void Run() { var c = shop.Update("a"); Action a = delegate { [|c|].ToRequestJson(); }; }""", "CS8175")]
    [Arguments("""static void Run() { var c = shop.Update("a"); void L() { [|c|].ToRequestJson(); } L(); }""", "CS8175")]
    [Arguments("""static void Run() { var c = shop.Update("a"); var q = from i in new[] { 1 } select [|c|].ToRequestJson(); }""", "CS8175")]
    [Arguments("""static void Run() { var c = shop.Update("a"); var q = new[] { 1 }.Select(i => [|c|].ToRequestJson()); }""", "CS8175")]
    [Arguments("""static void Run(UpdateCommand<Item> c) { Action a = () => [|c|].ToRequestJson(); }""", "CS9108")]
    [Arguments("""static void Run() { Handler h = async [|c|] => await Task.Delay(1); }""", "CS4012")]
    [Arguments("""static void Run() { async Task L(UpdateCommand<Item> [|c|]) { await Task.Delay(1); } }""", "CS4012")]
    [Arguments("""static async Task Run(UpdateCommand<Item> [|c|]) { await Task.Delay(1); }""", "CS4012")]
    public async Task Analyze_WhatTheCompilerRefuses_IsReportedOnItsSpan(string member, string compilerError)
    {
        // Arrange
        var source = AnalyzerRun.Member(Helpers + member);

        // Act
        var report = await Shared.ReportAsync(source);
        var errors = Shared.CompilerErrors(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
        await Assert.That(errors.Contains((compilerError, AnalyzerRun.Marks(source)[0]))).IsTrue();
    }

    [Test]
    [Arguments("""static async Task Run(CancellationToken ct) { await shop.Update("a").Set("x", 1).SendAsync(ct); }""")]
    [Arguments("""static async Task Run() { await shop.Update("a").Set("x", 1); }""")]
    [Arguments("""static async Task Run(CancellationToken ct) { var v = await ValueAsync(); await shop.Update("a").Set("x", v).SendAsync(ct); }""")]
    [Arguments("""static async Task Run(CancellationToken ct) { var c = shop.Update("a").Set("x", 1); await c.SendAsync(ct); }""")]
    [Arguments("""static async Task Run(bool f) { var c = shop.Update("a"); if (f) c = c.Set("x", 1); await c.SendAsync(); }""")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await Task.Delay(1); c = shop.Update("b"); await c.SendAsync(); }""")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await Task.Delay(1); Make(out c); c.ToRequestJson(); }""")]
    [Arguments("""static void Deconstruct(this UpdateCommand<Item> command, out UpdateCommand<Item> next, out int count) { next = command; count = 1; } static async Task Run() { var c = shop.Update("a"); var n = 0; await Task.Delay(1); (c, n) = shop.Update("b"); c.ToRequestJson(); }""")]
    [Arguments("""static async Task Run(bool f) { var c = shop.Update("a"); if (f) { await Task.Delay(1); } else { c.ToRequestJson(); } }""")]
    [Arguments("""static async Task Run(int k) { var c = shop.Update("a"); switch (k) { case 1: await Task.Delay(1); break; default: c.ToRequestJson(); break; } }""")]
    [Arguments("""static async Task Run() { for (var i = 0; i < 2; i++) { var c = shop.Update("a"); await c.SendAsync(); } }""")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); while (true) { await c.SendAsync(); c = shop.Update("b"); } }""")]
    [Arguments("""static async Task Run(bool f) { var c = f ? shop.Update("a") : shop.Update("b"); await (f ? c.SendAsync() : default); }""")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); await c; await Task.Delay(1); }""")]
    [Arguments("""static async Task Run() { await using var d = new Stream(); var c = shop.Update("a"); c.ToRequestJson(); }""")]
    [Arguments("""static async Task Run() { var c = shop.Update("a"); Take(c); await Task.Delay(1); }""")]
    [Arguments("""static async Task Run() { try { var c = shop.Update("a"); await c.SendAsync(); } catch (Exception) { throw; } }""")]
    [Arguments("""static async Task Run() { try { var c = shop.Update("a"); c.ToRequestJson(); } finally { await Task.Delay(1); } }""")]
    [Arguments("""static async Task Run() { await Task.Delay(1); var c = shop.Update("a"); await c.SendAsync(); }""")]
    [Arguments("""static async Task<string> Run() { var c = shop.Update("a"); await Task.Delay(1); throw new InvalidOperationException(); }""")]
    [Arguments("""static async Task Run(DynamoDbClient client) { await client.TransactWrite().Delete(shop, "a").Put(shop, new Item()).SendAsync(); }""")]
    [Arguments("""static async Task Run(DynamoDbClient client) { await client.TransactWrite().Delete(shop, "a"); }""")]
    [Arguments("""static async Task Run(DynamoDbClient client) { TransactWriteCommand t = client.TransactWrite(); for (var i = 0; i < 2; i++) { t = t.Delete(shop, "a").IfExists(); } await t.SendAsync(); await Task.Delay(1); }""")]
    [Arguments("""static async Task Run(DynamoDbClient client) { TransactWriteCommand t = client.TransactWrite().Delete(shop, "a"); await Task.Delay(1); t = client.TransactWrite().Delete(shop, "b"); await t; }""")]
    [Arguments("""static async Task Run() { try { await Task.Delay(1); Func<Task> f = async () => { var c = shop.Update("a"); c = c.Set("x", 1); await c.SendAsync(); await Task.Delay(1); }; await f(); } finally { await Task.Delay(1); } }""")]
    [Arguments("""static async Task Run() { await using var d = new Stream(); await Task.Delay(1); Func<Task> f = async () => { await Task.Yield(); var c = shop.Update("a"); await c.SendAsync(); await Task.Yield(); }; await f(); }""")]
    [Arguments("""static async Task Run() { try { async Task L() { var c = shop.Update("a"); await c.SendAsync(); await Task.Delay(1); } await L(); } catch (Exception) { throw; } }""")]
    [Arguments("""static UpdateCommand<Item> Touch(string id) => shop.Update(id).Add("n", 1);""")]
    [Arguments("""static async Task Run() { await Touch("a").SendAsync(); } static UpdateCommand<Item> Touch(string id) => shop.Update(id).Add("n", 1);""")]
    [Arguments("""static UpdateCommand<Item> Audit(this UpdateCommand<Item> c, string user) => c.Set("by", user);""")]
    [Arguments("""static void Run() { Func<ValueTask<WriteResult<Item>>> f = () => shop.Update("a").SendAsync(); }""")]
    [Arguments("""static void Run() { Func<int, ValueTask<WriteResult<Item>>> f = i => { var c = shop.Update("a"); return c.SendAsync(); }; }""")]
    [Arguments("""static void Run() { Func<Task> f = async () => { var c = shop.Update("a"); await c.SendAsync(); }; }""")]
    [Arguments("""static void Run() { void L() { var c = shop.Update("a"); c.ToRequestJson(); } L(); }""")]
    [Arguments("""static void Run() { var c = shop.Update("a"); c.ToRequestJson(); }""")]
    [Arguments("""static IEnumerable<int> Run() { var c = shop.Update("a"); c.ToRequestJson(); yield return 1; }""")]
    [Arguments("""static IEnumerable<string> Run() { var c = shop.Update("a"); yield return c.ToRequestJson(); }""")]
    [Arguments("""static IEnumerable<int> Run() { yield return 1; var c = shop.Update("a"); c.ToRequestJson(); }""")]
    [Arguments("""static IEnumerable<int> Run() { shop.Update("a").ToRequestJson(); yield return 1; }""")]
    [Arguments("""static IEnumerable<int> Run(UpdateCommand<Item> c) { yield return 1; }""")]
    [Arguments("""static async Task Run() { Span<int> s = stackalloc int[1]; s[0] = 1; await Task.Delay(1); }""")]
    [Arguments("""static async Task Run() { await Task.WhenAll(ValueAsync(), ValueAsync()); }""")]
    [Arguments("""static void Run() { var s = new Span<int>(); Action a = () => { }; s.Clear(); }""")]
    [Arguments("""static bool Used = Take(default) && Make2(out UpdateCommand<Item> c); static bool Make2(out UpdateCommand<Item> c) { c = default; return true; }""")]
    public async Task Analyze_WhatCompiles_ReportsNothing(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(Helpers + member);

        // Act
        var report = await Shared.ReportAsync(source);
        var errors = Shared.CompilerErrors(source);

        // Assert
        await Assert.That(errors).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_RefStructOfTheConsumer_ReportsNothing()
    {
        // Arrange: the compiler refuses it, but that is the consumer's type and DynaSharp's message would be wrong.
        var source = AnalyzerRun.Member(
            """static async Task Run() { var c = new Mine(); await Task.Delay(1); c.Use(); var o = new Global(); await Task.Delay(1); o.Use(); }""",
            "public ref struct Mine { public void Use() { } }\npublic ref struct DynaSharpNot { }");

        // Act
        var report = await AnalyzerRun.ReportAsync(source.Replace("namespace Shop;", "namespace Shop;\n\n", StringComparison.Ordinal));

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_RefStructInTheGlobalNamespace_ReportsNothing()
    {
        // Arrange
        var source = """
            using System.Threading.Tasks;
            using DynaSharp;

            public ref struct Mine { public void Use() { } }

            public static class Code
            {
                public static async Task Run() { var c = new Mine(); await Task.Delay(1); c.Use(); }
            }
            """;

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_RefStructInANamespaceThatStartsLikeTheLibrary_ReportsNothing()
    {
        // Arrange
        var source = """
            using System.Threading.Tasks;
            using DynaSharp;

            namespace DynaSharpExtras.Inner;

            public ref struct Mine { public void Use() { } }

            public static class Code
            {
                public static async Task Run() { var c = new Mine(); await Task.Delay(1); c.Use(); }
            }
            """;

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_NewCommandOfTheLibrary_IsCoveredWithoutAList()
    {
        // Arrange: a transaction step, and a type no list could know, which is what a future command looks like.
        var source = AnalyzerRun.Member(
            """static async Task Run(DynamoDbClient client) { var c = default(DynaSharp.Fluent.FutureCommand); await Task.Delay(1); [|c|].Use(); }""",
            "}\n\nnamespace DynaSharp.Fluent\n{\n    public ref struct FutureCommand { public void Use() { } }\n").Replace("namespace Shop;", "namespace Shop\n{", StringComparison.Ordinal) + "}\n";

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""static async Task Run() { var [|c|] = shop.Update("a"); await c.SendAsync(); }""")]
    [Arguments("""static void Run() { Func<Task> f = async () => { var [|c|] = shop.Update("a"); await c.SendAsync(); }; }""")]
    public async Task Analyze_AsyncLocalBeforeCSharp13_IsReported(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(Helpers + member, Outside);

        // Act
        var report = await AnalyzerRun.ReportAsync(source, LanguageVersion.CSharp12);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message));
    }

    [Test]
    [Arguments("""static async Task Run(CancellationToken ct) { await shop.Update("a").Set("x", 1).SendAsync(ct); }""")]
    [Arguments("""static void Run() { var c = shop.Update("a"); c.ToRequestJson(); }""")]
    [Arguments("""static IEnumerable<int> Run() { var c = shop.Update("a"); c.ToRequestJson(); yield return 1; }""")]
    public async Task Analyze_WhatCSharp12Compiles_ReportsNothing(string member)
    {
        // Arrange
        var source = AnalyzerRun.Member(Helpers + member, Outside);

        // Act
        var report = await AnalyzerRun.ReportAsync(source, LanguageVersion.CSharp12);
        var errors = AnalyzerRun.CompilerErrors(source, LanguageVersion.CSharp12);

        // Assert
        await Assert.That(errors).IsEmpty();
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_AwaitOutsideAnAsyncMember_DoesNotThrow()
    {
        // Arrange: a compile error, in an initializer, which has no block above it.
        var source = AnalyzerRun.Member("""static int Value = await ValueAsync(); static Task<int> ValueAsync() => Task.FromResult(1);""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_HelpLink_PointsAtTheDiagnosticsPage()
    {
        // Arrange
        var descriptor = DynaSharp.Generators.Descriptors.CommandOutlivesExpression;

        // Act
        var link = descriptor.HelpLinkUri;

        // Assert
        await Assert.That(link).Contains("spec/diagnostics.md");
    }
}
