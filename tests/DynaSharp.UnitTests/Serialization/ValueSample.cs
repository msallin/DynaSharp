namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// An attribute value as a test argument. For the type of a test parameter, TUnit generates a conversion through each of its implicit
/// operators, and the one <see cref="AttributeValue"/> blocks from <see cref="char"/> does not compile, so tests take this record instead.
/// </summary>
/// <param name="Json">The JSON that belongs to the value.</param>
/// <param name="Value">The value.</param>
public sealed record ValueSample(string Json, AttributeValue Value);
