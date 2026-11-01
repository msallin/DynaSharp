namespace DynaSharp;

/// <summary>
/// The lifecycle state of a table.
/// </summary>
public enum TableStatus
{
    /// <summary>A state this library does not know yet. DynamoDB adds states over time.</summary>
    Unknown = 0,

    /// <summary>The table is being created.</summary>
    Creating,

    /// <summary>The table or one of its indexes is being updated.</summary>
    Updating,

    /// <summary>The table is being deleted.</summary>
    Deleting,

    /// <summary>The table is ready for use.</summary>
    Active,

    /// <summary>The KMS key that encrypts the table is inaccessible; the table becomes archived after seven days.</summary>
    InaccessibleEncryptionCredentials,

    /// <summary>The table is being archived.</summary>
    Archiving,

    /// <summary>The table is archived and must be restored before use.</summary>
    Archived,

    /// <summary>A replica of a multi-account global table lacks the authorization it needs, so replication has stopped.</summary>
    ReplicationNotAuthorized,
}
