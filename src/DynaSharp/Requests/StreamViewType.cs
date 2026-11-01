namespace DynaSharp.Requests;

/// <summary>
/// What a stream record contains about the item that changed.
/// </summary>
/// <remarks>
/// Chosen when streams are enabled on a table (<see cref="StreamSpecification.StreamViewType"/>) and reported on every stream
/// record. Changing it requires turning the stream off and on again, which starts a new stream.
/// </remarks>
public enum StreamViewType
{
    /// <summary>Not set. In a request it sends nothing; in a response it is a view this library does not know yet, or none was reported.</summary>
    Unspecified = 0,

    /// <summary>The item as it is after the change, DynamoDB name <c>NEW_IMAGE</c>.</summary>
    NewImage,

    /// <summary>The item as it was before the change, DynamoDB name <c>OLD_IMAGE</c>.</summary>
    OldImage,

    /// <summary>Both the item before and after the change, DynamoDB name <c>NEW_AND_OLD_IMAGES</c>.</summary>
    NewAndOldImages,

    /// <summary>Only the key attributes of the item, DynamoDB name <c>KEYS_ONLY</c>.</summary>
    KeysOnly,
}
