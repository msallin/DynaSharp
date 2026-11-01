namespace DynaSharp.Keys;

/// <summary>
/// The resolution of a timestamp in a key. Every format is fixed width, so ordinal byte order is chronological order.
/// Finer precision than the format is truncated, which is how time buckets are expressed.
/// </summary>
/// <remarks>
/// <see cref="Year"/> to <see cref="Minute"/> are byte prefixes of every finer format. <see cref="Second"/> and finer end
/// in <c>Z</c>, so <see cref="Second"/> is not a prefix of <see cref="Millisecond"/>. Only <see cref="Ticks"/> keeps a
/// <see cref="DateTime"/> exactly; no format keeps the offset of a <see cref="DateTimeOffset"/>.
/// Infrastructure: compatible only within a package version, because generated code comes from the generator of the same package.
/// </remarks>
public enum KeyTimeFormat
{
    /// <summary><c>yyyy</c>, 4 bytes: <c>2026</c>.</summary>
    Year,

    /// <summary><c>yyyy-MM</c>, 7 bytes: <c>2026-10</c>.</summary>
    Month,

    /// <summary><c>yyyy-MM-dd</c>, 10 bytes: <c>2026-10-31</c>.</summary>
    Day,

    /// <summary><c>yyyy-MM-ddTHH</c>, 13 bytes: <c>2026-10-31T12</c>.</summary>
    Hour,

    /// <summary><c>yyyy-MM-ddTHH:mm</c>, 16 bytes: <c>2026-10-31T12:34</c>.</summary>
    Minute,

    /// <summary><c>yyyy-MM-ddTHH:mm:ssZ</c>, 20 bytes: <c>2026-10-31T12:34:56Z</c>.</summary>
    Second,

    /// <summary><c>yyyy-MM-ddTHH:mm:ss.fffZ</c>, 24 bytes: <c>2026-10-31T12:34:56.789Z</c>.</summary>
    Millisecond,

    /// <summary><c>yyyy-MM-ddTHH:mm:ss.ffffffZ</c>, 27 bytes: <c>2026-10-31T12:34:56.789012Z</c>.</summary>
    Microsecond,

    /// <summary><c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>, 28 bytes, seven fraction digits: <c>2026-10-31T12:34:56.7890123Z</c>.</summary>
    Ticks,
}
