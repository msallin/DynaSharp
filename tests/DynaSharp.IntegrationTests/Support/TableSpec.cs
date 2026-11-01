using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// The shape of a test table: a partition key <c>pk</c>, optionally a sort key <c>sk</c>, optionally a global secondary index
/// <c>gsi</c> over <c>gsi_pk</c> and <c>gsi_sk</c> (both strings, all attributes projected), optionally a stream, and any further global indexes.
/// </summary>
/// <param name="PartitionKeyType">The type of <c>pk</c>.</param>
/// <param name="SortKeyType">The type of <c>sk</c>, or <see langword="null"/> for a table without a sort key.</param>
/// <param name="WithGlobalIndex">Adds the index <see cref="Tables.GlobalIndexName"/>.</param>
/// <param name="StreamViewType">Enables a stream with this view type, or <see langword="null"/> for none.</param>
/// <param name="GlobalIndexes">Further global secondary indexes, or <see langword="null"/> for none.</param>
internal sealed record TableSpec(
    KeyAttributeType PartitionKeyType = KeyAttributeType.String,
    KeyAttributeType? SortKeyType = null,
    bool WithGlobalIndex = false,
    StreamViewType? StreamViewType = null,
    IReadOnlyList<GlobalIndexSpec>? GlobalIndexes = null)
{
    /// <summary>Gets a table with a string partition key only.</summary>
    public static TableSpec PartitionOnly { get; } = new();

    /// <summary>Gets a table with a string partition key and a number sort key, and the global secondary index (see <c>Items.PutSequenceAsync</c>).</summary>
    public static TableSpec Sequence { get; } = new(SortKeyType: KeyAttributeType.Number, WithGlobalIndex: true);

    /// <summary>Gets a table with a string partition key and a stream that carries the given images.</summary>
    /// <param name="viewType">What the stream records of each change.</param>
    /// <returns>The shape.</returns>
    public static TableSpec WithStream(StreamViewType viewType) => new(StreamViewType: viewType);
}
