using System.Globalization;
using System.Numerics;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A shard of a <see cref="FakeStream"/>: its records in order, open until the stream closes or the shard splits, and readable for 24 hours after it closed.
/// </summary>
/// <param name="id">The shard id.</param>
/// <param name="parentId">The shard it continues, or <see langword="null"/> for the first shard of a stream.</param>
/// <param name="startingSequenceNumber">The first sequence number of the shard; every record has a larger one.</param>
/// <param name="listedFrom">When <c>DescribeStream</c> lists it from, on the clock of the fake: a child that DynamoDB lists late.</param>
internal sealed class FakeShard(string id, string? parentId, BigInteger startingSequenceNumber, DateTimeOffset listedFrom)
{
    private readonly List<FakeStreamRecord> _records = [];

    /// <summary>Gets the shard id.</summary>
    public string Id { get; } = id;

    /// <summary>Gets the shard it continues, or <see langword="null"/>.</summary>
    public string? ParentId { get; } = parentId;

    /// <summary>Gets the first sequence number of the shard.</summary>
    public BigInteger StartingSequenceNumber { get; } = startingSequenceNumber;

    /// <summary>Gets when <c>DescribeStream</c> lists it from.</summary>
    public DateTimeOffset ListedFrom { get; } = listedFrom;

    /// <summary>Gets the last sequence number of the shard once it is closed: that of its last record, or the starting one when it has none.</summary>
    public BigInteger? EndingSequenceNumber { get; private set; }

    /// <summary>Gets when the shard closed, or <see langword="null"/> while it is open.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Gets a value indicating whether the shard takes records.</summary>
    public bool IsOpen => ClosedAt is null;

    /// <summary>Gets the records of the shard, the oldest first, the trimmed ones too.</summary>
    public IReadOnlyList<FakeStreamRecord> Records => _records;

    /// <summary>Gets the largest sequence number the shard knows: that of its last record, or the starting one.</summary>
    public BigInteger LastSequenceNumber => _records.Count == 0 ? StartingSequenceNumber : _records[^1].SequenceNumber;

    /// <summary>Makes the id of a shard as DynamoDB's read: <c>shardId-00000001767225600000-00000001</c>, the time it opened and a number.</summary>
    /// <param name="openedAt">When it opened.</param>
    /// <param name="number">A number no other shard of the fake has.</param>
    /// <returns>The id, which sorts after the ids of the shards opened before it.</returns>
    public static string MakeId(DateTimeOffset openedAt, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"shardId-{openedAt.ToUnixTimeMilliseconds():D20}-{number:x8}");

    /// <summary>Adds a record.</summary>
    /// <param name="record">The record, with a sequence number larger than every one of the shard.</param>
    public void Add(FakeStreamRecord record) => _records.Add(record);

    /// <summary>Closes the shard. A closed shard stays as it is.</summary>
    /// <param name="now">The time it closes.</param>
    public void Close(DateTimeOffset now)
    {
        if (IsOpen)
        {
            ClosedAt = now;
            EndingSequenceNumber = LastSequenceNumber;
        }
    }

    /// <summary>Tells whether <c>DescribeStream</c> lists the shard: it is not gone and its time to be listed has come.</summary>
    /// <param name="now">The time of the request.</param>
    /// <returns><see langword="true"/> if it is listed.</returns>
    public bool IsListed(DateTimeOffset now) => now >= ListedFrom && !IsGone(now);

    /// <summary>Tells whether the shard is gone: closed for 24 hours, and its records with it.</summary>
    /// <param name="now">The time of the request.</param>
    /// <returns><see langword="true"/> if no request finds it any more.</returns>
    public bool IsGone(DateTimeOffset now) => ClosedAt is { } closed && now - closed >= FakeStream.Retention;

    /// <summary>Gets the position of the oldest record that is kept: a record 24 hours old is trimmed.</summary>
    /// <param name="now">The time of the request.</param>
    /// <returns>The index into <see cref="Records"/>, the count when every record is trimmed.</returns>
    public int TrimHorizon(DateTimeOffset now)
    {
        var index = _records.FindIndex(record => now - record.ApproximateCreationDateTime < FakeStream.Retention);
        return index < 0 ? _records.Count : index;
    }
}
