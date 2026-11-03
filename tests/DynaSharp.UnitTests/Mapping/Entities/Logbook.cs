using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>
/// Every place a built-in converter can refuse a value of its type on write: a <see cref="DateTime"/> of no kind and a number that is not finite, alone, nullable, in a list, in a set, in a key and in a nested document,
/// and an empty set in a list and in a map.
/// </summary>
[DynamoDbEntity]
[Discriminator("LOGBOOK")]
public sealed partial record Logbook
{
    /// <summary>Gets a time that every write takes.</summary>
    public static DateTime Utc { get; } = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [PartitionKey]
    public required string Id { get; init; }

    [SortKey]
    public DateTime On { get; init; } = Utc;

    [DynamoDbAttribute("logged_at")]
    public DateTime LoggedAt { get; init; } = Utc;

    public DateTime? SignedAt { get; init; }

    public IReadOnlyList<DateTime> Watches { get; init; } = [];

    [DynamoDbConverter(typeof(SdkDateTimeConverter))]
    public DateTime Noted { get; init; } = Utc;

    public double Depth { get; init; }

    public HashSet<double> Soundings { get; init; } = [];

    public LogEntry? Last { get; init; }

    public IReadOnlyList<HashSet<string>> Crews { get; init; } = [];

    public Dictionary<string, HashSet<int>> Shifts { get; init; } = [];
}
