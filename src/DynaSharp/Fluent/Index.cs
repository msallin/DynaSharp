using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A secondary index of a table. An index can be read and not written, and the type says so: it has no <c>Put</c>, <c>Update</c> or <c>Delete</c>, so
/// what DynamoDB would reject at run time does not compile.
/// </summary>
/// <remarks>Hold it in a field, as a <see cref="Table{T}"/>. Instances are immutable and thread-safe.</remarks>
/// <typeparam name="T">How items are read.</typeparam>
[DebuggerDisplay("{Name,nq} on {Table.Name,nq}")]
public sealed class Index<T>
    where T : IDynamoDbDocument<T>
{
    internal Index(Table<T> table, string name, string partitionKeyName, string? sortKeyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(partitionKeyName);
        Table = table;
        Name = name;
        Model = table.Model.ForIndex(name, partitionKeyName, sortKeyName);
    }

    /// <summary>Gets the table of the index.</summary>
    public Table<T> Table { get; }

    /// <summary>Gets the name of the index.</summary>
    public string Name { get; }

    /// <summary>Gets the attribute name of the partition key of the index.</summary>
    public string PartitionKeyName => Model.PartitionKeyName;

    /// <summary>Gets the attribute name of the sort key of the index, or <see langword="null"/> if it has none.</summary>
    public string? SortKeyName => Model.SortKeyName;

    /// <summary>Gets what a read of the index starts from: the table with the key names of the index.</summary>
    internal TableModel Model { get; }

    /// <summary>
    /// Starts a query of one partition of the index. A global secondary index is always eventually consistent, so a write shows up here a moment after it succeeded;
    /// a local secondary index can be read consistently. An index can hold several items with one key, which a page may split at any point.
    /// </summary>
    /// <remarks>
    /// A read of an entity filters on its discriminator, and an index gives only what it projects. If the index does not project that attribute, every
    /// item fails the filter and the query comes back empty, so such an index is read as <see cref="Item"/>. DynamoDB refuses a filter on a key of the index, so
    /// a query of an index with the discriminator as a key has no filter: the key condition decides, and a read fails with
    /// <see cref="DynamoDbMappingException"/> on an item of another type, which it gets from a partition key of that value, or from an index partition without a
    /// condition on a sort key that is the discriminator.
    /// </remarks>
    /// <param name="partitionKey">The partition key of the index. Strings and binary values must not be empty.</param>
    /// <param name="sortKey">The condition on the sort key of the index, or the default value to read the whole partition.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The key is empty, or there is a condition on a sort key and the index has none.</exception>
    public QueryCommand<T> Query(AttributeValue partitionKey, SortKey sortKey = default) => new(Model.BeginQuery(partitionKey, sortKey), Model);

    /// <summary>
    /// Starts a query of several partitions of the index, merged by the sort key of the index: the read of a sharded index. It costs at least one read per partition, see
    /// <see cref="FanOutQueryCommand{T}"/>. An index can hold several items with one sort key, and within a partition those come as DynamoDB gives them. See
    /// <see cref="Query(AttributeValue, SortKey)"/> for what is different about reading an index.
    /// </summary>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key of the index, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is empty or occurs twice, which the message places by position, or there is a condition on a sort key and the index has none.</exception>
    public FanOutQueryCommand<T> Query(IEnumerable<AttributeValue> partitionKeys, SortKey sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Table.Model);

    /// <summary>Starts a query of several partitions by their string keys, which is <see cref="Query(IEnumerable{AttributeValue}, SortKey)"/> for write shards: <c>orders.Query(["SHARD#0", "SHARD#1"])</c>.</summary>
    /// <param name="partitionKeys">The partitions, read here and not kept. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    public FanOutQueryCommand<T> Query(scoped ReadOnlySpan<string> partitionKeys, SortKey sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Table.Model);

    /// <summary>
    /// Starts a query of several partitions by string keys that are computed, which is <see cref="Query(IEnumerable{AttributeValue}, SortKey)"/> for a list of shards:
    /// <c>orders.Query(Enumerable.Range(0, 8).Select(static shard => Order.Keys.Pk.Format(shard)))</c>.
    /// </summary>
    /// <remarks>This overload yields to every other, so a call binds to it only when no other applies. That takes C# 13: a compiler below it ignores the priority, and a <c>string[]</c> needs <c>AsSpan()</c> there.</remarks>
    /// <param name="partitionKeys">The partitions, enumerated once, here. Their order breaks ties between equal sort keys. With none, the read gives nothing and sends nothing.</param>
    /// <param name="sortKey">The condition on the sort key, the same for all partitions, or the default value to read them whole.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partitionKeys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A partition key is <see langword="null"/>, empty or occurs twice, which the message places by position, or there is a condition on a sort key and there is no sort key.</exception>
    [OverloadResolutionPriority(-1)]
    public FanOutQueryCommand<T> Query(IEnumerable<string> partitionKeys, SortKey sortKey = default) => FanOutQueryCommand<T>.Start(partitionKeys, sortKey, Model, Table.Model);

    /// <summary>Starts a scan of the index, which charges for every item in it. See <see cref="Query(AttributeValue, SortKey)"/> for what is different about reading an index.</summary>
    /// <returns>The command.</returns>
    public ScanCommand<T> Scan() => new(Model.BeginScan(), Model);
}
