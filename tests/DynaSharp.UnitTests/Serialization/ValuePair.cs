namespace DynaSharp.UnitTests.Serialization;

/// <summary>Two attribute values as a test argument, see <see cref="ValueSample"/>.</summary>
/// <param name="Left">The first value.</param>
/// <param name="Right">The second value.</param>
public sealed record ValuePair(AttributeValue Left, AttributeValue Right);
