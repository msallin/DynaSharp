using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA122: a constant for a placeholder of a generated key builder that every run turns away.</summary>
public sealed class KeyValueAnalyzerTests
{
    private const string Entities = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("ORDER#{Id}")]
        [SortKey("LINE#{Line}#{Code:raw}")]
        [GlobalIndexKey("ByStatus", PartitionKey = "STATUS#{Status}", SortKey = "{At:ticks}#{Id}")]
        public partial class Order
        {
            public required string Id { get; init; }

            public required string Line { get; init; }

            public required string Code { get; init; }

            public required string Status { get; init; }

            public required DateTimeOffset At { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("CUSTOMER")]
        public partial class Customer
        {
            [PartitionKey]
            public required string Id { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("TAG")]
        [PartitionKey("{Name}", AttributeName = "tag")]
        public partial class Tag
        {
            public required string Name { get; init; }
        }

        [DynamoDbDocument]
        public partial class Address
        {
            public string? Street { get; init; }

            public static class Keys
            {
                public static string Of(string id) => id;

                public static class Pk
                {
                    public static string Of(string id) => id;
                }
            }
        }

        public static class Keys
        {
            public static string Of(string id) => id;
        }

        public static class Outer
        {
            public static class Keys
            {
                public static string Of(string id) => id;

                public static class Pk
                {
                    public static string Of(string id) => id;
                }
            }

            public static class Inner
            {
                public static string Of(string id) => id;
            }
        }
        """;

    private static readonly AnalyzerFixture Shared = new(Entities);

    [Test]
    [Arguments("""Order.Keys.Of([|"a#1"|], "l", "c")""", "id", "holds '#', a delimiter of 'ORDER#{Id}'")]
    [Arguments("""Order.Keys.Of([|Text.Id|], "l", "c")""", "id", "holds '#', a delimiter of 'ORDER#{Id}'")]
    [Arguments("""Order.Keys.Of([|"a" + "#" + "1"|], "l", "c")""", "id", "holds '#', a delimiter of 'ORDER#{Id}'")]
    [Arguments("""Order.Keys.Of(line: "l", code: "c", id: [|"a#1"|])""", "id", "holds '#', a delimiter of 'ORDER#{Id}'")]
    [Arguments("""Order.Keys.Of("a", [|""|], "c")""", "line", "is empty")]
    [Arguments("""Order.Keys.Of("a", [|string.Empty|], "c")""", "line", "is empty")]
    [Arguments("""Order.Keys.Of("a", [|"l#2"|], "c")""", "line", "holds '#', a delimiter of 'LINE#{Line}#{Code:raw}'")]
    [Arguments("""Order.Keys.Of("a", "l", [|""|])""", "code", "is empty")]
    [Arguments("""Order.Keys.Pk.Of([|"a#1"|])""", "id", "holds '#', a delimiter of 'ORDER#{Id}'")]
    [Arguments("""Order.Keys.Pk.Format([|""|])""", "id", "is empty")]
    [Arguments("""Order.Keys.Sk.Eq([|"l#2"|], "c")""", "line", "holds '#', a delimiter of 'LINE#{Line}#{Code:raw}'")]
    [Arguments("""Order.Keys.Sk.BeginsWith([|"l#2"|])""", "line", "holds '#', a delimiter of 'LINE#{Line}#{Code:raw}'")]
    [Arguments("""Order.Keys.Sk.PrefixPartial("l", [|""|])""", "code", "is empty")]
    [Arguments("""Order.Keys.Sk.Between([|"l#2"|], "a", "b")""", "line", "holds '#', a delimiter of 'LINE#{Line}#{Code:raw}'")]
    [Arguments("""Order.Keys.Sk.Between("l", [|""|], "b")""", "from", "is empty")]
    [Arguments("""Order.Keys.Sk.Between("l", "a", [|""|])""", "to", "is empty")]
    [Arguments("""Order.Keys.ByStatusPk.Of([|"open#late"|])""", "status", "holds '#', a delimiter of 'STATUS#{Status}'")]
    [Arguments("""Order.Keys.ByStatusSk.Eq(DateTimeOffset.UnixEpoch, [|"a#1"|])""", "id", "holds '#', a delimiter of '{At:ticks}#{Id}'")]
    [Arguments("""Tag.Keys.Of([|""|])""", "name", "is empty")]
    public async Task Analyze_ConstantThatTheTemplateRefuses_ReportsTheArgument(string call, string parameter, string problem)
    {
        // Arrange (a key that a test or a seed writes with a literal: the # of an id that came from another system, or a placeholder left empty)
        var source = Run(call);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA122 Warning: The value of '{parameter}' {problem}, so this call throws KeyFormatException every time"));
    }

    [Test]
    [Arguments("""Order.Keys.Of("a", "l", "c")""")]
    [Arguments("""Order.Keys.Of("a", "l", "c#d")""")]
    [Arguments("""Order.Keys.Of(args[0], args[1], args[2])""")]
    [Arguments("""Order.Keys.Of(Text.Other, Text.Empty, "c")""")]
    [Arguments("""Order.Keys.TryOf("a#1", "l", "c")""")]
    [Arguments("""Order.Keys.Pk.TryOf("a#1")""")]
    [Arguments("""Order.Keys.Pk.Parse("ORDER#a")""")]
    [Arguments("""Order.Keys.Pk.TryParse("ORDER#a", out _)""")]
    [Arguments("""Order.Keys.Pk.Write(null!, "a#1")""")]
    [Arguments("""Order.Keys.Sk.Between("l", "a#1", "b#2")""")]
    [Arguments("""Order.Keys.Of("a", "l", "c").WriteKey(null!)""")]
    [Arguments("""Customer.Keys.Of("")""")]
    [Arguments("""Customer.Keys.Pk.Of("")""")]
    [Arguments("""Tag.Keys.Of("a#1")""")]
    [Arguments("""Address.Keys.Of("")""")]
    [Arguments("""Address.Keys.Pk.Of("")""")]
    [Arguments("""Keys.Of("")""")]
    [Arguments("""Outer.Keys.Of("")""")]
    [Arguments("""Outer.Keys.Pk.Of("")""")]
    [Arguments("""Outer.Inner.Of("")""")]
    [Arguments("""string.Concat("a#1", "")""")]
    public async Task Analyze_CallThatCanHoldTheValue_ReportsNothing(string call)
    {
        // Arrange (a raw value takes the delimiter, a plain key and a template of one placeholder have none, TryOf answers null, Parse takes a whole key, and the rest is not a generated builder)
        var source = Run(call);

        // Act
        var report = await ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CodeWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        const string Source = "public static class Code { public static string Run(string a) => a.Trim(); }";

        // Act
        var report = await AnalyzerRun.ReportWithAsync(Source, new KeyValueAnalyzer());

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Run(string call) => AnalyzerRun.Member($$"""
        private static class Text
        {
            public const string Id = "a" + "#1";

            public static readonly string Other = "a#1";

            public static readonly string Empty = "";
        }

        public static void Run(string[] args)
        {
            {{call}};
        }
        """);

    private static Task<string> ReportAsync(string source) => Shared.ReportWithAsync(source, new KeyValueAnalyzer());
}
