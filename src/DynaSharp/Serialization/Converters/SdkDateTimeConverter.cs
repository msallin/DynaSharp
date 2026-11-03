namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="DateTime"/> to and from a string value (<c>S</c>) in the form <c>DynamoDBContext</c> of the AWS SDK for .NET stores: the instant in UTC with three
/// fraction digits, <c>2026-10-31T12:00:00.123Z</c>. It is opt-in, with <c>[DynamoDbConverter]</c> on the property or
/// <c>[assembly: DynamoDbDefaultConverter(typeof(DateTime), typeof(SdkDateTimeConverter))]</c>, for a table the SDK wrote, where <see cref="DateTimeConverter"/> would write seven digits,
/// <c>2026-10-31T12:00:00.1230000Z</c>, which an equality condition and a key built from an old value no longer match.
/// </summary>
/// <remarks>
/// <para>
/// The kinds go as in the SDK, version 4, in the conversions V1 and V2 alike. <see cref="DateTimeKind.Utc"/> is written as it is, and <see cref="DateTimeKind.Local"/> is converted to UTC.
/// <see cref="DateTimeKind.Unspecified"/> is taken for local time and converted too, so what it stores depends on the time zone of the machine, where <see cref="DateTimeConverter"/>
/// rejects it. Ticks below the millisecond are cut off, not rounded: <c>12:00:00.1239999</c> is stored as <c>12:00:00.123Z</c>.
/// </para>
/// <para>
/// A value read is always <see cref="DateTimeKind.Utc"/>, which is what the SDK gives under its default <c>RetrieveDateTimeInUtc = true</c>. Code that ran with
/// <c>RetrieveDateTimeInUtc = false</c>, or on version 3 of the SDK, got <see cref="DateTimeKind.Local"/> and calls <see cref="DateTime.ToLocalTime"/> on what this reads.
/// </para>
/// <para>
/// Reading accepts what <see cref="DateTimeConverter"/> accepts: zero to seven fraction digits and <c>Z</c> or an offset, so a property can move from one converter to the other and
/// read both. Text without <c>Z</c> or an offset is rejected, although the SDK reads it as UTC: the SDK never writes it. The width is fixed, so the stored text orders
/// chronologically, and a plain sort key has the ranges.
/// </para>
/// </remarks>
public readonly struct SdkDateTimeConverter : IDynamoDbConverter<DateTime>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, DateTime value) => TemporalText.WriteMilliseconds(writer, value.ToUniversalTime());

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset. The message does not quote the text.</exception>
    public static DateTime Read(ref DynamoDbReader reader, AttributeType type) => DateTimeConverter.Read(ref reader, type);
}
