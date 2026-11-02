namespace DynaSharp;

/// <summary>
/// Which attributes a <c>Query</c> or <c>Scan</c> returns.
/// </summary>
/// <remarks>
/// <see cref="Unspecified"/> sends nothing, so DynamoDB picks: <see cref="AllAttributes"/> when reading a table,
/// <see cref="AllProjectedAttributes"/> when reading an index, and <see cref="SpecificAttributes"/> when the request has a
/// projection expression. Combining a projection expression with any other value than <see cref="SpecificAttributes"/> is
/// rejected with a <see cref="ValidationException"/>.
/// </remarks>
public enum SelectKind
{
    /// <summary>Send no <c>Select</c> and let DynamoDB decide (see the remarks).</summary>
    Unspecified = 0,

    /// <summary>
    /// Return all attributes of the items. Reading a local secondary index that does not project all attributes fetches each
    /// item from the table, which costs additional read capacity and latency.
    /// </summary>
    AllAttributes,

    /// <summary>
    /// Return all attributes that are projected into the index. Only allowed when reading an index; for an index that projects
    /// everything this equals <see cref="AllAttributes"/>.
    /// </summary>
    AllProjectedAttributes,

    /// <summary>
    /// Return only the attributes named in the projection expression. Same as sending a projection expression without
    /// <c>Select</c>. A global secondary index can only return attributes projected into it; a local secondary index fetches the
    /// others from the table at additional cost.
    /// </summary>
    SpecificAttributes,

    /// <summary>
    /// Return only the number of matching items, no items. The read capacity consumed is the same as for returning them.
    /// </summary>
    Count,
}
