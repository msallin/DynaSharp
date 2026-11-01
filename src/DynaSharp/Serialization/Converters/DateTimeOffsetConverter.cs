namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="DateTimeOffset"/> to and from a string value (<c>S</c>) holding the instant in UTC in the round-trip form
/// <c>2026-10-31T12:00:00.0000000Z</c>: always seven fraction digits, always <c>Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// The offset is not preserved: <c>12:00+02:00</c> is stored as <c>10:00Z</c> and read back with offset zero. That is what makes stored
/// values comparable as text, so a key condition or a <c>BETWEEN</c> over the attribute orders chronologically. Keep the offset in an
/// attribute of its own if it matters.
/// </para>
/// <para>
/// Reading also accepts the ISO 8601 forms other writers produce: fewer fraction digits (<c>2026-10-31T12:00:00.123Z</c>, as the AWS SDK for
/// .NET writes) and an offset instead of <c>Z</c>; the instant is converted to UTC. Text without <c>Z</c> or an offset is rejected, since it
/// would name a local time without saying whose.
/// </para>
/// </remarks>
public readonly struct DateTimeOffsetConverter : IDynamoDbConverter<DateTimeOffset>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, DateTimeOffset value) => TemporalText.WriteInstant(writer, value.UtcDateTime);

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset. The message does not quote the text.</exception>
    public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return TemporalText.ParseInstant(reader.GetStringUtf8());
    }
}
