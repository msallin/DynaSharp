using System.Buffers;
using System.Text;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Expressions;

public sealed class ConditionTests
{
    private static readonly Condition Open = Condition.Of(Attr("status").Eq("open"));

    [Test]
    public async Task Render_Comparisons_WriteTheirOperator()
    {
        // Arrange
        var attribute = Attr("a");

        // Act
        var rendered = new[]
        {
            Render(attribute.Eq("x")),
            Render(attribute.Ne("x")),
            Render(attribute.Lt("x")),
            Render(attribute.Le("x")),
            Render(attribute.Gt("x")),
            Render(attribute.Ge("x")),
        };

        // Assert
        await Assert.That(rendered).IsEquivalentTo(["#n0 = :v0", "#n0 <> :v0", "#n0 < :v0", "#n0 <= :v0", "#n0 > :v0", "#n0 >= :v0"]);
    }

    [Test]
    public async Task Render_NumberComparisons_WriteTheirOperatorAndANumber()
    {
        // Arrange
        var attribute = Attr("a");

        // Act
        var rendered = new[]
        {
            Render(attribute.Eq(1)),
            Render(attribute.Ne(1)),
            Render(attribute.Lt(1)),
            Render(attribute.Le(1)),
            Render(attribute.Gt(1)),
            Render(attribute.Ge(1)),
        };

        // Assert
        await Assert.That(rendered).IsEquivalentTo(["#n0 = :v0", "#n0 <> :v0", "#n0 < :v0", "#n0 <= :v0", "#n0 > :v0", "#n0 >= :v0"]);
    }

    [Test]
    public async Task Render_TypedComparisons_WriteTheirOperator()
    {
        // Arrange
        var attribute = Attr<int>("a");

        // Act
        var rendered = new[]
        {
            Render(attribute.Eq(1)),
            Render(attribute.Ne(1)),
            Render(attribute.Lt(1)),
            Render(attribute.Le(1)),
            Render(attribute.Gt(1)),
            Render(attribute.Ge(1)),
        };

        // Assert
        await Assert.That(rendered).IsEquivalentTo(["#n0 = :v0", "#n0 <> :v0", "#n0 < :v0", "#n0 <= :v0", "#n0 > :v0", "#n0 >= :v0"]);
    }

    [Test]
    public async Task Render_Between_WritesBothBounds()
    {
        // Arrange
        var attribute = Attr("a");

        // Act
        var rendered = new[] { Render(attribute.Between("a", "z")), Render(attribute.Between(1, 9)), Render(Attr<int>("a").Between(1, 9)) };

        // Assert
        await Assert.That(rendered.Distinct().Single()).IsEqualTo("#n0 BETWEEN :v0 AND :v1");
    }

    [Test]
    public async Task Render_Existence_WritesTheFunction()
    {
        // Arrange
        var untyped = Attr("a");
        var typed = Attr<int>("a");

        // Act
        var rendered = new[] { Render(untyped.Exists()), Render(untyped.NotExists()), Render(typed.Exists()), Render(typed.NotExists()) };

        // Assert
        await Assert.That(rendered).IsEquivalentTo(["attribute_exists(#n0)", "attribute_not_exists(#n0)", "attribute_exists(#n0)", "attribute_not_exists(#n0)"]);
    }

    [Test]
    public async Task Render_BeginsWith_WritesTheFunction()
    {
        // Arrange
        var prefix = "ORDER#";

        // Act
        var rendered = new[] { Render(Attr("sk").BeginsWith(prefix)), Render(Attr<int>("sk").BeginsWith(prefix)) };

        // Assert
        await Assert.That(rendered.Distinct().Single()).IsEqualTo("begins_with(#n0, :v0)");
    }

    [Test]
    public async Task Render_Composition_NestsInParentheses()
    {
        // Arrange
        var condition = Attr("a").Eq(1).And(Attr("b").Eq(2).Or(Attr("c").Eq(3))).Negate();

        // Act
        var rendered = Render(condition);

        // Assert
        await Assert.That(rendered).IsEqualTo("NOT ((#n0 = :v0 AND (#n1 = :v1 OR #n2 = :v2)))");
    }

