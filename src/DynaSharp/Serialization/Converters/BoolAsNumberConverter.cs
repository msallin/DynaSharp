namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="bool"/> to and from a number value (<c>N</c>): <see langword="true"/> as <c>{"N":"1"}</c> and <see langword="false"/> as <c>{"N":"0"}</c>, the form
/// <c>DynamoDBContext</c> of the AWS SDK for .NET stores in its default conversion, V1. It is opt-in, with <c>[DynamoDbConverter]</c> on the property or
/// <c>[assembly: DynamoDbDefaultConverter(typeof(bool), typeof(BoolAsNumberConverter))]</c>, since a <see cref="bool"/> is a boolean value (<c>BOOL</c>) by default.
/// </summary>
/// <remarks>
/// Reading takes zero, in any spelling, as <see langword="false"/> and any other number as <see langword="true"/>, as the SDK does with what DynamoDB returns: <c>{"N":"2"}</c> reads as
/// <see langword="true"/>. A boolean value (<c>BOOL</c>) is rejected like any other type, so a table that holds both needs a converter of its own.
/// </remarks>
public readonly struct BoolAsNumberConverter : IDynamoDbConverter<bool>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, bool value) => writer.WriteNumber(value ? 1 : 0);

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Number"/>.</exception>
    /// <exception cref="OverflowException">The text is not a number, or one beyond the range of <see cref="double"/>, which DynamoDB never returns. The message does not quote the text.</exception>
    public static bool Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Number);

        // A double holds the range of DynamoDB, 38 digits and exponents from -130 to 125, and its test sees -0 and 0.000 as zero too.
        return NumberConverter<double>.ReadNumber(ref reader) != 0;
    }
}
