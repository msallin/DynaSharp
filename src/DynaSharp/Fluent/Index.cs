using System.Diagnostics;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A secondary index of a table. An index can be read and not written, and the type says so: it has no <c>Put</c>, <c>Update</c> or <c>Delete</c>, so
/// what DynamoDB would reject at run time does not compile.
/// </summary>
/// <remarks>
/// <para><b>Hold it in a field,</b> as a <see cref="Table{T}"/>. Instances are immutable and thread-safe.</para>
/// <para>
/// <b>Queries take a key builder.</b> An index of an entity is queried by the partition key builder of the index, <c>byStatus.Query(Order.Keys.ByStatusPk.Of(status))</c>, and an index of a
/// table without a schema by the values of its partition key as well, <c>shop.Index("GSI1", "gsi1pk", "gsi1sk").Query("STATUS#open")</c>; they are in <see cref="EntityTableExtensions"/> and <see cref="ItemTableExtensions"/>.
/// </para>
/// <para>
/// <b>Consistency.</b> A global secondary index is always eventually consistent, so a write shows up here a moment after it succeeded; a local secondary index can be read
/// consistently. An index can hold several items with one key, which a page may split at any point, and in a read of several partitions those come as DynamoDB gives them.
/// </para>
/// <para>
/// <b>What the index projects.</b> A read of an entity filters on its discriminator, and an index gives only what it projects. If the index does not project that attribute, every
/// item fails the filter and the query comes back empty, so such an index is read as <see cref="Item"/>. DynamoDB refuses a filter on a key of the index, so
/// a query of an index with the discriminator as a key has no filter: the key condition decides, and a read fails with
/// <see cref="DynamoDbMappingException"/> on an item of another type, which it gets from a partition key of that value, or from an index partition without a
/// condition on a sort key that is the discriminator.
/// </para>
/// </remarks>
/// <typeparam name="T">How items are read.</typeparam>
[DebuggerDisplay("{Name,nq} on {Table.Name,nq}")]
public sealed class Index<T>
    where T : IDynamoDbDocument<T>
{
    internal Index(Table<T> table, string name, string partitionKeyName, string? sortKeyName, bool isGlobal = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(partitionKeyName);
        Table = table;
        Name = name;
        Model = table.Model.ForIndex(name, partitionKeyName, sortKeyName, isGlobal);
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

    /// <summary>Starts a scan of the index, which charges for every item in it. See <see cref="Index{T}"/> for what is different about reading an index.</summary>
    /// <returns>The command.</returns>
    public ScanCommand<T> Scan() => new(Model.BeginScan(), Model);
}
