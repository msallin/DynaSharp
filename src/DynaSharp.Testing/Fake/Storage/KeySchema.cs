using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The key of a table or an index: the name and type of the partition key, and of the sort key if there is one. It finds the key of an item and checks it as DynamoDB does.
/// </summary>
/// <remarks>
/// <see cref="ReadKey"/> is for a request that names an item by its key, <c>Key</c> of <c>GetItem</c>, which must hold the key attributes and nothing else. <see cref="KeyOf"/> is for an item
/// that is written, which holds the key attributes among others. Both check the type, that a string or a binary is not empty, and the sizes: 2048 bytes for a partition key, 1024 for a sort key.
/// <see cref="ThrowIfInvalidIndexKey(IReadOnlyDictionary{string, AttributeValue}, string)"/> checks the same of the key attributes of an index that an item holds. An empty value, of the table key or of an index key, and an index key
/// value too large are <see cref="FakeError.InvalidInput"/>, which a transaction does not turn into a cancellation reason.
/// </remarks>
/// <param name="PartitionKeyName">The attribute of the partition key.</param>
/// <param name="PartitionKeyType">Its type.</param>
/// <param name="SortKeyName">The attribute of the sort key, or <see langword="null"/>.</param>
/// <param name="SortKeyType">Its type, or <see langword="null"/>.</param>
internal sealed record KeySchema(string PartitionKeyName, ScalarAttributeType PartitionKeyType, string? SortKeyName = null, ScalarAttributeType? SortKeyType = null)
{
    /// <summary>The largest partition key value.</summary>
    public const int MaxPartitionKeyBytes = 2048;

    /// <summary>The largest sort key value.</summary>
    public const int MaxSortKeyBytes = 1024;

    private const string NoMatch = "The provided key element does not match the schema";

    /// <summary>Gets the key attributes, the partition key first.</summary>
    public IEnumerable<string> Names => SortKeyName is null ? [PartitionKeyName] : [PartitionKeyName, SortKeyName];

    /// <summary>Gets the type of an attribute of a key schema as a message names it.</summary>
    /// <param name="type">The type.</param>
    /// <returns><c>S</c>, <c>N</c> or <c>B</c>.</returns>
    public static string Descriptor(ScalarAttributeType type) => WireNames.ScalarAttributeTypes.TryGetName(type, out var name) ? name : type.ToString();

    /// <summary>Reads the key of a request that names an item, such as <c>Key</c> of <c>GetItem</c> and <c>DeleteItem</c>.</summary>
    /// <param name="key">The attributes of the request.</param>
    /// <returns>The key, in its stored form.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: an attribute is missing, one more is there, a type is not the declared one, a value is empty or too large.</exception>
    public ItemKey ReadKey(IReadOnlyDictionary<string, AttributeValue> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Count != (SortKeyName is null ? 1 : 2) || !key.ContainsKey(PartitionKeyName) || (SortKeyName is not null && !key.ContainsKey(SortKeyName)))
        {
            throw FakeError.Validation(NoMatch);
        }

        var partition = AttributeRules.Normalize(key[PartitionKeyName]);
        var sort = SortKeyName is null ? (AttributeValue?)null : AttributeRules.Normalize(key[SortKeyName]);
        if (!HasType(partition, PartitionKeyType) || (sort is { } value && !HasType(value, SortKeyType!.Value)))
        {
            throw FakeError.Validation(NoMatch);
        }

