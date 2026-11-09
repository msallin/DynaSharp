using System.Numerics;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The stream of a table, from <c>StreamEnabled: true</c> until it is disabled or the table is deleted, with its shards. The table stages each change to an item here, and the
/// request that made the change commits the staged changes at its end, or discards them when it fails: a transaction emits its records together, and one that is cancelled emits none.
/// </summary>
/// <remarks>
/// <para>
/// A change that leaves the item as it was, such as a <c>PutItem</c> of the same item, emits no record, as the Developer Guide says of DynamoDB Streams. A record holds the images
/// its view type asks for, and <c>SizeBytes</c> is the size of its keys and images by the rules of item sizes, which is what the examples of the Developer Guide add up to.
/// </para>
/// <para>
/// A stream starts with one shard. <see cref="Split"/> closes every open shard and opens one or two children of each, as DynamoDB does when it rolls a shard over or splits it, and
/// a second child can be listed late. The records of an item go to one open shard, chosen by its partition key, so the changes of an item stay in order along the lineage.
/// </para>
/// <para>
/// Records are kept for 24 hours on the clock of the fake, and a closed shard, also every shard of a disabled stream or of a deleted table, is readable for 24 hours after it closed
/// and gone then. A closed shard has an ending sequence number, and the last page read from it has no next iterator.
/// </para>
/// </remarks>
internal sealed class FakeStream
{
    /// <summary>How long a record is kept, and how long a closed shard or stream stays readable.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly List<FakeShard> _shards = [];
    private readonly List<(Item? OldImage, Item? NewImage)> _staged = [];

    // The shards changed since the last commit, which tells the readers waiting on the fake.
    private bool _reshaped;

    /// <summary>Initializes a new instance of the <see cref="FakeStream"/> class, enabled and empty.</summary>
    /// <param name="table">The table.</param>
    /// <param name="viewType">What a record holds of the item.</param>
    /// <param name="label">The label, a timestamp such as <c>2026-01-01T00:00:00.000</c>, unique for the table.</param>
    /// <param name="creationDateTime">When it was enabled, on the clock of the fake.</param>
    /// <param name="startingSequenceNumber">The first sequence number of the first shard; every record has a larger one.</param>
    /// <param name="shardSuffix">The last part of the id of the first shard, which keeps the ids of the shards of a fake apart.</param>
    public FakeStream(FakeTable table, StreamViewType viewType, string label, DateTimeOffset creationDateTime, BigInteger startingSequenceNumber, int shardSuffix)
    {
        ArgumentNullException.ThrowIfNull(table);
        TableName = table.Name;
        KeySchema = table.KeySchema;
        ViewType = viewType;
        Label = label;
        Arn = table.Arn + "/stream/" + label;
        CreationDateTime = creationDateTime;
        _shards.Add(new FakeShard(FakeShard.MakeId(creationDateTime, shardSuffix), null, startingSequenceNumber, creationDateTime));
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

    /// <summary>Gets the shards in the order they opened, which is also the order of their ids; the gone ones too.</summary>
    public IReadOnlyList<FakeShard> Shards => _shards;

    /// <summary>Gets when the stream was disabled, or <see langword="null"/> while it is enabled.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Gets a value indicating whether the stream records changes.</summary>
    public bool IsEnabled => ClosedAt is null;

    /// <summary>Finds a shard by its id.</summary>
    /// <param name="shardId">The id.</param>
    /// <param name="now">The time of the request.</param>
    /// <returns>The shard, or <see langword="null"/> if the stream has none of that id or it is gone.</returns>
    public FakeShard? FindShard(string shardId, DateTimeOffset now) => _shards.Find(shard => shard.Id == shardId && !shard.IsGone(now));

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

    /// <summary>
    /// Turns the staged changes into records, all with the same time, in the order they were staged, each in the open shard of its partition key. A change that leaves the item as
    /// it was is no record.
    /// </summary>
    /// <param name="nextSequenceNumber">Gives the next sequence number of the fake.</param>
    /// <param name="now">The time of the request.</param>
    /// <param name="byTimeToLive"><see langword="true"/> for the deletes of the sweep of time to live, whose records name the service as the one who deleted.</param>
    /// <returns><see langword="true"/> if there is a record now or the shards changed since the last commit, which a reader of the stream may want to know.</returns>
    public bool Commit(Func<BigInteger> nextSequenceNumber, DateTimeOffset now, bool byTimeToLive)
    {
        ArgumentNullException.ThrowIfNull(nextSequenceNumber);
        var changed = _reshaped;
        _reshaped = false;
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
            ShardOf(keys).Add(new FakeStreamRecord(Guid.NewGuid().ToString("N"), name, nextSequenceNumber(), now, keys, newShown, oldShown, size, byTimeToLive));
            changed = true;
        }

        _staged.Clear();
        return changed;
    }

    /// <summary>Drops the staged changes of a request that failed.</summary>
    public void Discard() => _staged.Clear();

    /// <summary>
    /// Closes every open shard and opens its children, which take its records from then on: one child as when DynamoDB rolls a shard over, two as when it splits one. The
    /// stream is enabled.
    /// </summary>
    /// <param name="now">The time of the split.</param>
    /// <param name="children">How many children each shard gets, 1 or 2.</param>
    /// <param name="secondChildListedAfter">How long after the split <c>DescribeStream</c> lists the second child; zero lists it at once.</param>
    /// <param name="nextShardNumber">Gives the number of a new shard id.</param>
    /// <param name="nextSequenceNumber">Gives the next sequence number of the fake.</param>
    public void Split(DateTimeOffset now, int children, TimeSpan secondChildListedAfter, Func<int> nextShardNumber, Func<BigInteger> nextSequenceNumber)
    {
        _reshaped = true;
        foreach (var parent in _shards.FindAll(static shard => shard.IsOpen))
        {
            parent.Close(now);
            for (var child = 0; child < children; child++)
            {
                var listedFrom = child == 0 ? now : now + secondChildListedAfter;
                _shards.Add(new FakeShard(FakeShard.MakeId(now, nextShardNumber()), parent.Id, nextSequenceNumber(), listedFrom));
            }
        }
    }

    /// <summary>Disables the stream and closes its open shards. A closed stream stays as it is.</summary>
    /// <param name="now">The time it closes.</param>
    public void Close(DateTimeOffset now)
    {
        if (IsEnabled)
        {
            _reshaped = true;
            ClosedAt = now;
            foreach (var shard in _shards)
            {
                shard.Close(now);
            }
        }
    }

    /// <summary>Tells whether the stream is gone: closed for 24 hours.</summary>
    /// <param name="now">The time of the request.</param>
    /// <returns><see langword="true"/> if no request finds it any more.</returns>
    public bool IsGone(DateTimeOffset now) => ClosedAt is { } closed && now - closed >= Retention;

    // The open shards share the partition keys by a hash of the key's DynamoDB JSON, which is the same on every run.
    private FakeShard ShardOf(Item keys)
    {
        var open = _shards.FindAll(static shard => shard.IsOpen);
        var hash = 2166136261u;
        foreach (var c in keys[KeySchema.PartitionKeyName].ToString())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return open[(int)(hash % (uint)open.Count)];
    }
}
