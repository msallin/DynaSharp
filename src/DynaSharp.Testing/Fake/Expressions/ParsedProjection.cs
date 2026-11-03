namespace DynaSharp.Testing.Fake;

/// <summary>A parsed <c>ProjectionExpression</c>.</summary>
/// <param name="text">The expression as the request has it.</param>
internal sealed class ParsedProjection(string text)
{
    /// <summary>Gets the expression as the request has it.</summary>
    public string Text { get; } = text;

    /// <summary>Gives what the projection takes of an item: the paths it names, nested ones included, and nothing else, not even the key.</summary>
    /// <param name="item">The item.</param>
    /// <returns>A new item.</returns>
    public Item Apply(Item item) => throw FakeError.Unsupported($"the projection expression {Text}");
}
