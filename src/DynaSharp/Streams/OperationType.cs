namespace DynaSharp.Streams;

/// <summary>
/// The kind of change a stream record describes.
/// </summary>
public enum OperationType
{
    /// <summary>A kind this library does not know yet, or none was reported.</summary>
    Unknown = 0,

    /// <summary>An item was created, DynamoDB name <c>INSERT</c>.</summary>
    Insert,

    /// <summary>An item was changed, DynamoDB name <c>MODIFY</c>.</summary>
    Modify,

    /// <summary>An item was deleted, DynamoDB name <c>REMOVE</c>. Deletions by Time to Live are of this kind too; see <see cref="ChangeRecord.UserIdentity"/>.</summary>
    Remove,
}
