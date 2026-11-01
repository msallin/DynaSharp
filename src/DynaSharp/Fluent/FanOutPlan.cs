using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What a fan-out query is about, apart from its request: the partitions, in the order that breaks ties, and the attributes that make the key of an item. A read of an
/// index needs both keys: <c>ExclusiveStartKey</c> of an index holds the key of the index and the key of the table.
/// </summary>
internal sealed class FanOutPlan
{
    private const int DefaultMaxConcurrency = 8;

    private FanOutPlan(AttributeValue[] partitionKeys, string[] keyNames, int sortKeyIndex)
    {
        PartitionKeys = partitionKeys;
        KeyNames = keyNames;
        KeyNamesUtf8 = new byte[keyNames.Length][];
        for (var i = 0; i < keyNames.Length; i++)
        {
            KeyNamesUtf8[i] = Encoding.UTF8.GetBytes(keyNames[i]);
        }

        SortKeyIndex = sortKeyIndex;
    }

    /// <summary>Gets the partitions, in their order.</summary>
    public AttributeValue[] PartitionKeys { get; }

    /// <summary>Gets the names of the attributes that make the key of an item: the partition key that is read, its sort key if it has one, and the rest of the key of the table.</summary>
    public string[] KeyNames { get; }

    /// <summary>Gets <see cref="KeyNames"/> as UTF-8.</summary>
    public byte[][] KeyNamesUtf8 { get; }

    /// <summary>Gets where the sort key is in <see cref="KeyNames"/>, or a negative number if what is read has none, which makes all items equal for the merge.</summary>
    public int SortKeyIndex { get; }

    /// <summary>Gets or sets how many requests may be in flight at once.</summary>
    public int MaxConcurrency { get; set; } = DefaultMaxConcurrency;

    /// <summary>Takes the partitions of a fan-out, and finds what is wrong with them at the call that passed them.</summary>
    /// <param name="partitionKeys">The partitions, which the plan keeps.</param>
    /// <param name="read">The table or index that is read.</param>
    /// <param name="table">The table of it.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">A partition key is empty, or occurs twice.</exception>
    public static FanOutPlan Create(AttributeValue[] partitionKeys, TableModel read, TableModel table)
    {
        var positions = new Dictionary<string, int>(partitionKeys.Length, StringComparer.Ordinal);
        for (var position = 0; position < partitionKeys.Length; position++)
        {
            var key = partitionKeys[position];
            if (TableModel.IsEmptyKey(key))
            {
                throw new ArgumentException($"The partition key at position {position} is empty.", nameof(partitionKeys));
            }

            // Values do not go into the message: a partition key can come from a client.
            if (!positions.TryAdd(Identity(key), position))
            {
                throw new ArgumentException($"The partition key at position {position} repeats the one at position {positions[Identity(key)]}. A partition is read once.", nameof(partitionKeys));
            }
        }

        var names = new List<string>(4) { read.PartitionKeyName };
        var sortKeyIndex = Add(names, read.SortKeyName);
        Add(names, table.PartitionKeyName);
        Add(names, table.SortKeyName);
        return new FanOutPlan(partitionKeys, [.. names], sortKeyIndex);
    }

    // DynamoDB takes 1 and 1.0 for one number and so one partition, which the text does not show, and a key of any other type is left to DynamoDB to reject.
    private static string Identity(AttributeValue key)
    {
        if (key.Type == AttributeType.Number)
        {
            using var form = new PooledByteBuffer(64);
            if (NumberOrder.TryAppend(Encoding.UTF8.GetBytes(key.AsNumber()), form))
            {
                return "N" + Convert.ToBase64String(form.WrittenSpan);
            }
        }

        return key.ToString();
    }

    private static int Add(List<string> names, string? name)
    {
        if (name is null)
        {
            return -1;
        }

        var index = names.IndexOf(name);
        if (index >= 0)
        {
            return index;
        }

        names.Add(name);
        return names.Count - 1;
    }
}
