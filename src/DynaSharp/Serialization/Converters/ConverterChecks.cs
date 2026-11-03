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
    /// Turns a value into the attribute value its converter writes, for the key builders the source generator emits for a plain key: the key is whatever the converter of the property stores, so
    /// the builder cannot get its type or its text wrong. It writes and reads the value once, so it costs a string or an array.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <typeparam name="TConverter">The converter.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The attribute value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static AttributeValue ToAttributeValue<T, TConverter>(T value)
        where TConverter : IDynamoDbConverter<T>
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            TConverter.Write(new DynamoDbWriter(json), value);
        }

        var reader = new DynamoDbReader(buffer.WrittenSpan);
        return reader.ReadAttribute<AttributeValue, AttributeValueConverter>();
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

    /// <summary>
    /// Reads a plain key of the table through its converter, for the code the source generator emits when an index key is made of it. The value goes through the wire format, so it cannot
    /// drift from what a response reads.
    /// </summary>
    /// <typeparam name="T">The .NET type of the key.</typeparam>
    /// <typeparam name="TConverter">The converter of the key.</typeparam>
    /// <param name="value">The key.</param>
    /// <returns>The value, or <see langword="null"/> if the converter does not read it, which is a key that DynamoDB will reject.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static object? ReadValue<T, TConverter>(AttributeValue value)
        where TConverter : IDynamoDbConverter<T>
    {
        using var buffer = new PooledByteBuffer(256);
        using (var json = WireJson.CreateWriter(buffer))
        {
            AttributeValueConverter.Write(new DynamoDbWriter(json), value);
        }

        var reader = new DynamoDbReader(buffer.WrittenSpan);
        try
        {
            return reader.ReadAttribute<T, TConverter>();
        }
        catch (Exception exception) when (DynamoDbMappingException.IsConversionFailure(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Finds out whether a write failed on a value that its type allows and DynamoDB or the built-in converter does not: a <see cref="DateTime"/> of <see cref="DateTimeKind.Unspecified"/>,
    /// NaN or an infinity, or an empty set in a list or a map. The code the source generator emits asks it in the filter of the catch around the write of a property that such a converter writes.
    /// </summary>
    /// <param name="exception">What the write threw.</param>
    /// <returns><see langword="true"/> for one of those refusals, and <see langword="false"/> for any other exception, which goes on as it is.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsRefusedValue(ArgumentException exception) => exception is UnspecifiedDateTimeException or NonFiniteNumberException or EmptySetException;

    /// <summary>
    /// Creates the exception for a value that <see cref="IsRefusedValue"/> found, naming the property, its attribute, what was wrong and the fix, and never the value. The exception of the
    /// converter is its inner exception, and its type stays: an <see cref="ArgumentException"/> for a <see cref="DateTime"/> and for an empty set, an <see cref="ArgumentOutOfRangeException"/> for a number.
    /// </summary>
    /// <param name="exception">What the write threw.</param>
    /// <param name="entityType">The type that holds the property.</param>
    /// <param name="propertyName">The property.</param>
    /// <param name="attributeName">Its attribute.</param>
    /// <returns>The exception to throw.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ArgumentException RefusedValue(ArgumentException exception, string entityType, string propertyName, string attributeName)
    {
        var where = $"The property {propertyName} of {entityType}, the attribute \"{attributeName}\", cannot be written";
        return exception switch
        {
            UnspecifiedDateTimeException => new ArgumentException(
                $"{where}: its DateTime has DateTimeKind.Unspecified, so it names no instant. A DateTime that was never set, default(DateTime), has this kind: set it to a UTC time first. Only for times that the AWS SDK wrote, store it with [DynamoDbConverter(typeof(SdkDateTimeConverter))], which takes it for local time as the SDK does.",
                exception),
            EmptySetException => new ArgumentException($"{where}: it holds an empty set in a list or a map, which DynamoDB cannot store. Leave the empty set out, or store its elements as a list.", exception),
            _ => new ArgumentOutOfRangeException($"{where}: it is NaN or an infinity, which DynamoDB cannot store. Give it a finite number.", exception),
        };
    }

    private static JsonException WrongType(AttributeType actual, AttributeType expected) =>
        new($"Expected a DynamoDB attribute value of type {expected} but found {actual}.");
}
