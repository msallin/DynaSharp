using System.Globalization;
using System.Numerics;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The stream of a table, from <c>StreamEnabled: true</c> until it is disabled or the table is deleted, with one shard. The table stages each change to an item here, and the
/// request that made the change commits the staged changes at its end, or discards them when it fails: a transaction emits its records together, and one that is cancelled emits none.
/// </summary>
/// <remarks>
/// <para>
/// A change that leaves the item as it was, such as a <c>PutItem</c> of the same item, emits no record, as the Developer Guide says of DynamoDB Streams. A record holds the images
/// its view type asks for, and <c>SizeBytes</c> is the size of its keys and images by the rules of item sizes, which is what the examples of the Developer Guide add up to.
/// </para>
/// <para>
/// Records are kept for 24 hours on the clock of the fake, and a disabled stream, also that of a deleted table, is readable for 24 hours after it closed and gone then. The shard is
/// closed when the stream is: it then has an ending sequence number, and the last page read from it has no next iterator. One shard is all a stream of the fake ever has.
/// </para>
/// </remarks>
internal sealed class FakeStream
{
    /// <summary>How long a record is kept, and how long a closed stream stays readable.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly List<FakeStreamRecord> _records = [];
    private readonly List<(Item? OldImage, Item? NewImage)> _staged = [];

    /// <summary>Initializes a new instance of the <see cref="FakeStream"/> class, enabled and empty.</summary>
    /// <param name="table">The table.</param>
    /// <param name="viewType">What a record holds of the item.</param>
    /// <param name="label">The label, a timestamp such as <c>2026-01-01T00:00:00.000</c>, unique for the table.</param>
    /// <param name="creationDateTime">When it was enabled, on the clock of the fake.</param>
    /// <param name="startingSequenceNumber">The first sequence number of the shard; every record has a larger one.</param>
    /// <param name="shardSuffix">The last part of the shard id, which keeps the ids of the streams of a fake apart.</param>
    public FakeStream(FakeTable table, StreamViewType viewType, string label, DateTimeOffset creationDateTime, BigInteger startingSequenceNumber, int shardSuffix)
    {
        ArgumentNullException.ThrowIfNull(table);
        TableName = table.Name;
        KeySchema = table.KeySchema;
        ViewType = viewType;
        Label = label;
        Arn = table.Arn + "/stream/" + label;
        CreationDateTime = creationDateTime;
        StartingSequenceNumber = startingSequenceNumber;

        // shardId-00000001767225600000-00000001: the time it opened and a number, as the shard ids of DynamoDB read.
        ShardId = string.Create(CultureInfo.InvariantCulture, $"shardId-{creationDateTime.ToUnixTimeMilliseconds():D20}-{shardSuffix:x8}");
    }

    /// <summary>Gets the ARN: the ARN of the table, <c>/stream/</c> and the label.</summary>
    public string Arn { get; }

    /// <summary>Gets the label.</summary>
    public string Label { get; }

    /// <summary>Gets the name of the table.</summary>
    public string TableName { get; }

    /// <summary>Gets the key of the table.</summary>
    public KeySchema KeySchema { get; }

    /// <summary>Gets what a record holds of the item.</summary>
    public StreamViewType ViewType { get; }

    /// <summary>Gets when the stream was enabled.</summary>
    public DateTimeOffset CreationDateTime { get; }

    /// <summary>Gets the id of the one shard.</summary>
    public string ShardId { get; }

    /// <summary>Gets the first sequence number of the shard.</summary>
    public BigInteger StartingSequenceNumber { get; }

    /// <summary>Gets the last sequence number of the shard once it is closed: that of its last record, or the starting one when it has none.</summary>
    public BigInteger? EndingSequenceNumber { get; private set; }

    /// <summary>Gets when the stream was disabled, or <see langword="null"/> while it is enabled.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Gets a value indicating whether the stream records changes.</summary>
    public bool IsEnabled => ClosedAt is null;

    /// <summary>Gets the records of the shard, the oldest first, the trimmed ones too.</summary>
    public IReadOnlyList<FakeStreamRecord> Records => _records;

    /// <summary>Gets the largest sequence number the shard knows: that of its last record, or the starting one.</summary>
    public BigInteger LastSequenceNumber => _records.Count == 0 ? StartingSequenceNumber : _records[^1].SequenceNumber;

    /// <summary>Stages a change of an item, which the request commits at its end. A closed stream takes nothing.</summary>
    /// <param name="oldImage">The item before, or <see langword="null"/> for a new item.</param>
    /// <param name="newImage">The item after, or <see langword="null"/> for a removed one.</param>
    public void Stage(Item? oldImage, Item? newImage)
    {
        if (IsEnabled)
        {
            _staged.Add((oldImage, newImage));
        }
    }

    /// <summary>Turns the staged changes into records, all with the same time, in the order they were staged. A change that leaves the item as it was is no record.</summary>
    /// <param name="nextSequenceNumber">Gives the next sequence number of the fake.</param>
    /// <param name="now">The time of the request.</param>
    /// <param name="byTimeToLive"><see langword="true"/> for the deletes of the sweep of time to live, whose records name the service as the one who deleted.</param>
    public void Commit(Func<BigInteger> nextSequenceNumber, DateTimeOffset now, bool byTimeToLive)
    {
        ArgumentNullException.ThrowIfNull(nextSequenceNumber);
        foreach (var (oldImage, newImage) in _staged)
        {
            if (oldImage is not null && oldImage.Equals(newImage))
            {
                continue;
            }

            var image = newImage ?? oldImage!;
            var keys = KeySchema.ToItem(KeySchema.KeyOf(image));
            var name = oldImage is null ? OperationType.Insert : newImage is null ? OperationType.Remove : OperationType.Modify;
            var newShown = ViewType is StreamViewType.NewImage or StreamViewType.NewAndOldImages ? newImage : null;
            var oldShown = ViewType is StreamViewType.OldImage or StreamViewType.NewAndOldImages ? oldImage : null;
            var size = ItemSize.Of(keys) + (newShown is null ? 0 : ItemSize.Of(newShown)) + (oldShown is null ? 0 : ItemSize.Of(oldShown));
            _records.Add(new FakeStreamRecord(Guid.NewGuid().ToString("N"), name, nextSequenceNumber(), now, keys, newShown, oldShown, size, byTimeToLive));
        }

        _staged.Clear();
    }

    /// <summary>Drops the staged changes of a request that failed.</summary>
    public void Discard() => _staged.Clear();

    /// <summary>Disables the stream and closes its shard. A closed stream stays as it is.</summary>
    /// <param name="now">The time it closes.</param>
    public void Close(DateTimeOffset now)
    {
        if (IsEnabled)
        {
            ClosedAt = now;
            EndingSequenceNumber = LastSequenceNumber;
        }
    }

    /// <summary>Tells whether the stream is gone: closed for 24 hours.</summary>
    /// <param name="now">The time of the request.</param>
    /// <returns><see langword="true"/> if no request finds it any more.</returns>
    public bool IsGone(DateTimeOffset now) => ClosedAt is { } closed && now - closed >= Retention;

    /// <summary>Gets the position of the oldest record that is kept: a record 24 hours old is trimmed.</summary>
    /// <param name="now">The time of the request.</param>
    /// <returns>The index into <see cref="Records"/>, the count when every record is trimmed.</returns>
    public int TrimHorizon(DateTimeOffset now)
    {
        var index = _records.FindIndex(record => now - record.ApproximateCreationDateTime < Retention);
        return index < 0 ? _records.Count : index;
    }
}
