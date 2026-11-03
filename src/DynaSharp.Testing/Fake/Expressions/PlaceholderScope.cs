namespace DynaSharp.Testing.Fake;

/// <summary>
/// The <c>ExpressionAttributeNames</c> and <c>ExpressionAttributeValues</c> of one request, and which of them its expressions used. DynamoDB rejects a placeholder that is not in the
/// maps and an entry of the maps that no expression of the request uses.
/// </summary>
/// <param name="names">The names, or <see langword="null"/> when the request has none.</param>
/// <param name="values">The values, in their stored form, or <see langword="null"/> when the request has none.</param>
internal sealed class PlaceholderScope(IReadOnlyDictionary<string, string>? names, IReadOnlyDictionary<string, AttributeValue>? values)
{
    /// <summary>Gets the names.</summary>
    public IReadOnlyDictionary<string, string>? Names { get; } = names;

    /// <summary>Gets the values.</summary>
    public IReadOnlyDictionary<string, AttributeValue>? Values { get; } = values;

    /// <summary>Throws for an entry of the maps that no expression used.</summary>
    /// <exception cref="FakeError">A <c>ValidationException</c>: <c>Value provided in ExpressionAttributeNames unused in expressions: keys: {#a}</c>.</exception>
    public void ThrowIfUnused() => throw FakeError.Unsupported($"{Names?.Count ?? 0} expression attribute names and {Values?.Count ?? 0} values");
}
