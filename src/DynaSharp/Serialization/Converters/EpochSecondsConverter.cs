namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="DateTimeOffset"/> to and from a number value (<c>N</c>) of whole seconds since 1970-01-01T00:00:00Z, the form DynamoDB's
/// time to live (TTL) expects. Use it with <c>[DynamoDbConverter]</c> on the property that is the table's TTL attribute.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB deletes an item some time after the instant in its TTL attribute, usually within days, and only if the attribute is a number. A
/// string, which is what <see cref="DateTimeOffsetConverter"/> writes, is ignored. Seconds are counted in UTC, so the offset is not preserved.
/// </para>
/// <para>
/// Fractions of a second are dropped when writing (the value is rounded down). Reading accepts only whole numbers of seconds from
/// 0001-01-01T00:00:00Z to 9999-12-31T23:59:59Z, the range of <see cref="DateTimeOffset"/>; a number with a fraction, such as epoch milliseconds
/// with a decimal point, is rejected rather than rounded.
/// </para>
/// </remarks>
public readonly struct EpochSecondsConverter : IDynamoDbConverter<DateTimeOffset>
{
    // DateTimeOffset.MinValue and MaxValue (to the second) as Unix time. FromUnixTimeSeconds throws ArgumentOutOfRangeException outside them,
    // which is not a conversion failure, so the range is checked first.
    private const long MinSeconds = -62135596800;
    private const long MaxSeconds = 253402300799;

    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, DateTimeOffset value) => writer.WriteNumber(value.ToUnixTimeSeconds());

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.Number"/>.</exception>
    /// <exception cref="OverflowException">The number is not a whole number of seconds in the range of <see cref="DateTimeOffset"/>. The message does not quote the number.</exception>
    public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.Number);
        var seconds = NumberConverter<long>.ReadNumber(ref reader);
        if (seconds is < MinSeconds or > MaxSeconds)
        {
            throw new OverflowException("The number of seconds is outside the range of DateTimeOffset.");
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
