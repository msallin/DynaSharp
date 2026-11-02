namespace DynaSharp.Generators;

/// <summary>A timestamp in UTC at a fixed width. Every format but <c>ticks</c> truncates, which is how time buckets are expressed.</summary>
/// <param name="Format">The <c>KeyTimeFormat</c> member, for example <c>Millisecond</c>.</param>
/// <param name="Width">The width in bytes.</param>
/// <param name="IsOffset">Whether the value is a <c>DateTimeOffset</c> rather than a <c>DateTime</c>, which can fail on its kind.</param>
internal sealed record TimestampEncoding(string Format, int Width, bool IsOffset) : KeyEncoding(Width)
{
    public const string LosslessFormat = "Ticks";

    // The width of a minute, the finest format that names a bucket of time rather than a moment.
    private const int WidestBucket = 16;

    public override bool IsLossless => Format == LosslessFormat;

    public override bool CanReject => !IsOffset;

    /// <summary>Gets a value indicating whether the format asks for one key per period, a window or a month, as opposed to a moment in time that it cuts off.</summary>
    public bool IsBucket => Width <= WidestBucket;

    private string FormatCode => $"global::DynaSharp.Keys.KeyTimeFormat.{Format}";

    public override string WriteCall(string key, string value, string delimiters, string placeholder) =>
        IsOffset ? $"{key}.WriteTimestamp({value}, {FormatCode})" : $"{key}.WriteTimestamp({value}, {FormatCode}, {placeholder})";

    public override string TryReadCall(string key, string target, string delimiters) =>
        $"{key}.TryReadTimestamp({FormatCode}, out {(IsOffset ? "global::System.DateTimeOffset" : "global::System.DateTime")} {target})";
}
