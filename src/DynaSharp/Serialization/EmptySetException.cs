namespace DynaSharp.Serialization;

/// <summary>
/// An empty set in a list or a map, which <see cref="NonEmptySetConverter{T, TElement, TConverter}"/> refuses to write, because DynamoDB rejects one. It has a type of its own for the same reason as
/// <see cref="UnspecifiedDateTimeException"/>.
/// </summary>
/// <param name="paramName">The parameter that held the value.</param>
internal sealed class EmptySetException(string paramName)
    : ArgumentException("A DynamoDB set cannot be empty, and DynamoDB rejects one also inside a list or a map. Leave the empty set out, or store its elements as a list.", paramName);
