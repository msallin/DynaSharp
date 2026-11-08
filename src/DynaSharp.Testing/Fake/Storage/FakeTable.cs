using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A table of the fake: its schema and settings, its items and its indexes. It is the one place items go in and out, so every write checks the keys and the size and updates every index.
/// </summary>
/// <remarks>
/// <para>
/// A table is <c>ACTIVE</c> from the moment it exists, and so is every index. Settings that DynamoDB only reports, such as provisioned throughput, are kept and reported; nothing
/// throttles.
/// </para>
/// <para>
/// The enumerations of <see cref="Range"/> and <see cref="ScanAll"/> run over the stores, lazily, so a page stops reading where it is full. They must be done before the next write, which
/// the gate of the fake ensures: a request reads and writes under it from start to end.
/// </para>
/// </remarks>
internal sealed class FakeTable
{
    /// <summary>The start of every ARN: the region of <see cref="StubClient"/> and the sample account of the AWS documentation, which <see cref="StubResponse"/> uses as well.</summary>
    public const string ArnPrefix = "arn:aws:dynamodb:" + StubClient.RegionName + ":123456789012:table/";

    /// <summary>The start of the ARN of a KMS key of the fake: the region and account of <see cref="ArnPrefix"/>.</summary>
    public const string KmsArnPrefix = "arn:aws:kms:" + StubClient.RegionName + ":123456789012:";

    /// <summary>
    /// The ARN of the AWS managed key <c>alias/aws/dynamodb</c> of the fake's account, which encrypts a table created with <c>SSESpecification</c> enabled and no
    /// <c>KMSMasterKeyId</c>. It is one fixed key, as in DynamoDB, where that key is one per account and region and so the same for every such table.
    /// </summary>
    public const string AwsManagedKeyArn = KmsArnPrefix + "key/8f2b1c4e-6d3a-4e5f-9a7b-0c1d2e3f4a5b";

    private readonly ItemStore _items = new();

