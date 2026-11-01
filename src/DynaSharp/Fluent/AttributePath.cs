using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>Finds one attribute in an item, or in the maps inside it, and reads it by its converter, for what a response gives one attribute of.</summary>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
internal static class AttributePath<TValue, TConverter>
    where TConverter : IDynamoDbConverter<TValue>
{
    /// <summary>Walks the attributes of a map whose brace the reader has consumed, up to and including the closing one.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="rest">What is left of the path: <c>stats.count</c> at the top, <c>count</c> in the map <c>stats</c>.</param>
    /// <param name="path">The whole path, for what a failure says.</param>
    /// <param name="owner">What the attribute is read from, for what a failure says, or <see langword="null"/> for the type of the value.</param>
    /// <param name="value">The value, if it is there.</param>
    /// <returns><see langword="true"/> if the attribute is there.</returns>
    /// <exception cref="DynamoDbMappingException">The value cannot be read as <typeparamref name="TValue"/>.</exception>
    public static bool TryRead(ref DynamoDbReader reader, ReadOnlySpan<char> rest, string path, string? owner, out TValue? value)
    {
        var dot = rest.IndexOf('.');
        var name = dot < 0 ? rest : rest[..dot];
        value = default;
        var found = false;
        while (reader.TryReadAttributeName())
        {
            if (found || !reader.Json.ValueTextEquals(name))
            {
                reader.SkipAttributeValue();
                continue;
            }

            var type = reader.ReadStartAttribute();
            if (dot < 0)
            {
                value = Convert(ref reader, type, path, owner);
                found = true;
            }
            else if (type == AttributeType.Map)
            {
                found = TryRead(ref reader, rest[(dot + 1)..], path, owner, out value);
            }
            else
            {
                reader.Json.Skip();
            }

            reader.ReadEndAttribute();
        }

        return found;
    }

    // What was asked of DynamoDB has happened by now, so this is not a bad response but a value the caller's type cannot hold.
    private static TValue Convert(ref DynamoDbReader reader, AttributeType type, string path, string? owner)
    {
        try
        {
            return TConverter.Read(ref reader, type);
        }
        catch (Exception exception) when (DynamoDbMappingException.IsConversionFailure(exception))
        {
            throw new DynamoDbMappingException(owner ?? typeof(TValue).ToString(), path, exception);
        }
    }
}
