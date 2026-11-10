namespace DynaSharp.Serialization;

/// <summary>
/// An empty key in a dictionary, which <see cref="DictionaryConverter{T, TConverter}"/> and <see cref="ReadOnlyDictionaryConverter{T, TConverter}"/> refuse to write, because DynamoDB rejects an empty
/// name in a map. It has a type of its own for the same reason as <see cref="UnspecifiedDateTimeException"/>.
/// </summary>
/// <param name="paramName">The parameter that held the value.</param>
internal sealed class EmptyMapNameException(string paramName)
    : ArgumentException("A key of the dictionary is empty, and DynamoDB has no empty names in a map. Leave the entry out, or give it a name.", paramName);
