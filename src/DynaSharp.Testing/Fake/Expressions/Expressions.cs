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

    /// <summary>The most bytes of an expression, measured on the text as sent: placeholders are not replaced, and the values of the request are not counted.</summary>
    public const int MaxBytes = 4096;

    /// <summary>Gets the message of DynamoDB for an expression that is empty.</summary>
    /// <param name="member">The member the expression came from.</param>
    /// <returns>The message.</returns>
    public static string EmptyMessage(string member) => $"Invalid {member}: The expression can not be empty;";

    /// <summary>Checks the size of an expression.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="member">The member the expression came from, which the message names.</param>
    /// <param name="enveloped"><see langword="true"/> for a write of its own, which DynamoDB answers inside its request validation.</param>
    /// <param name="withSize"><see langword="true"/> to name the size, as DynamoDB does for the condition of an action of a transaction.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression of more than <see cref="MaxBytes"/> bytes.</exception>
    public static void ThrowIfTooLarge(string text, string member, bool enveloped = false, bool withSize = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var size = System.Text.Encoding.UTF8.GetByteCount(text);
        if (size > MaxBytes)
        {
            var message = $"Invalid {member}: Expression size has exceeded the maximum allowed size;{(withSize ? $" expression size: {size}" : string.Empty)}";
            throw enveloped ? FakeError.Enveloped(message) : FakeError.Validation(message);
        }
    }

    /// <summary>Parses a condition: <c>ConditionExpression</c> or <c>FilterExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <param name="member">The member the expression came from, <see cref="ConditionExpression"/> or <see cref="FilterExpression"/>, which the messages name.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedCondition ParseCondition(string text, PlaceholderScope scope, string member = ConditionExpression)
    {
        ThrowIfTooLarge(text, member);
        return new(text, ExpressionParser.ParseCondition(text, member, scope));
    }

    /// <summary>Parses an <c>UpdateExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The update.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedUpdate ParseUpdate(string text, PlaceholderScope scope)
    {
        ThrowIfTooLarge(text, "UpdateExpression");
        return new(text, ExpressionParser.ParseUpdate(text, scope));
    }

    /// <summary>Parses a <c>KeyConditionExpression</c>, which DynamoDB does before it looks at the table.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The condition, to be compiled against the key it queries with <see cref="CompileKeyCondition"/>.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ConditionNode ParseKeyCondition(string text, PlaceholderScope scope)
    {
        ThrowIfTooLarge(text, KeyConditionExpression);
        return ExpressionParser.ParseCondition(text, KeyConditionExpression, scope);
    }

    /// <summary>Compiles a parsed <c>KeyConditionExpression</c> against the key it queries.</summary>
    /// <param name="condition">The condition from <see cref="ParseKeyCondition"/>.</param>
    /// <param name="keySchema">The key of the table or the index.</param>
    /// <returns>The partition and the condition on the sort key.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for a condition the key does not take.</exception>
    public static KeyRange CompileKeyCondition(ConditionNode condition, KeySchema keySchema) => KeyConditionCompiler.Compile(condition, keySchema);

    /// <summary>Parses a <c>ProjectionExpression</c>.</summary>
    /// <param name="text">The expression.</param>
    /// <param name="scope">The placeholders of the request.</param>
    /// <returns>The projection.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an expression DynamoDB rejects.</exception>
    public static ParsedProjection ParseProjection(string text, PlaceholderScope scope)
    {
        ThrowIfTooLarge(text, ProjectionExpression);
        return new(text, ExpressionParser.ParseProjection(text, scope));
    }
}
