using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// Paths of the Developer Guide, "Specifying item attributes when using expressions": a name, a map entry after a dot, a list element in brackets.
public sealed class DocumentPathTests
{
    [Test]
    public async Task ToString_Path_PrintsItAsDynamoDbDoes()
    {
        // Arrange
        var path = new DocumentPath([PathSegment.Of("a"), PathSegment.Of("b"), PathSegment.At(0)]);

        // Act
        var text = path.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("[a, b, [0]]");
    }

    [Test]
    public async Task TryResolve_NoItem_FindsNothing()
    {
        // Arrange
        var path = new DocumentPath([PathSegment.Of("a")]);

        // Act
        var found = path.TryResolve(null, out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task TryResolve_NestedPath_FindsTheValue()
    {
        // Arrange
        var path = new DocumentPath([PathSegment.Of("a"), PathSegment.At(1), PathSegment.Of("b")]);
        var item = Item("""{"a":{"L":[{"S":"x"},{"M":{"b":{"N":"7"}}}]}}""");

        // Act
        var found = path.TryResolve(item, out var value);

        // Assert
        await Assert.That((found, value)).IsEqualTo((true, AttributeValue.FromNumber("7")));
    }

    [Test]
    [Arguments("a.b", "a.c")]
    [Arguments("a[0]", "a[1]")]
    [Arguments("a", "b")]
    [Arguments("ab", "a")]
    public async Task ThrowIfOverlapping_PathsApart_DoesNotThrow(string first, string second)
    {
        // Arrange
        var scope = Scope();
        var paths = FakeExpressions.ParseProjection(first, scope).Paths.Concat(FakeExpressions.ParseProjection(second, scope).Paths).ToList();

        // Act
        void Act() => DocumentPath.ThrowIfOverlapping(paths, FakeExpressions.ProjectionExpression);

        // Assert
        await Assert.That(Act).ThrowsNothing();
    }
}
