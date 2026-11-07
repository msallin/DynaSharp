using DynaSharp.Mapping;
using DynaSharp.Requests;

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

    /// <summary>
    /// Gets the identifier to tell a record seen again by: <see cref="EventId"/>, or the sequence number of <see cref="DynamoDb"/> when the event id is empty, as DynamoDB Local leaves it.
    /// <see langword="null"/> only if the response omits both.
    /// </summary>
    /// <remarks>
    /// An event id is unique within the stream, a sequence number within its shard, so an identifier from the sequence number is unique only together with the shard,
    /// <see cref="ShardChangeRecord.ShardId"/>.
    /// </remarks>
    public string? Id => string.IsNullOrEmpty(EventId) ? DynamoDb?.SequenceNumber : EventId;

    /// <summary>
    /// Gets approximately when the change was made, <see cref="StreamRecord.ApproximateCreationDateTime"/> of <see cref="DynamoDb"/>, to the second. DynamoDB sends it with every record:
    /// <see langword="null"/> only if the response omits it.
    /// </summary>
    public DateTimeOffset? At => DynamoDb?.ApproximateCreationDateTime;

    /// <summary>
    /// Reads the change as a change of a <typeparamref name="T"/>, from its images: <c>if (record.As&lt;Order&gt;() is { } order) { }</c>. A table of several entities streams
    /// all of them, so a reader asks for each type it handles, <c>As&lt;Customer&gt;()</c> after <c>As&lt;Order&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An image is a <typeparamref name="T"/> when its discriminator attribute holds the discriminator of the entity, as <see cref="Item.Is{T}"/> tests; an entity without a
    /// discriminator, <c>[Discriminator(OptOut = true)]</c>, takes every image. An image that is one is read as <see cref="Item.To{T}"/> reads it.
    /// </para>
    /// <para>
    /// An item whose type changed at its key, an old image of another type than the new one, is a removal of the old type and an insert of the new one:
    /// <c>As&lt;Order&gt;()</c> gives <see cref="OperationType.Remove"/> with <see cref="Change{T}.Before"/>, and <c>As&lt;Invoice&gt;()</c> <see cref="OperationType.Insert"/>
    /// with <see cref="Change{T}.After"/>. Which images there are depends on the view of the stream, see <see cref="Change{T}"/>: under <see cref="StreamViewType.KeysOnly"/>
    /// the result is always <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity.</typeparam>
    /// <returns>The change, or <see langword="null"/> when neither image is a <typeparamref name="T"/>, or the record has no <see cref="DynamoDb"/>.</returns>
    /// <exception cref="DynamoDbMappingException">
    /// An image that is a <typeparamref name="T"/> cannot be read as one, with <see cref="DynamoDbMappingException.Key"/> the keys of the record. An item of the
    /// entity that does not fit it is a bug of the model or of a writer, as on a read of the table.
    /// </exception>
    public Change<T>? As<T>()
        where T : IDynamoDbEntity<T>
    {
        // Only the callback of a reading reads the shard, and there is none here.
        return ChangeTyping.Split<SingleChange<T>, Change<T>>(new ShardChangeRecord(string.Empty, this), null, out var change, out _) == 0 ? null : change;
    }
}
