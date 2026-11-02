using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What the clauses of a read say, apart from its expressions: the options of a <c>GetItem</c>, a <c>Query</c> or a <c>Scan</c>, and how their members look on the wire.
/// </summary>
/// <remarks>
/// This lives in one field of <see cref="CommandState"/> and is reached by reference through <see cref="CommandState.Read"/>, so a clause changes that field. Its default value is a read that says nothing, which
/// is what <see cref="CommandState.Release"/> puts back, and a parallel scan or a fan-out assigns the whole struct to each of its copies, so a new option needs neither a line there nor a line in <c>CopyRead</c>.
/// </remarks>
internal struct ReadOptions
{
    /// <summary>Gets or sets a value indicating whether a read is strongly consistent.</summary>
    public bool ConsistentRead { get; set; }

    /// <summary>Gets or sets the limit of items per page of a read, or zero for none.</summary>
    public int Limit { get; set; }

    /// <summary>Gets or sets a value indicating whether a query reads the sort key downwards.</summary>
    public bool Descending { get; set; }

    /// <summary>Gets or sets what a read returns.</summary>
    public SelectKind Select { get; set; }

    /// <summary>Gets or sets the segment of a scan, which only counts when <see cref="TotalSegments"/> is set.</summary>
    public int Segment { get; set; }

    /// <summary>Gets or sets the number of segments of a scan, or zero for a scan of the whole table at once.</summary>
    public int TotalSegments { get; set; }

    /// <summary>Gets or sets a value indicating whether a read of one attribute has to look at the discriminator of each item, because no filter did.</summary>
    public bool ChecksDiscriminator { get; set; }

    /// <summary>Writes the members of the request that come from these options.</summary>
    /// <param name="body">The request, with at least one member before this.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Select"/> holds a value that is not a member of its enum.</exception>
    public readonly void WriteMembers(PooledByteBuffer body)
    {
        if (ConsistentRead)
        {
            body.Append(",\"ConsistentRead\":true"u8);
        }

        if (Limit > 0)
        {
            body.Append(",\"Limit\":"u8);
            body.AppendNumber(Limit);
        }

        if (Descending)
        {
            body.Append(",\"ScanIndexForward\":false"u8);
        }

        if (TotalSegments > 0)
        {
            body.Append(",\"Segment\":"u8);
            body.AppendNumber(Segment);
            body.Append(",\"TotalSegments\":"u8);
            body.AppendNumber(TotalSegments);
        }

        CommandState.WriteEnumMember(body, ",\"Select\":\""u8, WireNames.Selects.GetNameIfSet(Select));
    }
}
