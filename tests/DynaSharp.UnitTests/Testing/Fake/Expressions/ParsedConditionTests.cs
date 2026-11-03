using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The attributes a condition reads are the first steps of its document paths, as the Developer Guide's "Specifying item attributes" defines a path: what a Query checks
// a filter against for key attributes.
public sealed class ParsedConditionTests
{
    [Test]
    [Arguments("#k = :v AND size(m.a[0]) > :n", "m pk")]
    [Arguments("NOT a = :v OR b BETWEEN c AND :n", "a b c")]
    [Arguments("x IN (y, :v)", "x y")]
    [Arguments("attribute_exists(f.g) AND contains(h, :v)", "f h")]
    [Arguments("a = :v AND a.b = :v", "a")]
    [Arguments(":v = :v", "")]
    public async Task AttributeNames_Condition_AreTheRootsOfItsPaths(string expression, string expected)
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"x"},":n":{"N":"1"}}""", """{"#k":"pk"}""");

        // Act
        var condition = FakeExpressions.ParseCondition(expression, scope, FakeExpressions.FilterExpression);

        // Assert
        await Assert.That(string.Join(' ', condition.AttributeNames.Order(StringComparer.Ordinal))).IsEqualTo(expected);
    }
}
