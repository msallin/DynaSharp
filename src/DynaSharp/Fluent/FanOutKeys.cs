using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The keys of what one partition holds of a fan-out: for each item the key as JSON, which is where a token continues after it, and its sort key in the form that compares
/// (<see cref="Sort"/>). They come from the JSON of the response, not from the items: an entity need not hold its key in any readable way. Entry 0 is where the page started.
/// </summary>
/// <remarks>Everything lands in two pooled buffers, per page and not per item, and <see cref="Release"/> gives them back.</remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A cursor owns this object and calls Release however the read ends.")]
internal sealed class FanOutKeys
{
    // A key of two attributes, a hundred times over, which covers the page of a usual limit.
    private const int FirstRentLength = 8192;
    private const int FirstEntryCapacity = 16;

    private readonly FanOutPlan _plan;
    private readonly PooledByteBuffer _keys;
    private readonly PooledByteBuffer _sorts;
    private Entry[] _entries = new Entry[FirstEntryCapacity];

    /// <summary>Initializes a new instance of the <see cref="FanOutKeys"/> class.</summary>
    /// <param name="plan">The plan of the read.</param>
    /// <param name="pool">The pool of the buffers, which are rented when they are first written to.</param>
    public FanOutKeys(FanOutPlan plan, ArrayPool<byte> pool)
    {
        _plan = plan;
        _keys = new PooledByteBuffer(pool, FirstRentLength);
        _sorts = new PooledByteBuffer(pool, FirstRentLength);
    }

    /// <summary>Gets the number of entries, which starts at 1 once <see cref="Start"/> has written the start of the page.</summary>
    public int Count { get; private set; }

    /// <summary>Forgets the page before and starts with where this one starts.</summary>
    /// <param name="startKey">The key as JSON, or nothing at the start of the partition.</param>
    public void Start(ReadOnlySpan<byte> startKey)
    {
        _keys.Truncate(0);
        _sorts.Truncate(0);
        _keys.Append(startKey);
        _entries[0] = new Entry(_keys.WrittenCount, 0);
        Count = 1;
    }

    /// <summary>Adds an entry from a key as JSON: <c>LastEvaluatedKey</c>, or the key of a token.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The attribute that is wrong, or <see langword="null"/>.</returns>
    public string? AddKey(ReadOnlySpan<byte> key)
    {
        var reader = new DynamoDbReader(key);
        reader.ReadStartItem();
        return Add(reader);
    }

    /// <summary>
    /// Adds an entry from the attributes of an item, in any order, skipping what is not part of the key. The value text stands in the response as it stands in a key, so
    /// the key is put together from the tokens, without a detour through objects.
    /// </summary>
    /// <param name="probe">A copy of the reader, positioned after the opening brace of the item. Passing it by value leaves the original in place, for the item itself.</param>
    /// <returns>The attribute that is missing, is no key type or holds no number, or <see langword="null"/>.</returns>
    public string? Add(DynamoDbReader probe)
    {
        var names = _plan.KeyNamesUtf8;
        var complete = (1 << names.Length) - 1;
        var seen = 0;
        _keys.Append((byte)'{');
        while (seen != complete && probe.TryReadAttributeName())
        {
            var index = IndexOf(names, ref probe);
            if (index < 0)
            {
                probe.SkipAttributeValue();
                continue;
            }

            if (seen != 0)
            {
                _keys.Append((byte)',');
            }

            seen |= 1 << index;
            if (!TryAppendAttribute(ref probe, index))
            {
                return _plan.KeyNames[index];
            }
        }

        if (seen != complete)
        {
            return _plan.KeyNames[BitOperations.TrailingZeroCount(~seen)];
        }

        _keys.Append((byte)'}');
        if (Count == _entries.Length)
        {
            Array.Resize(ref _entries, Count * 2);
        }

        _entries[Count++] = new Entry(_keys.WrittenCount, _sorts.WrittenCount);
        return null;
    }

    /// <summary>Gets the key of an entry as JSON.</summary>
    /// <param name="index">The entry.</param>
    /// <returns>The key, which is empty for a page that starts at the start of the partition.</returns>
    public ReadOnlySpan<byte> Key(int index)
    {
        var start = index == 0 ? 0 : _entries[index - 1].KeyEnd;
        return _keys.WrittenSpan[start.._entries[index].KeyEnd];
    }

    /// <summary>Gets the sort key of an entry, in the form that compares by its bytes.</summary>
    /// <param name="index">The entry.</param>
    /// <returns>The bytes, which are empty if what is read has no sort key.</returns>
    public ReadOnlySpan<byte> Sort(int index)
    {
        var start = index == 0 ? 0 : _entries[index - 1].SortEnd;
        return _sorts.WrittenSpan[start.._entries[index].SortEnd];
    }

    /// <summary>Gives the buffers back to the pool.</summary>
    public void Release()
    {
        _keys.Release();
        _sorts.Release();
    }

    private static int IndexOf(byte[][] names, ref DynamoDbReader reader)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (reader.AttributeNameEquals(names[i]))
            {
                return i;
            }
        }

        return -1;
    }

    // "sk":{"S":"ORDER#1"}
    private bool TryAppendAttribute(ref DynamoDbReader reader, int index)
    {
        ref var json = ref reader.Json;
        _keys.Append((byte)'"');
        _keys.Append(json.ValueSpan);
        var type = reader.ReadStartAttribute();
        _keys.Append(type switch
        {
            AttributeType.String => "\":{\"S\":\""u8,
            AttributeType.Number => "\":{\"N\":\""u8,
            AttributeType.Binary => "\":{\"B\":\""u8,
            _ => default,
        });
        if (type is not (AttributeType.String or AttributeType.Number or AttributeType.Binary))
        {
            return false;
        }

        _keys.Append(json.ValueSpan);
        _keys.Append("\"}"u8);
        if (index == _plan.SortKeyIndex && !TryAppendSort(ref reader, type))
        {
            return false;
        }

        reader.ReadEndAttribute();
        return true;
    }

    private bool TryAppendSort(ref DynamoDbReader reader, AttributeType type)
    {
        switch (type)
        {
            case AttributeType.String:
                _sorts.Append(reader.GetStringUtf8());
                return true;
            case AttributeType.Binary:
                _sorts.Append(reader.GetBinary());
                return true;
            default:
                return NumberOrder.TryAppend(reader.GetStringUtf8(), _sorts);
        }
    }

    private readonly record struct Entry(int KeyEnd, int SortEnd);
}
