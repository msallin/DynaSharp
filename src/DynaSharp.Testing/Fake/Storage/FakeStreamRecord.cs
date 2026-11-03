using System.Numerics;
using DynaSharp.Streams;

namespace DynaSharp.Testing.Fake;

/// <summary>A record of a <see cref="FakeStream"/>: one change of one item, with the images its view type holds.</summary>
/// <param name="EventId">The id of the event, 32 hex digits, new per record.</param>
/// <param name="EventName">What happened to the item.</param>
/// <param name="SequenceNumber">The position in the shard, larger than that of every record before.</param>
/// <param name="ApproximateCreationDateTime">When the request that made the change ran, on the clock of the fake.</param>
/// <param name="Keys">The key attributes of the item.</param>
/// <param name="NewImage">The item after the change, where the view type holds it.</param>
/// <param name="OldImage">The item before the change, where the view type holds it.</param>
/// <param name="SizeBytes">The size of the keys and the images.</param>
/// <param name="ByTimeToLive"><see langword="true"/> for a delete of the sweep of time to live.</param>
internal sealed record FakeStreamRecord(
    string EventId,
    OperationType EventName,
    BigInteger SequenceNumber,
    DateTimeOffset ApproximateCreationDateTime,
    Item Keys,
    Item? NewImage,
    Item? OldImage,
    long SizeBytes,
    bool ByTimeToLive);
