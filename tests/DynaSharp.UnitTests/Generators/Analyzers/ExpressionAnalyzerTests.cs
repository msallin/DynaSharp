using System.Text.RegularExpressions;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA210 to DYNA212, DYNA216, DYNA220 and the bare names of DYNA213 on real snippets: which strings are looked at, and where the diagnostic lands.</summary>
public sealed class ExpressionAnalyzerTests
{
    private const string FlagsModel = """

        [Flags] public enum Rights { None = 0, Read = 1, Write = 2 }

        public enum Tint { Red, Green }

        public readonly struct RightsAsText : IDynamoDbConverter<Rights>
        {
            public static void Write(DynamoDbWriter writer, Rights value) => writer.WriteString(value.ToString());

            public static Rights Read(ref DynamoDbReader reader, AttributeType type) => Enum.Parse<Rights>(reader.GetString());
        }

        [DynamoDbDocument]
        public partial class Part
        {
            public Rights Access { get; init; }
        }

        [DynamoDbEntity]
        [Discriminator("DOC")]
        [PartitionKey("DOC#{Id}")]
        public partial class Doc
        {
            public required string Id { get; init; }

            public Rights Access { get; init; }

            public Rights? Optional { get; init; }

            public Tint Tint { get; init; }

            public HashSet<Rights> Many { get; init; } = [];

            public Part? Inner { get; init; }

            [DynamoDbConverter(typeof(RightsAsText))]
            public Rights Stored { get; init; }

            [Version(Missing = VersionMissing.Allow)]
            public long Revision { get; init; }
        }

        public static class Own
        {
            public static Condition BeginsWith(string prefix) => Attr("n").Exists();

            public readonly struct Nested : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter writer, int value) => writer.WriteNumber(value);

                public static int Read(ref DynamoDbReader reader, AttributeType type) => reader.GetNumber<int>();
            }

            public static class Deeper
            {
                public readonly struct Nested : IDynamoDbConverter<int>
                {
                    public static void Write(DynamoDbWriter writer, int value) => writer.WriteNumber(value);

                    public static int Read(ref DynamoDbReader reader, AttributeType type) => reader.GetNumber<int>();
                }
            }
        }

        public static class Foreign
        {
            public static class AttrExtensions
            {
                public static Condition BeginsWith<T, TConverter>(Attr<T, TConverter> attribute, string prefix)
                    where TConverter : IDynamoDbConverter<T> => Attr("n").Exists();
            }
        }
        """;

    private const string OrderModel = """

        [DynamoDbEntity]
        [Discriminator("ORDER")]
        [PartitionKey("USER#{UserId}")]
        [SortKey("ORDER#{OrderId}")]
        [GlobalIndexKey("GSI1", PartitionKey = "MARKET#{Market}")]
        public partial class Order
        {
            public required string UserId { get; init; }

            [KeyOnly]
            public required string OrderId { get; init; }

            public required string Market { get; init; }

            public required decimal Total { get; init; }

            public Address? ShipTo { get; init; }

            public List<string> Tags { get; init; } = [];
        }

        [DynamoDbDocument]
        public partial record Address(string Street);

        [DynamoDbEntity]
        [Discriminator("INVOICE")]
        [PartitionKey("USER#{UserId}")]
        public partial class Invoice
        {
            public required string UserId { get; init; }

            public decimal Refund { get; init; }
        }

        public static class Mine
        {
            public static UpdateCommand<T> Touch<T>(this UpdateCommand<T> command, [InterpolatedStringHandlerArgument("command")] ExpressionHandler action)
                where T : IDynamoDbDocument<T> => command.Set(action);
        }
        """;

    private const string OrderFields = """
        private static readonly Table<Order> orders = null!;
        private static UpdateCommand<Order> update => default;

        """;

    private static readonly AnalyzerFixture WithFlags = new(FlagsModel);

    private static readonly AnalyzerFixture WithOrders = new(OrderModel);

