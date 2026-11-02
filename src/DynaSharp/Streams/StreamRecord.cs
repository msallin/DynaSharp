using DynaSharp.Requests;

namespace DynaSharp.Streams;

/// <summary>
/// The change that a stream <see cref="ChangeRecord"/> describes.
/// </summary>
/// <remarks>
/// Which of <see cref="NewImage"/> and <see cref="OldImage"/> are present depends on <see cref="StreamViewType"/> and on the kind of
/// change: an insert has no old image and a removal has no new image. An image is an <see cref="Item"/>, so <see cref="Item.To{T}"/> reads it as an entity.
/// </remarks>
public sealed record StreamRecord
{
    /// <summary>
    /// Gets approximately when the change was made (<c>ApproximateCreationDateTime</c>; DynamoDB sends seconds since the Unix epoch).
    /// DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public DateTimeOffset? ApproximateCreationDateTime { get; init; }

    /// <summary>
    /// Gets the key attributes of the item. DynamoDB sends them with every record: <see langword="null"/> only if the response omits them.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Keys { get; init; }

    /// <summary>
    /// Gets the item as it is after the change. Non-null only if the stream view includes new images (<see cref="StreamViewType.NewImage"/> or
    /// <see cref="StreamViewType.NewAndOldImages"/>) and the item exists after the change, so <see langword="null"/> for a removal.
    /// </summary>
    public Item? NewImage { get; init; }

    /// <summary>
    /// Gets the item as it was before the change. Non-null only if the stream view includes old images (<see cref="StreamViewType.OldImage"/> or
    /// <see cref="StreamViewType.NewAndOldImages"/>) and the item existed before the change, so <see langword="null"/> for an insert.
    /// </summary>
    public Item? OldImage { get; init; }

    /// <summary>
    /// Gets the sequence number of the record within its shard. Opaque and increasing; pass the one of the last processed record to
    /// <see cref="ShardIteratorType.AfterSequenceNumber"/> to resume. DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? SequenceNumber { get; init; }

    /// <summary>
    /// Gets the size of the record in bytes, including the keys, the images and the metadata. DynamoDB sends it with every record: <see langword="null"/> only if
    /// the response omits it.
    /// </summary>
    public long? SizeBytes { get; init; }

    /// <summary>Gets what the stream records contain.</summary>
    public StreamViewType StreamViewType { get; init; }
}
