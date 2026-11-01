namespace DynaSharp.Requests;

/// <summary>
/// The lifecycle state of a global secondary index. Local secondary indexes have none: they exist from the creation of the table.
/// </summary>
public enum IndexStatus
{
    /// <summary>A state this library does not know yet, or none was reported.</summary>
    Unknown = 0,

    /// <summary>The index is being created. For an index added to an existing table this includes the backfill.</summary>
    Creating,

    /// <summary>The index is being updated.</summary>
    Updating,

    /// <summary>The index is being deleted.</summary>
    Deleting,

    /// <summary>The index is ready for use.</summary>
    Active,
}
