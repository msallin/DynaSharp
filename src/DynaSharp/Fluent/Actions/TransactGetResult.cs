using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// What a <see cref="TransactGetCommand"/> returns: one snapshot of all its items, each read on demand with <see cref="Read"/>.
/// </summary>
/// <remarks>
/// An item is parsed when <see cref="Read"/> asks for it, so a type that does not fit fails only where it is read. The result stays valid for as long as it is held, also
/// after the client is disposed, and it can be read from several threads and more than once; each read makes a new item. Holding the result holds the whole
/// response, up to 4 MB, so keep the items, not the result, if they live long.
/// </remarks>
public sealed class TransactGetResult
{
    private readonly byte[] _response;
    private readonly IReadOnlyList<Range> _entries;

    internal TransactGetResult(byte[] response, IReadOnlyList<Range> entries, IReadOnlyList<ConsumedCapacity> consumedCapacity)
    {
        _response = response;
        _entries = entries;
        ConsumedCapacity = consumedCapacity;
    }

    /// <summary>Gets the capacity the transaction consumed, if <c>ReturnCapacity</c> asked for it: an entry per table. Empty otherwise. A read in a transaction costs twice a strongly consistent read.</summary>
    public IReadOnlyList<ConsumedCapacity> ConsumedCapacity { get; }

    /// <summary>Reads the item of one get.</summary>
    /// <typeparam name="T">How the item is read, which the slot knows.</typeparam>
    /// <param name="slot">The slot of the get.</param>
    /// <returns>The item, or <see cref="GetResult{T}.Found"/> <see langword="false"/> if there is none under the key. <see cref="GetResult{T}.ConsumedCapacity"/> stays empty: the capacity is on this result.</returns>
    /// <exception cref="ArgumentException">The slot is a default value, or it comes from another transaction with more gets.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    public GetResult<T> Read<T>(TransactSlot<T> slot)
        where T : IDynamoDbDocument<T>
    {
        var index = slot.Index;
        if (index >= _entries.Count)
        {
            throw new ArgumentException("This slot comes from another transaction: the result holds fewer items.", nameof(slot));
        }

        // {"Item":{"pk":{"S":"a"}}}, or {} for a key without an item. The documentation also has null.
        var reader = new DynamoDbReader(_response.AsSpan(_entries[index]));
        reader.Advance();
        var item = default(T);
        var found = false;
        if (reader.Json.TokenType == JsonTokenType.StartObject)
        {
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("Item"u8))
                {
                    reader.ReadStartItem();
                    item = T.ReadAttributes(ref reader);
                    found = true;
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }
        }

        return new GetResult<T>(item, found, null);
    }
}