    [Test]
    public async Task Render_HeapConditionComposedWithAStruct_ConvertsTheStruct()
    {
        // Arrange
        var version = 3;

        // Act
        var rendered = Render(Open & Attr("version").Eq(version));

        // Assert
        await Assert.That(rendered).IsEqualTo("(#n0 = :v0 AND #n1 = :v1)");
    }

    [Test]
    public async Task Render_HeapOperators_ComposeAtRunTime()
    {
        // Arrange
        Condition condition = Attr("a").Exists();

        // Act
        condition |= Attr("b").BeginsWith("x");
        condition &= Attr("c").Between(1, 2);
        var rendered = Render(!condition);

        // Assert
        await Assert.That(rendered).IsEqualTo("NOT (((attribute_exists(#n0) OR begins_with(#n1, :v0)) AND #n2 BETWEEN :v1 AND :v2))");
    }

    [Test]
    public async Task Render_EveryStructConvertsToAHeapCondition_RendersTheSame()
    {
        // Arrange
        var compare = Attr("a").Eq(1);
        var between = Attr("a").Between(1, 2);
        var exists = Attr("a").Exists();
        var beginsWith = Attr("a").BeginsWith("x");
        var and = compare.And(exists);
        var or = compare.Or(exists);
        var not = compare.Negate();

        // Act
        Condition[] heap = [compare, between, exists, beginsWith, and, or, not];
        var rendered = heap.Select(condition => Render(condition)).ToArray();

        // Assert
        await Assert.That(rendered).IsEquivalentTo([Render(compare), Render(between), Render(exists), Render(beginsWith), Render(and), Render(or), Render(not)]);
    }

    [Test]
    public async Task Text_CharactersJsonCannotCarry_AreEscaped()
    {
        // Arrange
        var condition = new Verbatim("a\"b");

        // Act
        var rendered = Render(condition);

        // Assert
        await Assert.That(rendered).IsEqualTo("a\\\"b");
    }

    [Test]
    public async Task Attr_ToString_IsThePath()
    {
        // Arrange
        var untyped = Attr("a.b");
        var typed = Attr<int>("a.b");

        // Act
        string converted = untyped;

        // Assert
        await Assert.That((untyped.ToString(), typed.ToString(), converted, untyped.Path, typed.Path)).IsEqualTo(("a.b", "a.b", "a.b", "a.b", "a.b"));
    }

    [Test]
    public async Task Attr_EmptyPath_Throws()
    {
        // Arrange
        var path = string.Empty;

        // Act
        var failures = new Exception[]
        {
            await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(Attr(path))),
            await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(Attr<int>(path))),
        };

        // Assert
        await Assert.That(failures.Length).IsEqualTo(2);
    }

    [Test]
    public async Task SortKey_EveryOperator_RendersAfterThePartitionKey()
    {
        // Arrange
        var keys = new[] { SortKey.Eq("a"), SortKey.Lt("a"), SortKey.Le("a"), SortKey.Gt("a"), SortKey.Ge("a"), SortKey.Between("a", "b"), SortKey.BeginsWith("a"), default };

        // Act
        var rendered = keys.Select(Render).ToArray();

        // Assert
        await Assert.That(rendered).IsEquivalentTo(
        [
            " AND #n0 = :v0",
            " AND #n0 < :v0",
            " AND #n0 <= :v0",
            " AND #n0 > :v0",
            " AND #n0 >= :v0",
            " AND #n0 BETWEEN :v0 AND :v1",
            " AND begins_with(#n0, :v0)",
            string.Empty,
        ]);
    }

    private static string Render<T>(T condition)
        where T : ICondition
    {
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        writer.AppendCondition(Clause.Condition, condition);
        return Encoding.UTF8.GetString(writer[Clause.Condition].WrittenSpan);
    }

    private static string Render(SortKey key)
    {
        var writer = new ExpressionWriter(ArrayPool<byte>.Shared);
        key.Render(writer, writer[Clause.KeyCondition], "sk");
        return Encoding.UTF8.GetString(writer[Clause.KeyCondition].WrittenSpan);
    }

    private readonly struct Verbatim(string text) : ICondition
    {
        public void Render(ref ExpressionSink sink) => sink.Text(Encoding.UTF8.GetBytes(text));
    }
}
