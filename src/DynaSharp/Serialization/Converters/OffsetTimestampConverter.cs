namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="DateTimeOffset"/> to and from a string value (<c>S</c>) that keeps the offset, in the round-trip form <c>2026-11-20T11:00:00.0000000+01:00</c>: always seven
/// fraction digits, always an offset, <c>+00:00</c> for UTC. It is opt-in, with <c>[DynamoDbConverter]</c> on the property or
/// <c>[assembly: DynamoDbDefaultConverter(typeof(DateTimeOffset), typeof(OffsetTimestampConverter))]</c>, since <see cref="DateTimeOffsetConverter"/> stores the instant in UTC.
/// </summary>
/// <remarks>
/// <para>
/// <b>The text does not order chronologically across offsets.</b> <c>2026-11-20T11:00:00.0000000+01:00</c> is 10:00 in UTC and sorts after
/// <c>2026-11-20T10:30:00.0000000+00:00</c>, which is later. A key condition, a <c>BETWEEN</c> or a sort over the attribute is right only among values of one offset, and an equality
/// condition finds the text, so the same instant at another offset does not match. Use <see cref="DateTimeOffsetConverter"/> for an attribute that is compared, and keep the offset
/// beside it if it matters.
/// </para>
/// <para>
/// A value read has the offset of its text. Reading accepts what <see cref="DateTimeOffsetConverter"/> accepts: zero to seven fraction digits and an offset or <c>Z</c>, which reads
/// with offset zero, so values that converter wrote read too. Text without <c>Z</c> or an offset is rejected, since it would name a local time without saying whose.
/// </para>
/// </remarks>
public readonly struct OffsetTimestampConverter : IDynamoDbConverter<DateTimeOffset>
{
    /// <inheritdoc/>
    public static void Write(DynamoDbWriter writer, DateTimeOffset value) => TemporalText.WriteWithOffset(writer, value);

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException"><paramref name="type"/> is not <see cref="AttributeType.String"/>.</exception>
    /// <exception cref="FormatException">The text is not an ISO 8601 date and time with <c>Z</c> or an offset, or outside the range of <see cref="DateTimeOffset"/>. The message does not quote the text.</exception>
    public static DateTimeOffset Read(ref DynamoDbReader reader, AttributeType type)
    {
        ConverterChecks.Expect(type, AttributeType.String);
        return TemporalText.ParseWithOffset(reader.GetStringUtf8());
    }
}
