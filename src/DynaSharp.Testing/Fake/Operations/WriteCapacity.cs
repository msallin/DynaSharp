using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// <para>The capacity a write consumes, by the arithmetic of the Developer Guide, and the <c>ConsumedCapacity</c> of a write, a batch or a transaction.</para>
/// <para>
/// A write costs 1 unit per KB of the larger of the old and the new image, rounded up, and at least 1, also for a delete of an item that is not there. A secondary index pays on
/// top of that, per the Developer Guide, "Provisioned throughput considerations": an item that comes into the index is one write of its new index entry, one that leaves it a write
/// of the old entry, one whose index key changes both, one that stays with other projected values one write of the larger entry, and one whose entry does not change nothing.
/// The entry is what the index projects of the item.
/// </para>
/// <para>
/// <c>TOTAL</c> reports <c>CapacityUnits</c>, as the reads of <see cref="ReadCapacity"/> do; <c>INDEXES</c> adds the share of the table and of every
/// index that paid: <c>{"TableName":"T","CapacityUnits":2.0,"Table":{"CapacityUnits":1.0},"GlobalSecondaryIndexes":{"ByX":{"CapacityUnits":1.0}}}</c>.
/// </para>
/// <para>
/// A transaction with <c>INDEXES</c> also tells read units from write units, as DynamoDB answers it: <c>ReadCapacityUnits</c> or <c>WriteCapacityUnits</c> beside each
/// <c>CapacityUnits</c>, at the top, of the table and of every index, see <see cref="Split"/>. With <c>TOTAL</c> it stays <c>CapacityUnits</c> alone, which is all that was observed of it.
/// </para>
/// </summary>
internal static class WriteCapacity
{
    private const long WriteUnitBytes = 1024;

    /// <summary>Which units a <c>ConsumedCapacity</c> of <c>INDEXES</c> repeats its <c>CapacityUnits</c> in.</summary>
    public enum Split
    {
        /// <summary>Neither: <c>CapacityUnits</c> alone, as the single writes and the batches answer.</summary>
        None,

        /// <summary><c>ReadCapacityUnits</c>, as <c>TransactGetItems</c> and the replay of a <c>TransactWriteItems</c> answer.</summary>
        Read,

        /// <summary><c>WriteCapacityUnits</c>, as <c>TransactWriteItems</c> answers.</summary>
        Write,
    }

    /// <summary>Gets the units of a write to a table.</summary>
    /// <param name="oldSizeBytes">The size of the old image, 0 for none.</param>
    /// <param name="newSizeBytes">The size of the new image, 0 for a delete.</param>
    /// <returns>The units, at least 1.</returns>
    public static double Units(long oldSizeBytes, long newSizeBytes) => Math.Max(1, (Math.Max(oldSizeBytes, newSizeBytes) + WriteUnitBytes - 1) / WriteUnitBytes);

    /// <summary>Gets the units of a write as <see cref="WriteOutcome"/> describes it.</summary>
    /// <param name="outcome">The write.</param>
    /// <returns>The units, at least 1.</returns>
    public static double Units(WriteOutcome outcome) => Units(SizeOf(outcome.OldImage), SizeOf(outcome.NewImage));

    /// <summary>Gets the units each index of a table pays for a write.</summary>
    /// <param name="table">The table.</param>
    /// <param name="oldImage">The item before, or <see langword="null"/>.</param>
    /// <param name="newImage">The item after, or <see langword="null"/>.</param>
    /// <returns>The units per index that the write touches, global indexes first, each in the order of the table.</returns>
    public static IReadOnlyList<IndexUnits> IndexWriteUnits(FakeTable table, Item? oldImage, Item? newImage)
    {
        ArgumentNullException.ThrowIfNull(table);
        var units = new List<IndexUnits>();
        foreach (var index in table.GlobalIndexes.Concat(table.LocalIndexes))
        {
            var before = oldImage is not null && index.KeySchema.Covers(oldImage) ? index.Project(oldImage) : null;
            var after = newImage is not null && index.KeySchema.Covers(newImage) ? index.Project(newImage) : null;
            var cost = (before, after) switch
            {
                (null, null) => 0,
                (null, { } entry) => Units(0, ItemSize.Of(entry)),
                ({ } entry, null) => Units(ItemSize.Of(entry), 0),
                ({ } old, { } entry) when !SameKey(index.KeySchema, old, entry) => Units(ItemSize.Of(old), 0) + Units(0, ItemSize.Of(entry)),
                ({ } old, { } entry) => old.Equals(entry) ? 0 : Units(ItemSize.Of(old), ItemSize.Of(entry)),
            };

            if (cost > 0)
            {
                units.Add(new IndexUnits(index.Name, index.IsGlobal, cost));
            }
        }

        return units;
    }

