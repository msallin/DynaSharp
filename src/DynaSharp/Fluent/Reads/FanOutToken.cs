using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The composite position of a fan-out: one entry per partition, a <see cref="ContinuationToken"/> on the outside. It holds an attribute of a list, so that the text of a token is
/// what <see cref="ContinuationToken.Parse"/> takes for all tokens, one map of attribute values, and comes through it the same way, with the version of the format in front. A change of the layout below
/// is a new version there. No message here quotes a token, because it comes from a client.
/// </summary>
/// <remarks>
/// <code>
/// {"fanOut":{"L":[{"L":[{"M":{"pk":{"S":"a"},"sk":{"S":"b"}}},{"M":{"pk":{"S":"a"},"sk":{"S":"c"}}}]},{"L":[{"M":{}},{"M":{"pk":{"S":"x"},"sk":{"S":"m"}}}]},{"L":[{"M":{"pk":{"S":"y"},"sk":{"S":"n"}}},{"M":{}}]},{"S":"z"}]},"descending":{"BOOL":true}}
/// </code>
/// <para>
/// An entry is a start key and the key of the item that comes next, either of which can be an empty map. So the first partition continues after <c>b</c> and has <c>c</c> next, the second starts
/// from the beginning and has <c>m</c> first, the third continues after <c>n</c> and does not know what comes next, and the fourth, <c>z</c>, is done: a partition that is done is its partition key, and <c>{"NULL":true}</c>,
/// which the release before wrote, is read as done too. The second key is a hint for the merge: it goes
/// into no request, and an entry with an empty one is what a token of a page that was given out whole holds, so a token without hints is a token. <c>descending</c> is there only for
/// a read in that direction. A key means another position in the other direction, and the merge would go on with no error, so a token is held to the direction it came from.
/// </para>
/// <para>
/// A token comes from a client, so both keys are held to the attributes of the read, and the partition key in them and of a partition that is done to the partition at that place
/// of the query: a token of another list of partitions of the same length would otherwise read one of them from the position of another, or not at all. What goes on is not passed on as it is: it is read into values and those are written anew,
/// which leaves nothing in the request that was not a value of the key. A hint that lies can only reorder what that client reads, which it could as well do by hand. All this is bad
/// input that fails like a token <see cref="ContinuationToken.Parse"/> refuses, with a <see cref="FormatException"/>, so that a web handler has one thing to catch before a request goes out.
/// </para>
/// </remarks>
internal static class FanOutToken
{
    private const string Member = "fanOut";
    private const string DescendingMember = "descending";

    private const string NotOfThisRead = "This position of a fan-out query does not fit this table or index: an entry does not have the attributes of its key.";
    private const string OtherPartitions = "This position of a fan-out query names another partition than the query has at its place, so it came from another list of partitions. A position goes with the same partitions in the same order.";

    /// <summary>Writes a token.</summary>
    /// <typeparam name="T">How the items are read.</typeparam>
    /// <param name="cursors">The partitions, with <see langword="null"/> for one that was done when the read started.</param>
    /// <param name="descending">Whether the read goes down the sort key.</param>
    /// <param name="template">The request every partition is a copy of, for the signature.</param>
    /// <param name="plan">The plan of the read, for the signature.</param>
    /// <returns>The token, signed if the client signs.</returns>
    public static ContinuationToken Write<T>(FanOutCursor<T>?[] cursors, bool descending, CommandState template, FanOutPlan plan)
        where T : IDynamoDbDocument<T>
    {
        using var buffer = new PooledByteBuffer();
        buffer.Append("{\"fanOut\":{\"L\":["u8);
        for (var i = 0; i < cursors.Length; i++)
        {
            if (i > 0)
            {
                buffer.Append((byte)',');
            }

            if (cursors[i] is { IsDone: false } cursor)
            {
                buffer.Append("{\"L\":["u8);
                AppendKey(buffer, cursor.ResumeKey);
                buffer.Append((byte)',');
                AppendKey(buffer, cursor.HeadKey);
                buffer.Append("]}"u8);
            }
            else
            {
                // The partition key, so that a token of another list of partitions does not end a partition of this one.
                using var json = WireJson.CreateWriter(buffer);
                AttributeValueConverter.Write(new DynamoDbWriter(json), plan.PartitionKeys[i]);
                json.Flush();
            }
        }

        buffer.Append(descending ? "]},\"descending\":{\"BOOL\":true}}"u8 : "]}}"u8);
        return ContinuationToken.FromRawKey(buffer.WrittenSpan, template, plan);
    }

