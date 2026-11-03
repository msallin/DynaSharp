namespace DynaSharp.Testing.Fake;

/// <summary>
/// The entry of the expression engine: condition, update, key condition and projection expressions, parsed against the placeholders of their request. Each parse records the
/// placeholders it used in the scope, and the operation calls <see cref="PlaceholderScope.ThrowIfUnused"/> once after the last expression of the request.
/// </summary>
internal static class Expressions
{
    /// <summary>Parses a condition: <c>ConditionExpression</c> or <c>FilterExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedCondition ParseCondition(string text, PlaceholderScope scope) => throw FakeError.Unsupported("a condition expression");

    /// <summary>Parses an <c>UpdateExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The update.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedUpdate ParseUpdate(string text, PlaceholderScope scope) => throw FakeError.Unsupported("an update expression");

    /// <summary>Parses a <c>KeyConditionExpression</c> against the key it queries.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <param name="keySchema">The key of the table or the index.</param>
    /// <returns>The partition and the condition on the sort key.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static KeyRange ParseKeyCondition(string text, PlaceholderScope scope, KeySchema keySchema) => throw FakeError.Unsupported("a key condition expression");

    /// <summary>Parses a <c>ProjectionExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The projection.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedProjection ParseProjection(string text, PlaceholderScope scope) => throw FakeError.Unsupported("a projection expression");
}
