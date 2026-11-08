using DynaSharp.Testing.Fake;
using static DynaSharp.UnitTests.Testing.Fake.Expressions.ExpressionFixture;
using FakeExpressions = DynaSharp.Testing.Fake.Expressions;

namespace DynaSharp.UnitTests.Testing.Fake.Expressions;

// The limit of the Developer Guide, "Expression attribute names": an expression is at most 4096 bytes, measured on the text as sent. The wording is what the Parity Suite recorded of DynamoDB
// (tier3/limits/expressionSize).
public sealed class ExpressionsTests
{
    [Test]
    public async Task ThrowIfTooLarge_ExpressionOf4096Bytes_Passes()
    {
        // Arrange
        var text = new string('a', FakeExpressions.MaxBytes);

        // Act
        var act = () => FakeExpressions.ThrowIfTooLarge(text, FakeExpressions.ConditionExpression);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task ThrowIfTooLarge_ExpressionOf4097Bytes_NamesTheMemberAndNoSize()
    {
        // Arrange
        var text = new string('a', FakeExpressions.MaxBytes + 1);

        // Act
        var act = () => FakeExpressions.ThrowIfTooLarge(text, FakeExpressions.FilterExpression);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Invalid FilterExpression: Expression size has exceeded the maximum allowed size;", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfTooLarge_MultiByteCharacters_AreCountedAsBytes()
    {
        // Arrange: 2049 characters of 2 bytes each are 4098 bytes
        var text = new string('é', 2049);

        // Act
        var act = () => FakeExpressions.ThrowIfTooLarge(text, FakeExpressions.ProjectionExpression);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Invalid ProjectionExpression: Expression size has exceeded the maximum allowed size;", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(true, false, "1 validation error detected: Invalid UpdateExpression: Expression size has exceeded the maximum allowed size;")]
    [Arguments(false, true, "Invalid UpdateExpression: Expression size has exceeded the maximum allowed size; expression size: 4100")]
    [Arguments(true, true, "1 validation error detected: Invalid UpdateExpression: Expression size has exceeded the maximum allowed size; expression size: 4100")]
    public async Task ThrowIfTooLarge_EnvelopeAndSizeOnRequest_AreWhatTheCallerAsks(bool enveloped, bool withSize, string message)
    {
        // Arrange
        var text = new string('a', 4100);

        // Act
        var act = () => FakeExpressions.ThrowIfTooLarge(text, "UpdateExpression", enveloped, withSize);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task ThrowIfTooLarge_Null_ThrowsArgumentNullException()
    {
        // Arrange
        // Act
        var act = () => FakeExpressions.ThrowIfTooLarge(null!, "ConditionExpression");

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ParseCondition_ExpressionOver4096Bytes_ThrowsBeforeParsingIt()
    {
        // Arrange: the text is no expression at all, and the size comes first
        var scope = Scope("{}");
        var text = new string('(', FakeExpressions.MaxBytes + 1);

        // Act
        var act = () => FakeExpressions.ParseCondition(text, scope);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Invalid ConditionExpression: Expression size has exceeded the maximum allowed size;", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseUpdate_ExpressionOver4096Bytes_ThrowsBeforeParsingIt()
    {
        // Arrange
        var scope = Scope("{}");
        var text = "SET " + new string('a', FakeExpressions.MaxBytes);

        // Act
        var act = () => FakeExpressions.ParseUpdate(text, scope);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Invalid UpdateExpression: Expression size has exceeded the maximum allowed size;", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseProjection_ExpressionOver4096Bytes_ThrowsBeforeParsingIt()
    {
        // Arrange
        var scope = Scope("{}");
        var text = new string('a', FakeExpressions.MaxBytes + 1);

        // Act
        var act = () => FakeExpressions.ParseProjection(text, scope);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Invalid ProjectionExpression: Expression size has exceeded the maximum allowed size;", StringComparison.Ordinal);
    }

    [Test]
    public async Task ParseKeyCondition_ExpressionOver4096Bytes_ThrowsBeforeParsingIt()
    {
        // Arrange
        var scope = Scope("{}");
        var text = "pk = :p AND " + new string('a', FakeExpressions.MaxBytes);

        // Act
        var act = () => FakeExpressions.ParseKeyCondition(text, scope);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("Invalid KeyConditionExpression: Expression size has exceeded the maximum allowed size;", StringComparison.Ordinal);
    }
}