        ThrowIfEmptyOrLarge(partition, sort);
        return new ItemKey(partition, sort);
    }

    /// <summary>Finds the key of an item that is written.</summary>
    /// <param name="item">The item, in its stored form.</param>
    /// <returns>The key.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: a key attribute is missing, has another type, is empty or too large.</exception>
    public ItemKey KeyOf(IReadOnlyDictionary<string, AttributeValue> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var partition = KeyAttribute(item, PartitionKeyName, PartitionKeyType);
        var sort = SortKeyName is null ? (AttributeValue?)null : KeyAttribute(item, SortKeyName, SortKeyType!.Value);
        ThrowIfEmptyOrLarge(partition, sort);
        return new ItemKey(partition, sort);
    }

    /// <summary>
    /// Checks the key attributes of an index that an item written to the table holds: each that is there has the declared type, is not empty, and is at most 2048 bytes for the
    /// partition key and 1024 for the sort key.
    /// </summary>
    /// <param name="item">The item, in its stored form.</param>
    /// <param name="indexName">The index, for the message.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>, <c>Size limit exceeded</c> for a value too large.</exception>
    public void ThrowIfInvalidIndexKey(IReadOnlyDictionary<string, AttributeValue> item, string indexName)
    {
        ArgumentNullException.ThrowIfNull(item);
        ThrowIfInvalidIndexKey(item, PartitionKeyName, PartitionKeyType, MaxPartitionKeyBytes, indexName);
        if (SortKeyName is not null)
        {
            ThrowIfInvalidIndexKey(item, SortKeyName, SortKeyType!.Value, MaxSortKeyBytes, indexName);
        }
    }

    /// <summary>Tells whether an item is in an index with this key: it holds every key attribute with the declared type. An index is sparse, so an item without one is not in it.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if the item has the whole key.</returns>
    public bool Covers(IReadOnlyDictionary<string, AttributeValue> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.TryGetValue(PartitionKeyName, out var partition) && HasType(partition, PartitionKeyType)
            && (SortKeyName is null || (item.TryGetValue(SortKeyName, out var sort) && HasType(sort, SortKeyType!.Value)));
    }

    /// <summary>
    /// Tells whether an item goes into an index with this key: it holds every key attribute with the declared type, none empty and none too large. A write of an item that holds a
    /// key attribute and does not fit is rejected, so it is only a backfill that meets one, and it leaves the item out, as the Developer Guide says of index key violations.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> if the item is in the index.</returns>
    public bool Admits(IReadOnlyDictionary<string, AttributeValue> item) =>
        Covers(item) && Fits(item[PartitionKeyName], MaxPartitionKeyBytes) && (SortKeyName is null || Fits(item[SortKeyName], MaxSortKeyBytes));

    /// <summary>Gets the key attributes of an item, as a <c>Key</c> or a <c>LastEvaluatedKey</c> holds them.</summary>
    /// <param name="key">The key.</param>
    /// <returns>A new item with the key attributes.</returns>
    public Item ToItem(ItemKey key)
    {
        var item = new Item(2) { [PartitionKeyName] = key.Partition };
        if (SortKeyName is not null)
        {
            item[SortKeyName] = key.Sort!.Value;
        }

        return item;
    }

    private static bool HasType(AttributeValue value, ScalarAttributeType type) => (value.Type, type) switch
    {
        (AttributeType.String, ScalarAttributeType.String) => true,
        (AttributeType.Number, ScalarAttributeType.Number) => true,
        (AttributeType.Binary, ScalarAttributeType.Binary) => true,
        _ => false,
    };

    private static AttributeValue KeyAttribute(IReadOnlyDictionary<string, AttributeValue> item, string name, ScalarAttributeType type)
    {
        if (!item.TryGetValue(name, out var value))
        {
            throw FakeError.Validation($"One or more parameter values were invalid: Missing the key {name} in the item");
        }

        return HasType(value, type)
            ? value
            : throw FakeError.Validation($"One or more parameter values were invalid: Type mismatch for key {name} expected: {Descriptor(type)} actual: {AttributeRules.Descriptor(value.Type)}");
    }

    private static bool Fits(AttributeValue value, int maxBytes) => !IsEmpty(value) && ItemSize.Of(value) <= maxBytes;

    private static void ThrowIfInvalidIndexKey(IReadOnlyDictionary<string, AttributeValue> item, string name, ScalarAttributeType type, int maxBytes, string indexName)
    {
        if (!item.TryGetValue(name, out var value))
        {
            return;
        }

        if (!HasType(value, type))
        {
            throw FakeError.Validation(
                $"One or more parameter values were invalid: Type mismatch for Index Key {name} Expected: {Descriptor(type)} Actual: {AttributeRules.Descriptor(value.Type)} IndexName: {indexName}");
        }

        if (IsEmpty(value))
        {
            throw FakeError.InvalidInput(
                $"One or more parameter values are not valid. A value specified for a secondary index key is not supported. The AttributeValue for a key attribute cannot contain an empty {Kind(value)} value. IndexName: {indexName}, IndexKey: {name}");
        }

        if (ItemSize.Of(value) > maxBytes)
        {
            throw FakeError.InvalidInput("One or more parameter values were invalid: Size limit exceeded");
        }
    }

    private static bool IsEmpty(AttributeValue value) => value.Type == AttributeType.String ? value.AsString().Length == 0 : value.Type == AttributeType.Binary && value.AsBinary().IsEmpty;

    private static string Kind(AttributeValue value) => value.Type == AttributeType.String ? "string" : "binary";

    private void ThrowIfEmptyOrLarge(AttributeValue partition, AttributeValue? sort)
    {
        if (IsEmpty(partition))
        {
            throw FakeError.InvalidInput($"One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty {Kind(partition)} value. Key: {PartitionKeyName}");
        }

        if (sort is { } sortValue && IsEmpty(sortValue))
        {
            throw FakeError.InvalidInput($"One or more parameter values are not valid. The AttributeValue for a key attribute cannot contain an empty {Kind(sortValue)} value. Key: {SortKeyName}");
        }

        if (ItemSize.Of(partition) > MaxPartitionKeyBytes)
        {
            throw FakeError.Validation($"One or more parameter values were invalid: Size of hashkey has exceeded the maximum size limit of {MaxPartitionKeyBytes} bytes");
        }

        if (sort is { } large && ItemSize.Of(large) > MaxSortKeyBytes)
        {
            throw FakeError.Validation($"One or more parameter values were invalid: Aggregated size of all range keys has exceeded the size limit of {MaxSortKeyBytes} bytes");
        }
    }
}
