using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The capacity a read consumes, by the arithmetic of the Developer Guide, and the <c>ConsumedCapacity</c> of its answer. A read is 1 unit per 4 KB of what it reads, rounded up,
/// and at least 1; an eventually consistent read is half of that. <c>GetItem</c> measures the whole item, also with a projection and also for a key without an item. <c>Query</c> and
/// <c>Scan</c> add up the items they read, before the filter, and round once.
/// </summary>
/// <remarks>
/// <para>
/// 6 KB read eventually consistent is 2 units / 2 = 1; 100 bytes strongly consistent is 1; 0 bytes eventually consistent is 0.5.
/// </para>
/// <para>
/// With <c>INDEXES</c>, a read of a table reports its units under <c>Table</c> as well, and a read of an index reports them under the index, with <c>Table</c> at 0, or at what a
/// local index fetched from the table.
/// </para>
/// </remarks>
internal static class ReadCapacity
{
    /// <summary>The bytes one read unit covers.</summary>
    public const int BytesPerUnit = 4 * 1024;

    /// <summary>Gets the units of a read.</summary>
    /// <param name="sizeBytes">The bytes read.</param>
    /// <param name="consistent"><see langword="true"/> for a strongly consistent read.</param>
    /// <returns>The units.</returns>
    public static double Units(long sizeBytes, bool consistent)
    {
        var units = Math.Max(1, (sizeBytes + BytesPerUnit - 1) / BytesPerUnit);
        return consistent ? units : units / 2.0;
    }

    /// <summary>Writes the member <c>ConsumedCapacity</c> of a read, as <c>TOTAL</c> or <c>INDEXES</c> ask for it, or nothing for <c>NONE</c>.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="mode">What the request asked for.</param>
    /// <param name="tableName">The table.</param>
    /// <param name="tableUnits">The units read from the table.</param>
    /// <param name="index">The units read from an index, or <see langword="null"/> for a read of the table.</param>
    public static void Write(Utf8JsonWriter response, ReturnConsumedCapacity mode, string tableName, double tableUnits, IndexUnits? index)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (mode == ReturnConsumedCapacity.None)
        {
            return;
        }

        var capacity = new ConsumedCapacity { TableName = tableName, CapacityUnits = tableUnits + (index?.Units ?? 0) };
        if (mode == ReturnConsumedCapacity.Indexes)
        {
            IReadOnlyDictionary<string, CapacityDetail>? detail = index is { } read
                ? new Dictionary<string, CapacityDetail>(StringComparer.Ordinal) { [read.IndexName] = new CapacityDetail { CapacityUnits = read.Units } }
                : null;
            capacity = capacity with
            {
                Table = new CapacityDetail { CapacityUnits = tableUnits },
                GlobalSecondaryIndexes = index is { IsGlobal: true } ? detail : null,
                LocalSecondaryIndexes = index is { IsGlobal: false } ? detail : null,
            };
        }

        response.WritePropertyName("ConsumedCapacity"u8);
        ResponseJson.WriteConsumedCapacity(response, capacity);
    }
}