    /// <summary>Initializes a new instance of the <see cref="FakeTable"/> class, empty.</summary>
    /// <param name="name">The name.</param>
    /// <param name="keySchema">The key.</param>
    /// <param name="attributeDefinitions">The attribute definitions of the request.</param>
    /// <param name="creationDateTime">When it was created, on the clock of the fake.</param>
    public FakeTable(string name, KeySchema keySchema, IReadOnlyList<AttributeDefinition> attributeDefinitions, DateTimeOffset creationDateTime)
    {
        Name = name;
        KeySchema = keySchema;
        AttributeDefinitions = attributeDefinitions;
        CreationDateTime = creationDateTime;
        TableId = Guid.NewGuid().ToString();
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Gets the ARN.</summary>
    public string Arn => ArnPrefix + Name;

    /// <summary>Gets the id, new per table.</summary>
    public string TableId { get; }

    /// <summary>Gets the key.</summary>
    public KeySchema KeySchema { get; }

    /// <summary>Gets or sets the attribute definitions, which an <c>UpdateTable</c> that adds an index extends.</summary>
    public IReadOnlyList<AttributeDefinition> AttributeDefinitions { get; set; }

    /// <summary>Gets when the table was created.</summary>
    public DateTimeOffset CreationDateTime { get; }

    /// <summary>Gets or sets the billing mode.</summary>
    public BillingMode BillingMode { get; set; } = BillingMode.Provisioned;

    /// <summary>Gets or sets the provisioned read capacity, 0 on demand.</summary>
    public long ReadCapacityUnits { get; set; }

    /// <summary>Gets or sets the provisioned write capacity, 0 on demand.</summary>
    public long WriteCapacityUnits { get; set; }

    /// <summary>
    /// Gets or sets the on-demand maximums, or <see langword="null"/> for none. A member is <see langword="null"/> where there is no maximum, never <c>-1</c>, and the
    /// object is <see langword="null"/> when neither has one.
    /// </summary>
    public OnDemandThroughput? OnDemandThroughput { get; set; }

    /// <summary>Gets or sets the table class, or <see cref="TableClass.Unspecified"/> for the default, which is not reported.</summary>
    public TableClass TableClass { get; set; }

    /// <summary>Gets or sets a value indicating whether the table refuses <c>DeleteTable</c>.</summary>
    public bool DeletionProtectionEnabled { get; set; }

    /// <summary>Gets or sets the ARN of the KMS key that encrypts the table, or <see langword="null"/> for the key that DynamoDB owns, which the description does not report.</summary>
    public string? KmsMasterKeyArn { get; set; }

    /// <summary>Gets or sets the attribute of time to live, or <see langword="null"/> while it is off.</summary>
    public string? TimeToLiveAttribute { get; set; }

    /// <summary>Gets or sets the stream enabled last, which the description names until the table is gone, or <see langword="null"/> if it never had one. Every change of an item is staged on it while it is enabled.</summary>
    public FakeStream? LatestStream { get; set; }

    /// <summary>Gets the global secondary indexes in the order they were declared or added.</summary>
    public List<FakeIndex> GlobalIndexes { get; } = [];

    /// <summary>Gets the local secondary indexes in the order they were declared.</summary>
    public List<FakeIndex> LocalIndexes { get; } = [];

    /// <summary>Gets the number of items.</summary>
    public long ItemCount => _items.Count;

    /// <summary>Gets the size of the items.</summary>
    public long SizeBytes => All().Sum(ItemSize.Of);

    /// <summary>Finds an index by name, global or local.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The index, or <see langword="null"/>.</returns>
    public FakeIndex? FindIndex(string name) =>
        GlobalIndexes.Find(index => index.Name == name) ?? LocalIndexes.Find(index => index.Name == name);

    /// <summary>Gets an item by its key.</summary>
    /// <param name="key">The key, from <see cref="KeySchema.ReadKey"/>.</param>
    /// <returns>The stored item, which the caller must not change, or <see langword="null"/>.</returns>
    public Item? Get(ItemKey key) => _items.Get(key.Partition, SlotKey.OfTable(key));

    /// <summary>Checks an item as <see cref="Put"/> stores it, without storing it: its values, its key, the keys of the indexes and its size.</summary>
    /// <param name="item">The item.</param>
    /// <param name="ofRequest">
    /// Whether the item is the one a request holds, as of <c>PutItem</c>: then a size past the limit is wrong whatever the table holds, an error of the input that fails a transaction as a whole.
    /// An item an update made is too large only because of the item it started from.
    /// </param>
    /// <param name="byUpdate">Whether the item is the one an update made, whose faults DynamoDB words as the update's.</param>
    /// <returns>The item in its stored form, a copy.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: a value, the key, a key of an index or the size is not what DynamoDB accepts.</exception>
    public Item Check(IReadOnlyDictionary<string, AttributeValue> item, bool ofRequest = false, bool byUpdate = false)
    {
        var stored = AttributeRules.Normalize(item);
        _ = KeySchema.KeyOf(stored);
        foreach (var index in GlobalIndexes.Concat(LocalIndexes))
        {
            index.KeySchema.ThrowIfInvalidIndexKey(stored, index.Name, byUpdate);
        }

        if (ItemSize.Of(stored) > ItemSize.MaxItemBytes)
        {
            var tooLarge = byUpdate ? "Item size to update has exceeded the maximum allowed size" : "Item size has exceeded the maximum allowed size";
            throw ofRequest ? FakeError.InvalidInput(tooLarge) : FakeError.Validation(tooLarge);
        }

        return stored;
    }

    /// <summary>Puts an item, replacing the one with its key.</summary>
    /// <param name="item">The item. It is checked as by <see cref="Check"/> and stored in its stored form, as a copy.</param>
    /// <param name="byUpdate">Whether the item is the one an update made.</param>
    /// <returns>The item it replaced, or <see langword="null"/>.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c>: a value, the key, a key of an index or the size is not what DynamoDB accepts. Nothing changes then.</exception>
    public Item? Put(IReadOnlyDictionary<string, AttributeValue> item, bool byUpdate = false)
    {
        var stored = Check(item, byUpdate: byUpdate);
        var key = KeySchema.KeyOf(stored);
        stored.MakeReadOnly();
        var old = Get(key);
        _items.Set(key.Partition, SlotKey.OfTable(key), stored);
        Reindex(old, stored);
        LatestStream?.Stage(old, stored);
        return old;
    }

    /// <summary>Removes an item.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The item it removed, or <see langword="null"/>.</returns>
    public Item? Remove(ItemKey key)
    {
        var old = Get(key);
        if (old is not null)
        {
            _items.Remove(key.Partition, SlotKey.OfTable(key));
            Reindex(old, null);
            LatestStream?.Stage(old, null);
        }

        return old;
    }

    /// <summary>Enumerates every item of the table in key order: partitions in the order of their keys, then sort keys.</summary>
    /// <returns>The stored items.</returns>
    public IEnumerable<Item> All() => _items.Partitions().SelectMany(static partition => partition.Value.Values);

    /// <summary>Enumerates the items of one partition of the table or an index, as a query reads them.</summary>
    /// <param name="index">The index, or <see langword="null"/> for the table.</param>
    /// <param name="partition">The value of the partition key of the table or the index.</param>
    /// <param name="range">The condition on the sort key, or <see langword="null"/> for all.</param>
    /// <param name="forward"><see langword="false"/> for <c>ScanIndexForward: false</c>.</param>
    /// <param name="exclusiveStart">The <c>ExclusiveStartKey</c>, which the caller checked against the schema: the read starts after it.</param>
    /// <returns>The items as the store holds them: projected for an index.</returns>
    public IEnumerable<Item> Range(FakeIndex? index, AttributeValue partition, SortKeyRange? range, bool forward, Item? exclusiveStart)
    {
        var store = index?.Store ?? _items;
        SlotKey? start = exclusiveStart is null ? null : SlotOf(index, exclusiveStart);
        foreach (var (slot, item) in store.Partition(partition, forward))
        {
            if (start is { } after && IsAtOrBefore(slot, after, forward))
            {
                continue;
            }

            if (range is null || range.Admits(slot.Sort))
            {
                yield return item;
            }
        }
    }

    /// <summary>Enumerates the items of the table or an index, as a scan reads them: partitions in the order of their keys, and with segments, those of one segment.</summary>
    /// <param name="index">The index, or <see langword="null"/> for the table.</param>
    /// <param name="exclusiveStart">The <c>ExclusiveStartKey</c>, which the caller checked against the schema: the scan goes on after it.</param>
    /// <param name="segment">The segment, 0 without segments.</param>
    /// <param name="totalSegments">The number of segments, 1 without segments.</param>
    /// <returns>The items as the store holds them.</returns>
    public IEnumerable<Item> ScanAll(FakeIndex? index, Item? exclusiveStart, int segment, int totalSegments)
    {
        var store = index?.Store ?? _items;
        var keySchema = index?.KeySchema ?? KeySchema;
        var startPartition = exclusiveStart?[keySchema.PartitionKeyName];
        SlotKey? startSlot = exclusiveStart is null ? null : SlotOf(index, exclusiveStart);
        foreach (var (partition, items) in store.Partitions())
        {
            if (Segments.Of(partition, totalSegments) != segment)
            {
                continue;
            }

            var order = startPartition is { } first ? AttributeComparer.Instance.Compare(partition, first) : 1;
            if (order < 0)
            {
                continue;
            }

            foreach (var (slot, item) in items)
            {
                if (order == 0 && IsAtOrBefore(slot, startSlot!.Value, forward: true))
                {
                    continue;
                }

                yield return item;
            }
        }
    }

    private static bool IsAtOrBefore(SlotKey slot, SlotKey start, bool forward)
    {
        var order = ItemStore.SlotComparer.Instance.Compare(slot, start);
        return forward ? order <= 0 : order >= 0;
    }

    // The slot an ExclusiveStartKey stands for. A key that misses an attribute of the slot has been rejected by the read before it gets here.
    private SlotKey SlotOf(FakeIndex? index, Item key) =>
        index is null ? SlotKey.OfTable(KeySchema.KeyOf(key)) : index.Locate(key).Slot;

    private void Reindex(Item? oldImage, Item? newImage)
    {
        foreach (var index in GlobalIndexes.Concat(LocalIndexes))
        {
            index.Reindex(oldImage, newImage);
        }
    }
}
