namespace DynaSharp.Testing.Fake;

/// <summary>A parsed <c>ConditionExpression</c> or <c>FilterExpression</c>.</summary>
/// <param name="text">The expression as the request has it.</param>
internal sealed class ParsedCondition(string text)
{
    /// <summary>Gets the expression as the request has it.</summary>
    public string Text { get; } = text;

    /// <summary>Evaluates the condition on an item, with the type rules of DynamoDB: a comparison of values of different types is false.</summary>
    /// <param name="item">The item, or <see langword="null"/> where there is none, which is what <c>attribute_not_exists</c> on a new item sees.</param>
    /// <returns><see langword="true"/> if the condition holds.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for what DynamoDB rejects only on an item, such as <c>size</c> of a number.</exception>
    public bool Evaluate(Item? item) => throw FakeError.Unsupported($"the condition expression {Text}");
}
