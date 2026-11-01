using System.Text.RegularExpressions;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA210 to DYNA212 on real snippets: which strings are looked at, and where the diagnostic lands.</summary>
public sealed class ExpressionAnalyzerTests
{
    [Test]
    public async Task Analyze_LiteralLeftOfAnOperator_ReportsItsSpan()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{[|"status"|]} = {"open"}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("status")));
    }

    [Test]
    public async Task Analyze_LiteralInAPathPosition_ReportsItsSpan()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"attribute_exists({[|"pk"|]})");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

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
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, NamePosition("status")));
    }

    [Test]
    public async Task Analyze_TargetOfASetAction_IsAName()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").Set($"{[|"total"|]} = {1}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

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
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_PlaceholderInTheText_ReportsItsExactSpan()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"attribute_exists([|#pk|]) AND a = [|:v|]");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

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
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_SyntaxErrorInATextPart_ReportsTheToken()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"[|atribute_exists|]({Attr("a")})");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: unknown function 'atribute_exists'; function names are case sensitive"));
    }

    [Test]
    public async Task Analyze_ExpressionThatEndsEarly_ReportsTheWholeString()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If([|$"{Attr("a")} = {1} AND"|]);""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: the expression ends too early"));
    }

    [Test]
    public async Task Analyze_EmptyString_ReportsTheWholeString()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If([|$""|]);""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: the expression ends too early"));
    }

    [Test]
    public async Task Analyze_HoleThatCannotStandThere_ReportsTheHole()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{Attr("a")} {[|Attr("b")|]}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: a hole cannot stand here"));
    }

    [Test]
    public async Task Analyze_EscapeInTheText_ReportsTheWholeTextPart()
    {
        // Arrange: \t is two characters in the source and one in the text, so a position in the text means nothing in the source.
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{Attr("a")}[| ==\t|]{1}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA212 Warning: The expression is not valid: unexpected '='"));
    }

    [Test]
    public async Task Analyze_StringsAddedTogether_ReadAsOneText()
    {
        // Arrange: the handler sees "attribute_" and "exists(" in a row, so they are one word.
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"attribute_" + $"exists({Attr("a")}) AND {Attr("b")} = " + $"{1}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ErrorInStringsAddedTogether_ReportsBothOfThem()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"[|atribute_" + $"exists(|]{Attr("a")})");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

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
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ActionOfATransactionStep_ReadsTheUpdateGrammar()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run(DynamoDbClient client) { client.TransactWrite().Update(shop, "a").Set($"{Attr("a")} = {Attr("a")} + {1}"); }""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_ConditionOfATransactionStep_ReadsTheConditionGrammar()
    {
        // Arrange
        var source = AnalyzerRun.Member("""public static void Run(DynamoDbClient client) { client.TransactWrite().Delete(shop, "a").If($"{[|"status"|]} = {1}"); }""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

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
        var report = await AnalyzerRun.ReportAsync(source);

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
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, "DYNA211 Error: DynaSharp generates placeholders; write the name in Attr(...) and the value in a hole, not '#a'"));
    }

    [Test]
    public async Task Analyze_HandlerOutsideAnArgument_ReadsNoGrammar()
    {
        // Arrange: this does not compile, because the handler needs a command, and the analyzer still runs on it in an IDE.
        var source = AnalyzerRun.Statements("""ExpressionHandler handler = $"no ( grammar";""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_HoleOfATypeTheHandlerDoesNotTake_DoesNotThrow()
    {
        // Arrange: a compile error, for which the call of the hole is invalid.
        var source = AnalyzerRun.Statements("""shop.Update("a").If($"{Attr("a")} = {new object()}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_InterpolatedStringOfAnotherHandler_IsNotRead()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""var text = new System.Text.StringBuilder(); text.Append($"#not {1} (an expression");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_InterpolatedString_DoesNotTouchAPlainString()
    {
        // Arrange
        var source = AnalyzerRun.Statements("""var text = $"#not {1} (an expression";""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_GeneratedCode_IsSkipped()
    {
        // Arrange
        var source = "// <auto-generated/>\n" + AnalyzerRun.Statements("""shop.Update("a").If($"#a = {1}");""");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

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
        // Arrange: each placeholder of the vectors in its hole, as a user would write them.
        var statements = VectorExpressions.All().Select(static vector => Statement(vector.Kind, vector.Text)).OfType<string>().ToList();
        var source = AnalyzerRun.Statements(string.Join("\n", statements));

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(statements.Count).IsGreaterThan(30);
        await Assert.That(report).IsEmpty();
    }

    [Test]
    [Arguments("""Update("a").If($"begins_with({[|Attr<int>("n")|]}, {"1"})");""", "begins_with")]
    [Arguments("""Update("a").If($"contains({[|Attr<int>("n")|]}, {"1"})");""", "contains")]
    [Arguments("""Update("a").If($"NOT contains( {[|new Attr<bool, BoolConverter>("n")|]}, {"1"})");""", "contains")]
    [Arguments("""Scan().Filter($"begins_with({[|new Attr<DateTimeOffset, EpochSecondsConverter>("n")|]}, {"1"})");""", "begins_with")]
    [Arguments("""Update("a").If($"contains({[|new Attr<int, OptionalConverter<int, NumberConverter<int>>>("n")|]}, {"1"})");""", "contains")]
    [Arguments("""Update("a").If($"begins_with({[|new Attr<bool, OptionalConverter<bool, BoolConverter>>("n")|]}, {"1"})");""", "begins_with")]
    public async Task Analyze_TextFunctionOnANumber_ReportsTheHandle(string chain, string function)
    {
        // Arrange
        var source = AnalyzerRun.Statements("shop." + chain + ";");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, $"DYNA216 Warning: {function} never matches a number or a boolean, so this condition holds for no item. Compare the attribute, or give it a type that stores text."));
    }

    [Test]
    [Arguments("""Update("a").If($"begins_with({Attr("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<string, StringConverter>("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<Guid, GuidConverter>("n")}, {"1"})");""")]
    [Arguments("""Update("a").If($"contains({new Attr<string, OptionalReferenceConverter<string, StringConverter>>("n")}, {"1"})");""")]
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
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).DoesNotContain("DYNA216");
    }

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
