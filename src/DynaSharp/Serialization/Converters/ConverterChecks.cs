using System.ComponentModel;
using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// The checks every converter makes before it reads a value. Converters and the code the source generator emits call them, so a wrong
/// type is reported the same way everywhere.
/// </summary>
public static class ConverterChecks
{
    /// <summary>Checks that an attribute value has the type the converter reads.</summary>
    /// <param name="actual">The type of the value, as <see cref="DynamoDbReader.ReadStartAttribute"/> returned it.</param>
    /// <param name="expected">The type the converter reads.</param>
    /// <remarks>
    /// A <c>{"NULL":true}</c> is a wrong type like any other here; converters of nullable types test for <see cref="AttributeType.Null"/>
    /// before they call the converter of the underlying type.
    /// </remarks>
    /// <exception cref="JsonException">
    /// <paramref name="actual"/> is not <paramref name="expected"/>. The message names both types and never the value.
    /// </exception>
    public static void Expect(AttributeType actual, AttributeType expected)
    {
        if (actual != expected)
        {
            throw WrongType(actual, expected);
        }
    }

    /// <summary>
    /// Reads the attribute value that holds the discriminator of an item and checks it, for the code the source generator emits. Comparing bytes
    /// costs no string, so a read pays for the name of the other type only when it fails.
    /// </summary>
    /// <param name="reader">The reader, positioned on the attribute name.</param>
    /// <param name="expected">The discriminator of <paramref name="entityType"/>, as UTF-8.</param>
    /// <param name="entityType">The .NET type being read.</param>
    /// <param name="attributeName">The attribute that holds the discriminator.</param>
    /// <exception cref="DynamoDbMappingException">The item has another discriminator. The message names it.</exception>
    /// <exception cref="JsonException">The attribute value is not a string.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ExpectDiscriminator(ref DynamoDbReader reader, ReadOnlySpan<byte> expected, string entityType, string attributeName)
    {
        Expect(reader.ReadStartAttribute(), AttributeType.String);
        if (!reader.GetStringUtf8().SequenceEqual(expected))
        {
            throw DynamoDbMappingException.OtherType(entityType, attributeName, reader.GetString());
        }

        reader.ReadEndAttribute();
    }

    /// <summary>
    /// Finds a string attribute in an item, for the code the source generator emits to restore a property from a key. It is not a check: it fails for an attribute that is absent or of
    /// another type, and the property then counts as missing.
    /// </summary>
    /// <param name="item">A copy of the reader, positioned after the opening brace of the item.</param>
    /// <param name="attributeName">The attribute, as UTF-8.</param>
    /// <param name="text">The string, as UTF-8, or an empty span when this fails.</param>
    /// <returns><see langword="true"/> if the item has a string under that name.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool TryFindString(DynamoDbReader item, ReadOnlySpan<byte> attributeName, out ReadOnlySpan<byte> text)
    {
        while (item.TryReadAttributeName())
        {
            if (item.AttributeNameEquals(attributeName))
            {
                var isString = item.ReadStartAttribute() == AttributeType.String;
                text = isString ? item.GetStringUtf8() : default;
                return isString;
            }

            item.SkipAttributeValue();
        }

        text = default;
        return false;
    }

    private static JsonException WrongType(AttributeType actual, AttributeType expected) =>
        new($"Expected a DynamoDB attribute value of type {expected} but found {actual}.");
}