    [Test]
    public async Task Analyze_LiteralLeftOfAnOperator_ReportsItsSpan()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{[|"status"|]} = {"open"}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("status")));
    }

    [Test]
    public async Task Analyze_LiteralInAPathPosition_ReportsItsSpan()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"attribute_exists({[|"pk"|]})");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("pk")));
    }

    [Test]
    public async Task Analyze_NamedConstant_CountsAsConstant()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            """
            private const string Status = "status";
            public static void Run() => shop.Update("a").If($"{[|Status|]} = {1}");
            """);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("status")));
    }

    [Test]
    public async Task Analyze_TargetOfASetAction_IsAName()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").Set($"{[|"total"|]} = {1}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("total")));
    }

    [Test]
    [Arguments("""shop.Update("a").If($"{Attr("status")} = {"open"}");""")]
    [Arguments("""shop.Update("a").If($"begins_with({Attr("sk")}, {"ORDER#"})");""")]
    [Arguments("""shop.Update("a").If($"contains({Attr("tags")}, {"x"})");""")]
    [Arguments("""shop.Update("a").If($"{Attr("a")} IN ({"x"}, {"y"})");""")]
    [Arguments("""var name = "status"; shop.Update("a").If($"{name} = {1}");""")]
    [Arguments("""shop.Update("a").If($"{(AttributeValue)"status"} = {Attr("a")}");""")]
    [Arguments("""shop.Update("a").If($"{1} = {Attr("a")}");""")]
    [Arguments("""shop.Update("a").If($"{new Attr("status")} = {1}");""")]
    [Arguments("""shop.Scan().Filter($"size({Attr("tags")}) > {1}");""")]
    [Arguments("""shop.Update("a").Set($"{Attr("total")} = {Attr("total")} + {5}");""")]
    [Arguments("""shop.Update("a").Set($"{Attr("tags")} = list_append({Attr("tags")}, {AttributeValue.FromList(["b"])})");""")]
    [Arguments("""shop.Update("a").Set($"{Attr("n")} = if_not_exists({Attr("n")}, {0})");""")]
    public async Task Analyze_ValidExpression_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_PlaceholderInTheText_ReportsItsExactSpan()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"attribute_exists([|#pk|]) AND a = [|:v|]");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(
            source,
            "DYNA211 Error: DynaSharp generates placeholders; write the name in Attr(...) and the value in a hole, not '#pk'",
            "DYNA211 Error: DynaSharp generates placeholders; write the name in Attr(...) and the value in a hole, not ':v'"));
    }

    [Test]
    [Arguments("""shop.Update("a").If($"{Attr("sk")} = {"#1"}");""")]
    [Arguments("""shop.Update("a").If($"{Attr("sk")} = {":v"}");""")]
    [Arguments("""shop.Update("a").Set("#odd", 1);""")]
    public async Task Analyze_PlaceholderCharacterInAValueOrName_ReportsNothing(string statement)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_SyntaxErrorInATextPart_ReportsTheToken()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"[|atribute_exists|]({Attr("a")})");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: unknown function 'atribute_exists'; function names are case sensitive"));
    }

    [Test]
    public async Task Analyze_ExpressionThatEndsEarly_ReportsTheWholeString()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If([|$"{Attr("a")} = {1} AND"|]);""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: the expression ends too early"));
    }

    [Test]
    public async Task Analyze_EmptyString_ReportsTheWholeString()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If([|$""|]);""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: the expression ends too early"));
    }

    [Test]
    public async Task Analyze_HoleThatCannotStandThere_ReportsTheHole()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{Attr("a")} {[|Attr("b")|]}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: a hole cannot stand here"));
    }

    [Test]
    public async Task Analyze_EscapeInTheText_ReportsTheWholeTextPart()
    {
        // Arrange: \t is two characters in the source and one in the text, so a position in the text means nothing in the source.
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{Attr("a")}[| ==\t|]{1}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: unexpected '='"));
    }

    [Test]
    public async Task Analyze_StringsAddedTogether_ReadAsOneText()
    {
        // Arrange: the handler sees "attribute_" and "exists(" in a row, so they are one word.
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"attribute_" + $"exists({Attr("a")}) AND {Attr("b")} = " + $"{1}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ErrorInStringsAddedTogether_ReportsBothOfThem()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"[|atribute_" + $"exists(|]{Attr("a")})");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: unknown function 'atribute_exists'; function names are case sensitive"));
    }

    [Test]
    public async Task Analyze_RawMultiLineString_ReadsTheText()
    {
        // Arrange
        var source = AnalyzerRun.Statements(
            """"
            shop.Update("a").If($"""
                attribute_exists({Attr("a")})
                    AND {Attr("b")} = {1}
                """);
            """");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ActionOfATransactionStep_ReadsTheUpdateGrammar()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run(DynamoDbClient client) { client.TransactWrite().Update(shop, "a").Set($"{Attr("a")} = {Attr("a")} + {1}"); }""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ConditionOfATransactionStep_ReadsTheConditionGrammar()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run(DynamoDbClient client) { client.TransactWrite().Delete(shop, "a").If($"{[|"status"|]} = {1}"); }""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("status")));
    }

    [Test]
    public async Task Analyze_MethodOfTheConsumerThatTakesTheHandler_ReadsNoGrammar()
    {
        // Arrange: it may forward to If or to Set, so a valid action must pass as well as a valid condition.
        var source = AnalyzerRun.Member(
            """
            public static UpdateCommand<T> Touch<T>(this UpdateCommand<T> command, [InterpolatedStringHandlerArgument("command")] ExpressionHandler action)
                where T : IDynamoDbDocument<T> => command.Set(action);

            public static void Run() => shop.Update("a").Touch($"{Attr("a")} = {Attr("a")} + {1}");
            """);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_PlaceholderInAMethodOfTheConsumer_IsStillReported()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            """
            public static UpdateCommand<T> Touch<T>(this UpdateCommand<T> command, [InterpolatedStringHandlerArgument("command")] ExpressionHandler action)
                where T : IDynamoDbDocument<T> => command.Set(action);

            public static void Run() => shop.Update("a").Touch($"[|#a|] = {1}");
            """);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA211 Error: DynaSharp generates placeholders; write the name in Attr(...) and the value in a hole, not '#a'"));
    }

    [Test]
    public async Task Analyze_HandlerOutsideAnArgument_ReadsNoGrammar()
    {
        // Arrange: this does not compile, because the handler needs a command, and the analyzer still runs on it in an IDE.
        var source = AnalyzerRun.Statements("""ExpressionHandler handler = $"no ( grammar";""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_HoleOfATypeTheHandlerDoesNotTake_DoesNotThrow()
    {
        // Arrange: a compile error, for which the call of the hole is invalid.
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{Attr("a")} = {new object()}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_InterpolatedStringOfAnotherHandler_IsNotRead()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""var text = new System.Text.StringBuilder(); text.Append($"#not {1} (an expression");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_InterpolatedString_DoesNotTouchAPlainString()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""var text = $"#not {1} (an expression";""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_GeneratedCode_IsSkipped()
    {
        // Arrange
        var source = "// <auto-generated/>\n" + AnalyzerRun.Statements("""shop.Update("a").If($"#a = {1}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_CompilationWithoutDynaSharp_ReportsNothing()
    {
        // Arrange
        var compilation = CSharpCompilation.Create(
            "NoReference",
            [CSharpSyntaxTree.ParseText("""class C { string M(int a) => $"#n = {a}"; }""")],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        // Act
        var diagnostics = await compilation.WithAnalyzers([new ExpressionAnalyzer(), new PathAnalyzer(), new CommandLifetimeAnalyzer(), new ReadTerminalAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task Analyze_ExpressionsOfTheOperationVectorsAsInterpolatedStrings_ReportNothing()
    {
        // Arrange: each placeholder of the vectors in its hole, as a user would write them. botocore wrote them and no DynamoDB ran them, so a bare total in them is reserved, DYNA220, and only the grammar counts here.
        var statements = VectorExpressions.All().Select(static vector => Statement(vector.Kind, vector.Text)).OfType<string>().ToList();
        var source = AnalyzerRun.Statements(string.Join("\n", statements));

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(statements.Count).IsGreaterThan(30);
        await Assert.That(report.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(static line => !line.StartsWith("DYNA220 ", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    [Arguments("""Update("a").If($"begins_with({[|Attr<int>("n")|]}, {"1"})");""", "begins_with")]
    [Arguments("""Update("a").If($"contains({[|Attr<int>("n")|]}, {"1"})");""", "contains")]
    [Arguments("""Update("a").If($"NOT contains( {[|new Attr<bool, BoolConverter>("n")|]}, {"1"})");""", "contains")]
    [Arguments("""Scan().Filter($"begins_with({[|new Attr<DateTimeOffset, EpochSecondsConverter>("n")|]}, {"1"})");""", "begins_with")]
    [Arguments("""Update("a").If($"contains({[|new Attr<int, NullableHandleConverter<int, NumberConverter<int>>>("n")|]}, {"1"})");""", "contains")]
    [Arguments("""Update("a").If($"begins_with({[|new Attr<bool, NullableHandleConverter<bool, BoolConverter>>("n")|]}, {"1"})");""", "begins_with")]
    public async Task Analyze_TextFunctionOnANumber_ReportsTheHandle(string chain, string function)
    {
        // Arrange
        var source = AnalyzerRun.Statements("shop." + chain + ";");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA216 Warning: {function} never matches a number or a boolean, so this condition holds for no item. Compare the attribute, or give it a type that stores text."));
    }

    [Test]
    [Arguments("""Update("a").If($"begins_with({Attr("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<string, StringConverter>("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<Guid, GuidConverter>("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<string, NullableReferenceHandleConverter<string, StringConverter>>("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<Dictionary<string, int>, DictionaryConverter<int, NumberConverter<int>>>("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"size({Attr<int>("n")}) > {1}");""")]
    [Arguments("""Update("a").If($"{Attr<int>("n")} > {1}");""")]
    [Arguments("""Update("a").If($"contains({Attr("list")}, {Attr<int>("n").Value(1)})");""")]
    [Arguments("""Update("a").If($"{"x"} = {Attr<int>("n")}");""")]
    public async Task Analyze_TextFunctionOnText_ReportsNoSuchThing(string chain)
    {
        // Arrange
        var source = AnalyzerRun.Statements("shop." + chain + ";");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).DoesNotContain("DYNA216");
    }

    [Test]
    [Arguments("""Scan().Filter($"begins_with({[|Doc.Attrs.Access|]}, {"1"})");""", "begins_with")]
    [Arguments("""Scan().Filter($"contains({[|Doc.Attrs.Access|]}, {"1"})");""", "contains")]
    [Arguments("""Scan().Filter($"begins_with({[|Doc.Attrs.Optional|]}, {"1"})");""", "begins_with")]
    [Arguments("""Scan().Filter($"contains({[|Doc.Attrs.Inner.At(Part.Attrs.Access)|]}, {"1"})");""", "contains")]
    [Arguments("""Scan().Filter([|Doc.Attrs.Access|].BeginsWith("1"));""", "begins_with")]
    [Arguments("""Scan().Filter([|Doc.Attrs.Optional|].BeginsWith("1"));""", "begins_with")]
    [Arguments("""Scan().Filter(AttrExtensions.BeginsWith([|Doc.Attrs.Access|], "1"));""", "begins_with")]
    public async Task Analyze_TextFunctionOnAFlagsEnum_ReportsTheHandle(string chain, string function)
    {
        // Arrange (a [Flags] enum is stored as a number, which the generated forwarder of its handle does not say)
        var source = AnalyzerRun.Statements("shop." + chain + ";");

        // Act
        var report = await WithFlags.ReportAsync(source);

        // Assert
        await Assert.That(WithFlags.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA216 Warning: {function} never matches a number or a boolean, so this condition holds for no item. Compare the attribute, or give it a type that stores text."));
    }

    [Test]
    [Arguments("""Scan().Filter($"begins_with({[|Doc.Attrs.Revision|]}, {"1"})");""", "begins_with")]
    [Arguments("""Scan().Filter([|Doc.Attrs.Revision|].BeginsWith("1"));""", "begins_with")]
    public async Task Analyze_TextFunctionOnAVersionThatAllowsAnItemWithoutOne_ReportsTheHandle(string chain, string function)
    {
        // Arrange (the handle has a generated converter, which the overload that does not compile cannot name)
        var source = AnalyzerRun.Statements("shop." + chain + ";");

        // Act
        var report = await WithFlags.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA216 Warning: {function} never matches a number or a boolean, so this condition holds for no item. Compare the attribute, or give it a type that stores text."));
    }

    [Test]
    [Arguments("""Scan().Filter($"begins_with({Doc.Attrs.Tint}, {"R"})");""")]
    [Arguments("""Scan().Filter($"contains({Doc.Attrs.Tint}, {"R"})");""")]
    [Arguments("""Scan().Filter(Doc.Attrs.Tint.BeginsWith("R"));""")]
    [Arguments("""Scan().Filter(Doc.Attrs.Id.BeginsWith("R"));""")]
    [Arguments("""Scan().Filter($"contains({Doc.Attrs.Many}, {1})");""")]
    [Arguments("""Scan().Filter($"{Doc.Attrs.Access} = {1}");""")]
    [Arguments("""Scan().Filter($"begins_with({Doc.Attrs.Stored}, {"1"})");""")]
    [Arguments("""Scan().Filter(Doc.Attrs.Stored.BeginsWith("1"));""")]
    [Arguments("""Scan().Filter(Own.BeginsWith("1"));""")]
    [Arguments("""Scan().Filter(Foreign.AttrExtensions.BeginsWith(Doc.Attrs.Access, "1"));""")]
    [Arguments("""Scan().Filter($"begins_with({new Attr<int, Own.Nested>("n")}, {"1"})");""")]
    [Arguments("""Scan().Filter($"begins_with({new Attr<int, Own.Deeper.Nested>("n")}, {"1"})");""")]
    public async Task Analyze_TextFunctionOnAnEnumStoredAsText_ReportsNoSuchThing(string chain)
    {
        // Arrange (an enum is stored by name, and a set of the enum is what contains is for; a method called BeginsWith of another type is none of this)
        var source = AnalyzerRun.Statements("shop." + chain + ";");

        // Act
        var report = await WithFlags.ReportAsync(source);

        // Assert
        await Assert.That(WithFlags.CompilerErrors(source)).IsEmpty();
        await Assert.That(report).DoesNotContain("DYNA216");
    }

    [Test]
    [Arguments("""shop.Update("a").If($"[|status|] = {"open"}");""", "status")]
    [Arguments("""shop.Scan().Filter($"[|Total|] > {100}");""", "Total")]
    [Arguments("""shop.Update("a").If($"attribute_exists([|name|])");""", "name")]
    [Arguments("""shop.Update("a").If($"size([|data|]) > {1}");""", "data")]
    [Arguments("""shop.Update("a").If($"info.[|year|] = {1}");""", "year")]
    [Arguments("""shop.Update("a").If($"{Attr("info")}.[|year|] = {1}");""", "year")]
    [Arguments("""shop.Query("a").Filter($"{Attr("a")} = {1} OR NOT ([|USER|] IN ({1}, {2}))");""", "USER")]
    [Arguments("""shop.Update("a").Set($"a = {1} REMOVE [|data|]");""", "data")]
    [Arguments("""shop.Update("a").Set($"a = {1} ADD [|count|] {1}");""", "count")]
    public async Task Analyze_ReservedWordAsABareName_ReportsTheWord(string statement, string word)
    {
        // Arrange
        var source = AnalyzerRun.Statements(statement);

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Reserved(word)));
    }

    [Test]
    public async Task Analyze_ReservedWordOnBothSidesOfAnAction_ReportsEach()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").Set($"[|count|] = [|count|] + {1}");""");

        // Act
        var report = await AnalyzerFixture.NoModel.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Reserved("count"), Reserved("count")));
    }

    [Test]
    [Arguments("""shop.Update("a").If($"{Attr("status")} = {"open"}");""")]
    [Arguments("""shop.Update("a").If($"kind = {"status"}");""")]
    [Arguments("""shop.Update("a").If($"size(tags) > {1} AND kind BETWEEN {1} AND {2}");""")]
    [Arguments("""shop.Update("a").If($"NOT contains(tags, {"x"})");""")]
    [Arguments("""shop.Update("a").Set($"kind = if_not_exists(kind, {1}) REMOVE tags");""")]
    [Arguments("""update.Touch($"status = {1}");""")]
    [Arguments("""shop.Update("a").If($"size = {1} AND attribute_not_exists(info.size)");""")]
    [Arguments("""shop.Update("a").Set($"a = {1} REMOVE size");""")]
    [Arguments("""shop.Update("a").If($"Size = {1} OR info.SIZE = {2}");""")]
    public async Task Analyze_ReservedWordWhereNoBareNameStands_ReportsNothing(string statement)
    {
        // Arrange (a function, a keyword, a value, a name in a hole, a method of the consumer, which reads no grammar, and size, which DynamoDB takes as a name)
        var source = AnalyzerRun.Member(OrderFields + "public static void Run() { " + statement + " }");

        // Act
        var report = await WithOrders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""orders.Scan().Filter($"[|Totl|] > {1}");""", "Totl")]
    [Arguments("""orders.Scan().Filter($"attribute_exists([|Shipto|].Street)");""", "Shipto")]
    [Arguments("""orders.Scan().Filter($"Market = {"eu"} AND [|Markte|] = {"eu"}");""", "Markte")]
    [Arguments("""update.If($"[|OrderId|] = {"a"}");""", "OrderId")]
    [Arguments("""update.Set($"[|Totl|] = {1}");""", "Totl")]
    [Arguments("""update.Set($"Tags = {1} REMOVE [|Tagz|]");""", "Tagz")]
    [Arguments("""update.Set($"Tags = list_append([|Tagz|], {1})");""", "Tagz")]
    [Arguments("""shop.Query("a").Of<Order, Invoice>().Filter($"[|Nope|] = {1}");""", "Nope")]
    public async Task Analyze_BareNameTheTypeOfTheCommandDoesNotHave_Warns(string statement, string name)
    {
        // Arrange
        var source = AnalyzerRun.Member(OrderFields + "public static void Run() { " + statement + " }");

        // Act
        var report = await WithOrders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA213 Warning: Path '{name}' matches no attribute of 'Order'. Check the spelling, unless the attribute is outside the entity."));
    }

    [Test]
    [Arguments("""orders.Scan().Filter($"Market = {"eu"} AND UserId = {"u"}");""")]
    [Arguments("""orders.Scan().Filter($"attribute_exists(ShipTo.Stret) AND size(Tags) > {1}");""")]
    [Arguments("""orders.Scan().Filter($"pk = {1} AND sk = {1} AND gsi1pk = {1}");""")]
    [Arguments("""update.Set($"Tags = list_append(Tags, {1})");""")]
    [Arguments("""shop.Query("a").Of<Order, Invoice>().Filter($"Refund > {1} AND Market = {"eu"}");""")]
    [Arguments("""shop.Scan().Filter($"Nope = {1}");""")]
    [Arguments("""update.Touch($"Nope = {1}");""")]
    public async Task Analyze_BareNameTheTypeHasOrThatCannotBeChecked_ReportsNothing(string statement)
    {
        // Arrange (a nested name is not looked at, Item has no attributes to compare with, and neither has a method of the consumer)
        var source = AnalyzerRun.Member(OrderFields + "public static void Run() { " + statement + " }");

        // Act
        var report = await WithOrders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_BareNameStartingWithAnUnderscore_ReportsASyntaxErrorThatAsksForAHole()
    {
        // Arrange (real DynamoDB answers a syntax error for a bare _type, FidelityTests question 14; the discriminator goes in a hole, {Attr("_type")})
        var source = AnalyzerRun.Member(OrderFields + "public static void Run() { " + """orders.Scan().Filter($"_type = {1}");""" + " }");

        // Act
        var report = await WithOrders.ReportAsync(source);

        // Assert
        await Assert.That(report).Contains("DYNA212 Warning [", StringComparison.Ordinal).And.Contains("""]: The expression is not valid: the name '_type' starts with an underscore and needs a placeholder; put it in a hole, {Attr("_type")}""", StringComparison.Ordinal);
    }

    [Test]
    public async Task Analyze_BareNameOnATransactionStep_HasNoEntityToCheck()
    {
        // Arrange
        var source = AnalyzerRun.Member(OrderFields + """public static void Run(DynamoDbClient client) { client.TransactWrite().Update(orders, Order.Keys.Of("a", "b")).Set($"Nope = {1}"); }""");

        // Act
        var report = await WithOrders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""orders.Scan().Filter($"[|Total|] > {1}");""", "Total")]
    [Arguments("""update.If($"[|Value|] = {1}");""", "Value")]
    public async Task Analyze_ReservedBareName_IsOnlyTheReservedWord(string statement, string word)
    {
        // Arrange (whether the type has the attribute or not, DynamoDB rejects the word first)
        var source = AnalyzerRun.Member(OrderFields + "public static void Run() { " + statement + " }");

        // Act
        var report = await WithOrders.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Reserved(word)));
    }

    private static string Reserved(string word) => $"DYNA220 Warning: '{word}' is a reserved word of DynamoDB, which rejects it as a bare name in an expression. Write the name in Attr(...) or use a handle, which DynaSharp sends as a #name placeholder.";

    private static string NamePosition(string name) => $"DYNA210 Warning: String '{name}' is a value here, but this position needs a name. Wrap it in Attr(...).";

    // Set($"...") is inside the SET clause, so an update that does not start with SET has no interpolated form.
    private static string? Statement(ExpressionKind kind, string text)
    {
        var holes = Regex.Replace(text, @"#(\w+)", """{Attr("$1")}""");
        holes = Regex.Replace(holes, @":\w+", "{1}");
        if (kind == ExpressionKind.Condition)
        {
            return $$"""shop.Update("a").If($"{{holes}}");""";
        }

        return holes.StartsWith("SET ", StringComparison.OrdinalIgnoreCase) ? $$"""shop.Update("a").Set($"{{holes["SET ".Length..]}}");""" : null;
    }
}
