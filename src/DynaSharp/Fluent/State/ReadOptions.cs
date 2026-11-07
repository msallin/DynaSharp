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

    /// <summary>Gets or sets the most items a read gives in all, across its pages, or zero for no such cap.</summary>
    public int Take { get; set; }

    /// <summary>Gets or sets how many items a read with <see cref="Take"/> has given so far.</summary>
    public int Taken { get; set; }

    /// <summary>
    /// Gets the limit of the next page: <see cref="Limit"/>, and with <see cref="Take"/> no more than the items still to give, so a page never holds more than the read gives out
    /// and its last evaluated key continues after the last item given. It is at least 1 while the read sends pages.
    /// </summary>
    public readonly int PageLimit => Take == 0 || (Limit > 0 && Limit <= Take - Taken) ? Limit : Take - Taken;

    /// <summary>Gets a value indicating whether a read with <see cref="Take"/> has given all it may, so that it sends no further page.</summary>
    public readonly bool IsTaken => Take > 0 && Taken >= Take;

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

    /// <summary>Gets or sets what a read does with an item that its type cannot read: <see langword="null"/> ends the read with the exception, anything else skips the item and gets the exception.</summary>
    public Action<DynamoDbMappingException>? OnMappingFailure { get; set; }

    /// <summary>
    /// Writes the limit of a read with <see cref="Take"/>, which changes from page to page, so it goes after the members that <see cref="WriteMembers"/> writes once for all pages.
    /// </summary>
    /// <param name="body">The request, with at least one member before this.</param>
    public readonly void WriteTakeLimit(PooledByteBuffer body)
    {
        if (Take > 0)
        {
            body.Append(",\"Limit\":"u8);
            body.AppendNumber(PageLimit);
        }
    }

    /// <summary>Writes the members of the request that come from these options, all but the limit of a read with <see cref="Take"/>, see <see cref="WriteTakeLimit"/>.</summary>
    /// <param name="body">The request, with at least one member before this.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Select"/> holds a value that is not a member of its enum.</exception>
    public readonly void WriteMembers(PooledByteBuffer body)
    {
        if (ConsistentRead)
        {
            body.Append(",\"ConsistentRead\":true"u8);
        }

        if (Take == 0 && Limit > 0)
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