    /// <summary>Writes <c>ConsumedCapacity</c> of one table as <c>TOTAL</c> or <c>INDEXES</c> ask for it.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="mode">What the request asked for, not <see cref="ReturnConsumedCapacity.None"/>.</param>
    /// <param name="tableName">The table.</param>
    /// <param name="tableUnits">The units of the table.</param>
    /// <param name="indexes">The units of the indexes.</param>
    public static void Write(Utf8JsonWriter response, ReturnConsumedCapacity mode, string tableName, double tableUnits, IReadOnlyList<IndexUnits> indexes)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.WritePropertyName("ConsumedCapacity"u8);
        ResponseJson.WriteConsumedCapacity(response, Of(mode, new CapacityShare(tableName, tableUnits, indexes), Split.None));
    }

    /// <summary>Writes <c>ConsumedCapacity</c> of a batch or a transaction: an array with one element per table, in the order the tables first come, each the sum of its shares.</summary>
    /// <param name="response">The writer, inside the object of the response.</param>
    /// <param name="mode">What the request asked for, not <see cref="ReturnConsumedCapacity.None"/>.</param>
    /// <param name="shares">The units of each item, with its table.</param>
    /// <param name="split">The units that <c>INDEXES</c> repeats <c>CapacityUnits</c> in; ignored for <c>TOTAL</c>.</param>
    public static void WriteAll(Utf8JsonWriter response, ReturnConsumedCapacity mode, IEnumerable<CapacityShare> shares, Split split = Split.None)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(shares);
        response.WriteStartArray("ConsumedCapacity"u8);
        foreach (var table in shares.GroupBy(static share => share.TableName, StringComparer.Ordinal))
        {
            IReadOnlyList<IndexUnits> indexes =
            [
                .. table.SelectMany(static share => share.Indexes)
                    .GroupBy(static index => (index.IndexName, index.IsGlobal))
                    .Select(static index => new IndexUnits(index.Key.IndexName, index.Key.IsGlobal, index.Sum(static units => units.Units))),
            ];
            ResponseJson.WriteConsumedCapacity(response, Of(mode, new CapacityShare(table.Key, table.Sum(static share => share.TableUnits), indexes), split));
        }

        response.WriteEndArray();
    }

    private static long SizeOf(Item? image) => image is null ? 0 : ItemSize.Of(image);

    private static bool SameKey(KeySchema key, Item old, Item entry) => key.Names.All(name => old[name].Equals(entry[name]));

    private static ConsumedCapacity Of(ReturnConsumedCapacity mode, CapacityShare share, Split split)
    {
        var units = share.TableUnits + share.Indexes.Sum(static index => index.Units);
        var capacity = new ConsumedCapacity { TableName = share.TableName, CapacityUnits = units };
        return mode != ReturnConsumedCapacity.Indexes
            ? capacity
            : capacity with
            {
                ReadCapacityUnits = split == Split.Read ? units : null,
                WriteCapacityUnits = split == Split.Write ? units : null,
                Table = Detail(share.TableUnits, split),
                GlobalSecondaryIndexes = Details(share.Indexes, isGlobal: true, split),
                LocalSecondaryIndexes = Details(share.Indexes, isGlobal: false, split),
            };
    }

    private static CapacityDetail Detail(double units, Split split) =>
        new() { CapacityUnits = units, ReadCapacityUnits = split == Split.Read ? units : null, WriteCapacityUnits = split == Split.Write ? units : null };

    private static Dictionary<string, CapacityDetail>? Details(IReadOnlyList<IndexUnits> indexes, bool isGlobal, Split split)
    {
        var details = indexes.Where(index => index.IsGlobal == isGlobal).ToDictionary(static index => index.IndexName, index => Detail(index.Units, split), StringComparer.Ordinal);
        return details.Count == 0 ? null : details;
    }
}
