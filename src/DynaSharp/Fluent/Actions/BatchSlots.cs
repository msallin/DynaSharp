using System.Globalization;
using System.Text;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The gets of a batch that hand out a slot. DynamoDB answers a batch per table and in no order, with nothing that says which key an item answers, so an item is matched to its
/// get by the values of its key: a string by its text, a number by its value, so that <c>1.50</c> is <c>1.5</c>, and a binary value by its bytes.
/// </summary>
/// <remarks>
/// An item that answers a slot is kept as a copy of its JSON and read when the slot is read, so a type that does not fit fails there, as in a transaction. What a response
/// matched waits until the whole response is read, so a response that fails halfway leaves the slots as they were, as it leaves the items.
/// </remarks>
internal sealed class BatchSlots
{
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<(string Table, string Key), int> _byKey = [];
    private readonly List<(int Slot, Range Json)> _pending = [];

    /// <summary>Reads the item of a slot.</summary>
    /// <typeparam name="T">How the item is read.</typeparam>
    /// <param name="slots">The slots of the batch, or <see langword="null"/> if it handed out none.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The item, or not found if DynamoDB has none under the key.</returns>
    /// <exception cref="ArgumentException">The slot is a default value, or it comes from another batch.</exception>
    /// <exception cref="InvalidOperationException">The batch gave up before DynamoDB answered the key of the slot.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    public static GetResult<T> Read<T>(BatchSlots? slots, BatchSlot<T> slot)
        where T : IDynamoDbDocument<T>
    {
        var index = slot.Index;
        if (slots is null || index >= slots._entries.Count)
        {
            throw new ArgumentException("This slot comes from another batch: this one handed out fewer slots.", nameof(slot));
        }

        var entry = slots._entries[index];
        if (entry.Alias >= 0)
        {
            entry = slots._entries[entry.Alias];
        }

        if (entry.Unprocessed)
        {
            throw new InvalidOperationException("The batch gave up before DynamoDB answered the key of this slot, so the key is among UnprocessedKeys.");
        }

        if (entry.Item is null)
        {
            return new GetResult<T>(default, false, null);
        }

        var reader = new DynamoDbReader(entry.Item);
        reader.ReadStartItem();
        return new GetResult<T>(T.ReadAttributes(ref reader), true, null);
    }

    /// <summary>Adds a get.</summary>
    /// <param name="table">The table of the get.</param>
    /// <param name="keyJson">The key as the request has it: <c>{"pk":{"S":"a"}}</c>.</param>
    /// <param name="action">The index of the get among the actions of the batch.</param>
    /// <returns>The index of the slot.</returns>
    public int Add(TableModel table, ReadOnlySpan<byte> keyJson, int action)
    {
        var reader = new DynamoDbReader(keyJson);
        reader.ReadStartItem();
        var index = _entries.Count;

        // The same key twice reads one item, which the second slot finds through the first.
        var key = (table.Name, KeyOf(reader, table));
        var alias = _byKey.TryAdd(key, index) ? -1 : _byKey[key];
        _entries.Add(new Entry(table, action, alias));
        return index;
    }

    /// <summary>Finds the table of a slot by its name.</summary>
    /// <param name="tableName">The name of the table.</param>
    /// <returns>The table, or <see langword="null"/> if no get with a slot reads it.</returns>
    public TableModel? TableNamed(string tableName)
    {
        foreach (var entry in _entries)
        {
            if (entry.Table.Name == tableName)
            {
                return entry.Table;
            }
        }

        return null;
    }

    /// <summary>Matches an item of a response to its slot, which takes it once the response is read through, see <see cref="Commit"/>.</summary>
    /// <param name="table">The table the item comes from.</param>
    /// <param name="item">A copy of the reader, positioned on the opening brace of the item.</param>
    /// <param name="json">Where the item stands in the response.</param>
    /// <returns><see langword="true"/> if a slot asked for the item.</returns>
    public bool TryMatch(TableModel table, DynamoDbReader item, Range json)
    {
        if (!_byKey.TryGetValue((table.Name, KeyOf(item, table)), out var slot))
        {
            return false;
        }

        _pending.Add((slot, json));
        return true;
    }

    /// <summary>Keeps what a response that was read through matched.</summary>
    /// <param name="response">The response.</param>
    public void Commit(ReadOnlySpan<byte> response)
    {
        foreach (var (slot, json) in _pending)
        {
            _entries[slot] = _entries[slot] with { Item = response[json].ToArray() };
        }

        _pending.Clear();
    }

    /// <summary>Forgets what a response that could not be read matched.</summary>
    public void Discard() => _pending.Clear();

    /// <summary>Marks the gets that a batch that gave up has no answer for: those from <paramref name="unsentFrom"/> on, and those in what DynamoDB left.</summary>
    /// <param name="unsentFrom">The first action that was never accepted.</param>
    /// <param name="carry">What the last response left, in the form of <c>RequestItems</c>, or empty.</param>
    public void MarkUnprocessed(int unsentFrom, ReadOnlySpan<byte> carry)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Action >= unsentFrom)
            {
                _entries[i] = _entries[i] with { Unprocessed = true };
            }
        }

        if (carry.IsEmpty)
        {
            return;
        }

        // {"Shop":{"Keys":[{...}],"ConsistentRead":true},"Other":{...}}
        var reader = new DynamoDbReader(carry);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var table = TableNamed(reader.GetAttributeName());
            reader.ReadStartItem();
            while (reader.TryReadAttributeName())
            {
                if (table is not null && reader.AttributeNameEquals("Keys"u8))
                {
                    MarkKeys(ref reader, table);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }
        }
    }

    // The values of the key attributes of the table, in one form for the request and the response: a type and a canonical value each, the first with its length in front.
    private static string KeyOf(DynamoDbReader item, TableModel table)
    {
        var partition = string.Empty;
        var sort = string.Empty;
        while (item.TryReadAttributeName())
        {
            if (item.Json.ValueTextEquals(table.PartitionKeyName))
            {
                partition = ValueOf(ref item);
            }
            else if (item.Json.ValueTextEquals(table.SortKeyName.AsSpan()))
            {
                sort = ValueOf(ref item);
            }
            else
            {
                item.SkipAttributeValue();
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{partition.Length}:{partition}{sort}");
    }

    private static string ValueOf(ref DynamoDbReader item)
    {
        var type = item.ReadStartAttribute();
        var value = type switch
        {
            AttributeType.Number => "N" + NumberForm(item.Json.ValueSpan),
            AttributeType.Binary => "B" + Convert.ToBase64String(item.GetBinary()),
            _ => "S" + item.GetString(),
        };
        item.ReadEndAttribute();
        return value;
    }

    // A text that is no number stays as it came; DynamoDB rejects it in a key anyway.
    private static string NumberForm(ReadOnlySpan<byte> text)
    {
        using var form = new PooledByteBuffer(text.Length + 8);
        return NumberOrder.TryAppend(text, form) ? Convert.ToHexString(form.WrittenSpan) : Encoding.UTF8.GetString(text);
    }

    private void MarkKeys(ref DynamoDbReader reader, TableModel table)
    {
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            // A key of the table of the command without a slot is in the carry too.
            if (_byKey.TryGetValue((table.Name, KeyOf(reader, table)), out var slot))
            {
                _entries[slot] = _entries[slot] with { Unprocessed = true };
            }

            reader.Json.Skip();
        }
    }

    private readonly record struct Entry(TableModel Table, int Action, int Alias)
    {
        public byte[]? Item { get; init; }

        public bool Unprocessed { get; init; }
    }
}
