namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="DateTime"/> to and from a string value (<c>S</c>) holding the instant in UTC in the round-trip form
/// <c>2026-10-31T12:00:00.0000000Z</c>, the form <see cref="DateTimeOffsetConverter"/> writes, so both kinds of properties compare in one order.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="DateTimeKind.Utc"/> value is written as it is and a <see cref="DateTimeKind.Local"/> value is converted to UTC.
/// <see cref="DateTimeKind.Unspecified"/> is rejected because it names no instant, the same rule as in keys. A value read is always
/// <see cref="DateTimeKind.Utc"/>, so a <see cref="DateTimeKind.Local"/> value comes back as the same instant in UTC, not as equal.
/// </para>
/// <para>Reading accepts the same forms as <see cref="DateTimeOffsetConverter"/>.</para>
/// </remarks>
public readonly struct DateTimeConverter : IDynamoDbConverter<DateTime>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The kind of <paramref name="value"/> is <see cref="DateTimeKind.Unspecified"/>.</exception>
    public static void Write(DynamoDbWriter writer, DateTime value)
    {
        if (value.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("The kind of the DateTime is Unspecified, so it names no instant. Use DateTimeKind.Utc or DateTimeKind.Local.", nameof(value));
        }

        TemporalText.WriteInstant(writer, value.ToUniversalTime());
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset. The message does not quote the text.</exception>
    public static DateTime Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return TemporalText.ParseInstant(reader.GetStringUtf8()).UtcDateTime;
    }
}
