using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.Streams;

/// <summary>
/// A change of an item of one entity, read from the images of a stream record: what <see cref="ChangeRecord.As{T}"/> and <c>ReadChangesAsync</c> of
/// <see cref="TableStreamExtensions"/> give.
/// </summary>
/// <remarks>
/// <para>
/// A stream cannot be filtered at its source, so a table that holds several entities streams all of them, and the type is decided here, from the images: an image is a
/// <typeparamref name="T"/> when its discriminator attribute holds the discriminator of the entity, as <see cref="Item.Is{T}"/> tests. For an entity without a discriminator,
/// <c>[Discriminator(OptOut = true)]</c>, nothing tells its items from others, so every image is one.
/// </para>
/// <para>
/// <see cref="Kind"/> is what happened to the <typeparamref name="T"/>, which is not always the kind of the record: an item whose discriminator changes at one key, an
/// <c>ORDER</c> that becomes an <c>INVOICE</c> in one <c>MODIFY</c>, is an order removed and an invoice inserted, so the change of the order has <see cref="OperationType.Remove"/> and that
/// of the invoice <see cref="OperationType.Insert"/>, both with the sequence number of the record. A typed reading gives the removal first.
/// </para>
/// <para>
/// What can be typed depends on the view of the stream. <see cref="StreamViewType.NewAndOldImages"/> types every change. <see cref="StreamViewType.NewImage"/> has no
/// old image, so <see cref="Before"/> is never set and a removal has no image to be typed by. <see cref="StreamViewType.OldImage"/> has no new image, so <see cref="After"/> is never set and
/// an insert is not typed. A <c>MODIFY</c> with only one image stays <see cref="OperationType.Modify"/> with the other side empty, and cannot show a change of the type.
/// <see cref="StreamViewType.KeysOnly"/> has no image, so nothing is typed.
/// </para>
/// </remarks>
/// <typeparam name="T">The entity.</typeparam>
public sealed record Change<T>
    where T : IDynamoDbEntity<T>
{
    /// <summary>
    /// Gets what happened to the <typeparamref name="T"/>: <see cref="OperationType.Insert"/> with <see cref="After"/> alone, <see cref="OperationType.Remove"/> with
    /// <see cref="Before"/> alone, and <see cref="OperationType.Modify"/> with both, or with the one image the view of the stream has. Never <see cref="OperationType.Unknown"/>.
    /// </summary>
    public OperationType Kind { get; init; }

    /// <summary>
    /// Gets the item before the change, read from the old image, or <see langword="null"/> when there is no old image of a <typeparamref name="T"/>; for an entity that is a struct,
    /// its default value then, and <see cref="Kind"/> tells.
    /// </summary>
    public T? Before { get; init; }

    /// <summary>
    /// Gets the item after the change, read from the new image, or <see langword="null"/> when there is no new image of a <typeparamref name="T"/>; for an entity that is a struct,
    /// its default value then, and <see cref="Kind"/> tells.
    /// </summary>
    public T? After { get; init; }

    /// <summary>
    /// Gets the key attributes of the item, as stored, <see cref="StreamRecord.Keys"/> of the record. DynamoDB sends them with every record: <see langword="null"/> only if the
    /// response omits them.
    /// </summary>
    public IReadOnlyDictionary<string, AttributeValue>? Keys { get; init; }

    /// <summary>
    /// Gets the sequence number of the record, <see cref="StreamRecord.SequenceNumber"/>. Both changes of an item whose type changed have the one of their record.
    /// DynamoDB sends it with every record: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? SequenceNumber { get; init; }

    /// <summary>
    /// Gets the record the change was read from, with what the change does not repeat: <see cref="StreamRecord.ApproximateCreationDateTime"/>, and <see cref="ChangeRecord.UserIdentity"/>,
    /// which tells a deletion by time to live.
    /// </summary>
    public required ChangeRecord Record { get; init; }
}
