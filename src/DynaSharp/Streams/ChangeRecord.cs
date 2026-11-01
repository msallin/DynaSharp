namespace DynaSharp.Streams;

/// <summary>
/// One change to an item of a table, as a stream delivers it.
/// </summary>
/// <remarks>
/// Each change appears in the stream exactly once and in the order of the changes to that item, but changes to different items may
/// come in any order, and a reader that restarts from an older position sees records again. Process records idempotently.
/// </remarks>
public sealed record ChangeRecord
{
    /// <summary>
    /// Gets the identifier of the record, unique within the stream (<c>eventID</c> on the wire). DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? EventId { get; init; }

    /// <summary>Gets the kind of change (<c>eventName</c> on the wire).</summary>
    public OperationType EventName { get; init; }

    /// <summary>
    /// Gets the version of the record format, for example <c>1.1</c> (<c>eventVersion</c> on the wire). DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? EventVersion { get; init; }

    /// <summary>
    /// Gets the source of the event: <c>aws:dynamodb</c> (<c>eventSource</c> on the wire). DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? EventSource { get; init; }

    /// <summary>
    /// Gets the name of the region the change was made in (<c>awsRegion</c> on the wire). DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// Gets the change itself (<c>dynamodb</c> on the wire). DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public StreamRecord? DynamoDb { get; init; }

    /// <summary>
    /// Gets who made the change (<c>userIdentity</c> on the wire). Only set for changes DynamoDB made itself: a deletion by Time to
    /// Live has <see cref="RecordIdentity.PrincipalId"/> <c>dynamodb.amazonaws.com</c> and <see cref="RecordIdentity.Type"/> <c>Service</c>.
    /// <see langword="null"/> for changes made by a client.
    /// </summary>
    public RecordIdentity? UserIdentity { get; init; }
}