    /// <summary>Reads the entries of a token.</summary>
    /// <param name="after">The token, or the default value for the start of the read.</param>
    /// <param name="partitions">The number of partitions of the query.</param>
    /// <param name="descending">Whether the query goes down the sort key.</param>
    /// <returns>One entry per partition, or <see langword="null"/> for the start of the read.</returns>
    /// <exception cref="FormatException"><paramref name="after"/> is no position of a fan-out, or of one with another number of partitions or the other direction.</exception>
    public static IReadOnlyList<AttributeValue>? Read(ContinuationToken after, int partitions, bool descending)
    {
        if (after.ToKey() is not { } key)
        {
            return null;
        }

        var wentDown = key.TryGetValue(DescendingMember, out var direction);
        if (key.Count != (wentDown ? 2 : 1) || (wentDown && !IsTrue(direction)) || !key.TryGetValue(Member, out var value) || value.Type != AttributeType.List)
        {
            throw new FormatException("This is no position of a fan-out query: it came from another read, or it was altered on the way.");
        }

        if (wentDown != descending)
        {
            throw new FormatException("This position comes from a fan-out query in the other direction. A position goes with the direction of the read it came from.");
        }

        var entries = value.AsList();
        if (entries.Count != partitions)
        {
            throw new FormatException($"This position holds {entries.Count} partitions and the query has {partitions}, so it came from another list of partitions.");
        }

        return entries;
    }

    /// <summary>Sets a partition to the start that its entry says.</summary>
    /// <typeparam name="T">How the items are read.</typeparam>
    /// <param name="cursor">The partition, at its start.</param>
    /// <param name="entry">The entry, or the default value at the start of the read.</param>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="position">The place of the partition in the query.</param>
    /// <exception cref="FormatException">The entry does not hold two keys of the attributes of this read and of this partition.</exception>
    public static void Start<T>(FanOutCursor<T> cursor, AttributeValue entry, FanOutPlan plan, int position)
        where T : IDynamoDbDocument<T>
    {
        if (entry.Type == AttributeType.None)
        {
            return;
        }

        if (entry.Type != AttributeType.List || entry.AsList() is not [{ Type: AttributeType.Map } start, { Type: AttributeType.Map } head])
        {
            throw new FormatException("This position of a fan-out query was altered on the way: an entry is neither two keys nor the end of its partition.");
        }

        using var buffer = new PooledByteBuffer(512);
        WriteKey(buffer, start.AsMap(), plan, position);
        var startLength = buffer.WrittenCount;
        WriteKey(buffer, head.AsMap(), plan, position);
        var keys = buffer.WrittenSpan;
        if (!keys.IsEmpty && cursor.StartAfter(keys[..startLength], keys[startLength..]) is not null)
        {
            throw new FormatException(NotOfThisRead);
        }
    }

    /// <summary>Tells whether an entry says that its partition is done.</summary>
    /// <param name="entry">The entry, or the default value at the start of the read.</param>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="position">The place of the partition in the query.</param>
    /// <returns><see langword="true"/> for the partition key of the partition at <paramref name="position"/>, or the null of the release before; <see langword="false"/> for any other entry, which <see cref="Start"/> checks.</returns>
    /// <exception cref="FormatException">The entry is the partition key of another partition.</exception>
    public static bool IsDone(AttributeValue entry, FanOutPlan plan, int position)
    {
        if (entry.Type is not (AttributeType.String or AttributeType.Number or AttributeType.Binary))
        {
            return entry.Type == AttributeType.Null;
        }

        return entry == plan.PartitionKeys[position] ? true : throw new FormatException(OtherPartitions);
    }

    private static bool IsTrue(AttributeValue value) => value.Type == AttributeType.Boolean && value.AsBool();

    private static void AppendKey(PooledByteBuffer buffer, ReadOnlySpan<byte> key)
    {
        buffer.Append("{\"M\":"u8);
        buffer.Append(key.IsEmpty ? "{}"u8 : key);
        buffer.Append((byte)'}');
    }

    // Writes what a client sent anew, or nothing for an empty map. The rest of the check is what a key of a response goes through as well, in FanOutCursor.StartAfter.
    private static void WriteKey(PooledByteBuffer buffer, IReadOnlyDictionary<string, AttributeValue> key, FanOutPlan plan, int position)
    {
        if (key.Count == 0)
        {
            return;
        }

        if (key.Count != plan.KeyNames.Length || !key.TryGetValue(plan.KeyNames[0], out var partition))
        {
            throw new FormatException(NotOfThisRead);
        }

        if (partition != plan.PartitionKeys[position])
        {
            throw new FormatException(OtherPartitions);
        }

        using var json = WireJson.CreateWriter(buffer);
        AttributeValueConverter.WriteItem(new DynamoDbWriter(json), key);
        json.Flush();
    }
}
