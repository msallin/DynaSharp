namespace DynaSharp.Testing.Fake;

/// <summary>
/// The entry of the expression engine: condition, update, key condition and projection expressions, parsed against the placeholders of their request. Each parse records the
/// placeholders it used in the scope, and the operation calls <see cref="PlaceholderScope.ThrowIfUnused"/> once after the last expression of the request.
/// </summary>
/// <remarks>
/// A parse fails with what DynamoDB answers before it reads an item: a syntax error, a reserved word, a placeholder the maps lack, a function with the wrong operands, overlapping
/// paths. Every message starts with the member, <c>Invalid FilterExpression: ...</c>, which is why a condition is parsed with the member it came from.
/// </remarks>
internal static class Expressions
{
    /// <summary>The member of a condition of a write.</summary>
    public const string ConditionExpression = "ConditionExpression";

    /// <summary>The member of the filter of a query or a scan.</summary>
    public const string FilterExpression = "FilterExpression";

    /// <summary>The member of the key condition of a query.</summary>
    public const string KeyConditionExpression = "KeyConditionExpression";

    /// <summary>The member of an update.</summary>
    public const string UpdateExpression = "UpdateExpression";

    /// <summary>The member of a projection.</summary>
    public const string ProjectionExpression = "ProjectionExpression";

    /// <summary>Parses a condition: <c>ConditionExpression</c> or <c>FilterExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <param name="member">The member the expression came from, <see cref="ConditionExpression"/> or <see cref="FilterExpression"/>, which the messages name.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedCondition ParseCondition(string text, PlaceholderScope scope, string member = ConditionExpression) =>
        new(text, ExpressionParser.ParseCondition(text, member, scope), member);

    /// <summary>Parses an <c>UpdateExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The update.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedUpdate ParseUpdate(string text, PlaceholderScope scope) => new(text, ExpressionParser.ParseUpdate(text, scope));

    /// <summary>Parses a <c>KeyConditionExpression</c> against the key it queries.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <param name="keySchema">The key of the table or the index.</param>
    /// <returns>The partition and the condition on the sort key.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static KeyRange ParseKeyCondition(string text, PlaceholderScope scope, KeySchema keySchema) =>
        KeyConditionCompiler.Compile(ExpressionParser.ParseCondition(text, KeyConditionExpression, scope), keySchema);

    /// <summary>Parses a <c>ProjectionExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The projection.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedProjection ParseProjection(string text, PlaceholderScope scope) => new(text, ExpressionParser.ParseProjection(text, scope));
}
