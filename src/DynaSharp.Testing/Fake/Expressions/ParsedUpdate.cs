namespace DynaSharp.Testing.Fake;

/// <summary>A parsed <c>UpdateExpression</c>.</summary>
/// <param name="text">The expression as the request has it.</param>
/// <param name="actions">The actions, in the order of the expression.</param>
internal sealed class ParsedUpdate(string text, IReadOnlyList<UpdateAction> actions)
{
    /// <summary>Gets the expression as the request has it.</summary>
    public string Text { get; } = text;

    /// <summary>Gets the actions, in the order of the expression.</summary>
    public IReadOnlyList<UpdateAction> Actions { get; } = actions;

    /// <summary>Applies the update to an item, or to the key alone for an item that does not exist yet.</summary>
    /// <param name="current">The item, or <see langword="null"/>.</param>
    /// <param name="key">The key of the request, which an update that creates the item writes.</param>
    /// <returns>The new item, and what <c>UPDATED_OLD</c> and <c>UPDATED_NEW</c> return.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an update DynamoDB rejects on this item.</exception>
    public (Item NewImage, Item UpdatedOld, Item UpdatedNew) Apply(Item? current, Item key) => UpdateApplier.Apply(Actions, current, key);
}
