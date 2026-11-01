namespace DynaSharp;

/// <summary>
/// Whether a write operation reports the size of the item collection it modified.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="None"/> (the default) sends nothing, which DynamoDB treats as its own <c>NONE</c>.
/// </para>
/// <para>
/// An item collection is the set of all items that share a partition key value, in the table and in its local secondary
/// indexes. Item collections only exist, and are only limited to 10 GB, for tables that have a local secondary index; for other
/// tables DynamoDB returns no metrics whatever this is set to.
/// </para>
/// </remarks>
public enum ReturnItemCollectionMetrics
{
    /// <summary>Do not report item collection metrics. Sends no <c>ReturnItemCollectionMetrics</c>, which DynamoDB treats as <c>NONE</c>.</summary>
    None = 0,

    /// <summary>Report an estimate of the size of the item collection, DynamoDB name <c>SIZE</c>, see <see cref="ItemCollectionMetrics"/>.</summary>
    Size,
}
