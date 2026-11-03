using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The results follow the Developer Guide, "Projection expressions": only the attributes named come back, nested entries inside their maps and list elements inside their lists,
// and a path that leads nowhere gives nothing. The key is not added, as real DynamoDB does not add it either.
public sealed class ProjectionTests
{
    private const string ItemJson = """
        {"pk":{"S":"key"},"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}},"l":{"L":[{"S":"x"},{"S":"y"},{"M":{"k":{"S":"z"},"j":{"S":"w"}}}]},"s":{"S":"text"}}
        """;

    [Test]
    [Arguments("a.b, l[2].k, l[0], absent", """{"a":{"M":{"b":{"N":"1"}}},"l":{"L":[{"S":"x"},{"M":{"k":{"S":"z"}}}]}}""")]
    [Arguments("a", """{"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}}}""")]
    [Arguments("#k, s", """{"pk":{"S":"key"},"s":{"S":"text"}}""")]
    [Arguments("a.absent", "{}")]
    [Arguments("s.b", "{}")]
    [Arguments("s[0]", "{}")]
    [Arguments("l[5]", "{}")]
    [Arguments("l[2].absent", "{}")]
    [Arguments("a.b, a.c", """{"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}}}""")]
    [Arguments("l[2], l[1]", """{"l":{"L":[{"S":"y"},{"M":{"k":{"S":"z"},"j":{"S":"w"}}}]}}""")]
    public async Task Apply_Projection_TakesTheNamedPathsOnly(string expression, string expected)
    {
        // Arrange
        var projection = FakeExpressions.ParseProjection(expression, Scope(names: """{"#k":"pk"}"""));

        // Act
        var projected = projection.Apply(ExpressionFixture.Item(ItemJson));

        // Assert
        await Assert.That(projected).IsEqualTo(ExpressionFixture.Item(expected));
    }

    [Test]
    public async Task Apply_NoPaths_GivesAnEmptyItem()
    {
        // Arrange
        var item = ExpressionFixture.Item(ItemJson);

        // Act
        var projected = Projection.Apply(item, []);

        // Assert
        await Assert.That(projected.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("a.b", "a")]
    [Arguments("a", "a.b")]
    public async Task Apply_PathInsideAnother_TakesTheOuterWhole(string first, string second)
    {
        // Arrange (the parser rejects such paths; the projection still answers for them, which UPDATED_NEW relies on)
        DocumentPath[] paths = [Path(first), Path(second)];

        // Act
        var projected = Projection.Apply(ExpressionFixture.Item(ItemJson), paths);

        // Assert
        await Assert.That(projected).IsEqualTo(ExpressionFixture.Item("""{"a":{"M":{"b":{"N":"1"},"c":{"N":"2"}}}}"""));
    }

    private static DocumentPath Path(string expression) => FakeExpressions.ParseProjection(expression, Scope()).Paths[0];
}
