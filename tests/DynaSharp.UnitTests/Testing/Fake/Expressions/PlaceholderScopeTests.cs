using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The rules of the Developer Guide, "Expression attribute names" and "Expression attribute values": a placeholder must be in its map, and every entry of the maps must be used by
// an expression of the request. The messages are those of DynamoDB.
public sealed class PlaceholderScopeTests
{
    [Test]
    public async Task ResolveName_DefinedPlaceholder_ReturnsTheName()
    {
        // Arrange
        var scope = Scope(names: """{"#s":"status"}""");

        // Act
        var name = scope.ResolveName("#s", FakeExpressions.ConditionExpression);

        // Assert
        await Assert.That(name).IsEqualTo("status");
    }

    [Test]
    [Arguments(null)]
    [Arguments("""{"#other":"a"}""")]
    public async Task ResolveName_UndefinedPlaceholder_ThrowsValidation(string? names)
    {
        // Arrange
        var scope = Scope(names: names);

        // Act
        void Act() => scope.ResolveName("#s", FakeExpressions.FilterExpression);

        // Assert
        await Assert.That(Act).Throws<FakeError>()
            .WithMessage("Invalid FilterExpression: An expression attribute name used in the document path is not defined; attribute name: #s", StringComparison.Ordinal);
    }

    [Test]
    public async Task ResolveValue_DefinedPlaceholder_ReturnsTheValue()
    {
        // Arrange
        var scope = Scope("""{":v":{"N":"1.50"}}""");

        // Act
        var value = scope.ResolveValue(":v", FakeExpressions.ConditionExpression);

        // Assert
        await Assert.That(value).IsEqualTo(AttributeValue.FromNumber("1.5"));
    }

    [Test]
    [Arguments(null)]
    [Arguments("""{":other":{"S":"a"}}""")]
    public async Task ResolveValue_UndefinedPlaceholder_ThrowsValidation(string? values)
    {
        // Arrange
        var scope = Scope(values);

        // Act
        void Act() => scope.ResolveValue(":v", FakeExpressions.UpdateExpression);

        // Assert
        await Assert.That(Act).Throws<FakeError>()
            .WithMessage("Invalid UpdateExpression: An expression attribute value used in expression is not defined; attribute value: :v", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfUnused_NoMaps_DoesNotThrow()
    {
        // Arrange
        var scope = Scope();

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).ThrowsNothing();
    }

    [Test]
    public async Task ThrowIfUnused_EveryEntryUsed_DoesNotThrow()
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"a"}}""", """{"#a":"a"}""");
        scope.ResolveName("#a", FakeExpressions.ConditionExpression);
        scope.ResolveValue(":v", FakeExpressions.UpdateExpression);

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).ThrowsNothing();
    }

    [Test]
    public async Task ThrowIfUnused_UnusedNames_ThrowsValidationNamingThemAll()
    {
        // Arrange
        var scope = Scope(names: """{"#a":"a","#b":"b","#c":"c"}""");
        scope.ResolveName("#b", FakeExpressions.ConditionExpression);

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Value provided in ExpressionAttributeNames unused in expressions: keys: {#a, #c}", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfUnused_UnusedValue_ThrowsValidation()
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"a"}}""");

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Value provided in ExpressionAttributeValues unused in expressions: keys: {:v}", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfUnused_UnusedNameAndValue_ThrowsForTheNameFirst()
    {
        // Arrange
        var scope = Scope("""{":v":{"S":"a"}}""", """{"#a":"a"}""");

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage("Value provided in ExpressionAttributeNames unused in expressions: keys: {#a}", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("{}", null, "1 validation error detected: ExpressionAttributeNames must not be empty")]
    [Arguments(null, "{}", "1 validation error detected: ExpressionAttributeValues must not be empty")]
    [Arguments("""{"a":"a"}""", null, "ExpressionAttributeNames contains invalid key: Syntax error; key: \"a\"")]
    [Arguments("""{"#":"a"}""", null, "ExpressionAttributeNames contains invalid key: Syntax error; key: \"#\"")]
    [Arguments("""{"#a-b":"a"}""", null, "ExpressionAttributeNames contains invalid key: Syntax error; key: \"#a-b\"")]
    [Arguments("""{":a":"a"}""", null, "ExpressionAttributeNames contains invalid key: Syntax error; key: \":a\"")]
    [Arguments(null, """{"#v":{"S":"a"}}""", "ExpressionAttributeValues contains invalid key: Syntax error; key: \"#v\"")]
    [Arguments(null, """{":v.w":{"S":"a"}}""", "ExpressionAttributeValues contains invalid key: Syntax error; key: \":v.w\"")]
    public async Task ThrowIfUnused_InvalidMap_ThrowsValidation(string? names, string? values, string message)
    {
        // Arrange
        var scope = Scope(values, names);

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfUnused_PlaceholdersOfSeveralExpressions_CountsThemAll()
    {
        // Arrange (one request: the condition uses #a and :v, the update #b and :w)
        var scope = Scope("""{":v":{"N":"1"},":w":{"N":"2"}}""", """{"#a":"a","#b":"b"}""");
        _ = FakeExpressions.ParseCondition("#a = :v", scope);
        _ = FakeExpressions.ParseUpdate("SET #b = :w", scope);

        // Act
        void Act() => scope.ThrowIfUnused();

        // Assert
        await Assert.That(Act).ThrowsNothing();
    }
}
