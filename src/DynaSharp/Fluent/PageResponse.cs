using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>What the pages of <c>Query</c> and <c>Scan</c> have in common, which is all of the response.</summary>
internal static class PageResponse
{
    /// <summary>Reads the response of a read of whole items, see <see cref="Read{T, TItem}"/>.</summary>
    /// <typeparam name="T">How the items are read.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="state">The state of the read.</param>
    /// <param name="keys">Where the keys of the items go, for a fan-out, which merges by them.</param>
    /// <returns>The page.</returns>
    public static PageData<T> Read<T>(ref DynamoDbReader reader, CommandState state, FanOutKeys? keys = null)
        where T : IDynamoDbDocument<T> => Read<T, DocumentItem<T>>(ref reader, state, keys);

    /// <summary>Reads the response. <c>LastEvaluatedKey</c> is only located: parsing it would build a dictionary, only to write it back to the next request.</summary>
    /// <typeparam name="T">What an item gives.</typeparam>
    /// <typeparam name="TItem">How it gives it.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="state">The state of the read.</param>
    /// <param name="keys">Where the keys of the items go, for a fan-out, which merges by them.</param>
    /// <returns>The page.</returns>
    public static PageData<T> Read<T, TItem>(ref DynamoDbReader reader, CommandState state, FanOutKeys? keys = null)
        where TItem : IItemReader<T>
    {
        T[] items = [];
        var count = 0;
        var scannedCount = 0;
        var keyOffset = 0;
        var keyLength = 0;
        ConsumedCapacity? capacity = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Items"u8))
            {
                items = ReadItems<T, TItem>(ref reader, keys, state);
            }
            else if (reader.AttributeNameEquals("Count"u8))
            {
                count = OperationJson.ReadInt32(ref reader);
            }
            else if (reader.AttributeNameEquals("ScannedCount"u8))
            {
                scannedCount = OperationJson.ReadInt32(ref reader);
            }
            else if (reader.AttributeNameEquals("LastEvaluatedKey"u8))
            {
                (keyOffset, keyLength) = LocateKey(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                capacity = OperationJson.ReadConsumedCapacity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        state.ResponseCapacity = capacity;
        return new PageData<T>(items, count, scannedCount, keyOffset, keyLength);
    }

    /// <summary>Sets the tags that the pages of <c>Query</c> and <c>Scan</c> share, the same as the request API has for the same request.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="state">The state of the read.</param>
    public static void SetRequestTags(Activity activity, CommandState state) =>
        ReadTags.Set(activity, state.ConsistentRead, state.Expressions.ResolveProjection(), state.Table.IndexName, state.Limit > 0 ? state.Limit : null, state.Select);

    /// <summary>Sets the tags of a page of <c>Query</c>.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="state">The state of the read.</param>
    public static void SetQueryTags(Activity activity, CommandState state)
    {
        SetRequestTags(activity, state);
        if (state.Descending)
        {
            activity.SetTag(TelemetryNames.ScanForward, false);
        }
    }

    /// <summary>Sets the tags of a page of <c>Scan</c>.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="state">The state of the read.</param>
    public static void SetScanTags(Activity activity, CommandState state)
    {
        SetRequestTags(activity, state);
        if (state.TotalSegments > 0)
        {
            activity.SetTag(TelemetryNames.Segment, state.Segment);
            activity.SetTag(TelemetryNames.TotalSegments, state.TotalSegments);
        }
    }

    private static T[] ReadItems<T, TItem>(ref DynamoDbReader reader, FanOutKeys? keys, CommandState state)
        where TItem : IItemReader<T>
    {
        // The items of a page tend to have the same attributes, and only this path knows the shape of the item before.
        if (typeof(TItem) == typeof(DocumentItem<Item>) && keys is null)
        {
            return (T[])(object)OperationJson.ReadItemArray(ref reader, ArrayPool<Item>.Shared, BuilderPools.Shared);
        }

        OperationJson.ReadStartArray(ref reader);
        var first = reader;
        var items = new ArrayBuilder<T>(ArrayPool<T>.Shared);
        try
        {
            while (reader.TryReadStartItem())
            {
                // The reader goes to the keys by value, so the copy finds them wherever they stand in the item and this one reads the item from its start.
                if (keys?.Add(reader) is { } attribute)
                {
                    throw new DynamoDbMappingException(typeof(T).ToString(), attribute);
                }

                items.Add(TItem.Read(ref reader, state));
            }

            return items.ToArray();
        }
        catch (DynamoDbMappingException failure)
        {
            failure.Key = FailedItem.KeyOf(first, items.Count, state.Table);
            throw;
        }
        finally
        {
            items.Dispose();
        }
    }

    // An empty key means the end of the read, as in the request API, so "no key" is the only end marker.
    private static (int Offset, int Length) LocateKey(ref DynamoDbReader reader)
    {
        reader.ReadStartItem();
        ref var json = ref reader.Json;
        var start = (int)json.TokenStartIndex;
        var next = json;
        next.Read();
        var empty = next.TokenType == JsonTokenType.EndObject;
        json.Skip();
        return empty ? default : (start, (int)json.BytesConsumed - start);
    }
}
