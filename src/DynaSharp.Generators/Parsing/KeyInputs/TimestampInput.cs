namespace DynaSharp.Generators;

/// <summary><c>DateTimeOffset</c> and <c>DateTime</c>: ticks by default, the one format that a read can restore the property from, and every <c>KeyTimeFormat</c>.</summary>
/// <param name="typeName">The type as messages show it.</param>
/// <param name="isOffset">Whether the type is <c>DateTimeOffset</c>.</param>
internal sealed class TimestampInput(string typeName, bool isOffset) : KeyInputType(typeName, "yyyy, yyyy-MM, yyyy-MM-dd, yyyy-MM-ddTHH, yyyy-MM-ddTHH:mm, s, ms, us or ticks, the default")
{
    private const string Default = "ticks";

    // Widths are the on-disk contract of KeyTimeFormat.
    private static readonly Dictionary<string, (string Member, int Width)> Formats = new()
    {
        ["yyyy"] = ("Year", 4),
        ["yyyy-MM"] = ("Month", 7),
        ["yyyy-MM-dd"] = ("Day", 10),
        ["yyyy-MM-ddTHH"] = ("Hour", 13),
        ["yyyy-MM-ddTHH:mm"] = ("Minute", 16),
        ["s"] = ("Second", 20),
        ["ms"] = ("Millisecond", 24),
        ["us"] = ("Microsecond", 27),
        ["ticks"] = (TimestampEncoding.LosslessFormat, 28),
    };

    /// <inheritdoc/>
    public override IReadOnlyList<string?> Fits { get; } = ["yyyy", "yyyy-MM", "yyyy-MM-dd", "yyyy-MM-ddTHH", "yyyy-MM-ddTHH:mm", "s", "ms", "us", null];

    public override FormatBinding Bind(string? format, string property) =>
        Formats.TryGetValue(format ?? Default, out var known)
            ? Encoded(new TimestampEncoding(known.Member, known.Width, isOffset))
            : DoesNotFit(format!);
}
