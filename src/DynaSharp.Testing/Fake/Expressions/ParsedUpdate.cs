namespace DynaSharp.Testing.Fake;

/// <summary>A parsed <c>UpdateExpression</c>.</summary>
/// <param name="text">The expression as the request has it.</param>
internal sealed class ParsedUpdate(string text)
{
    /// <summary>Gets the expression as the request has it.</summary>
    public string Text { get; } = text;

    /// <summary>Applies the update to an item, or to the key alone for an item that does not exist yet.</summary>
    /// <param name="current">The item, or <see langword="null"/>.</param>
    /// <param name="key">The key of the request, which an update that creates the item writes.</param>
    /// <returns>The new item, and what <c>UPDATED_OLD</c> and <c>UPDATED_NEW</c> return.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an update DynamoDB rejects on this item.</exception>
    public (Item NewImage, Item UpdatedOld, Item UpdatedNew) Apply(Item? current, Item key) => throw FakeError.Unsupported($"the update expression {Text}");
}
