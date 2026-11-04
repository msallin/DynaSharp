namespace DynaSharp.Streams;

/// <summary>
/// A record of <see cref="DynamoDbStreamsClient.ReadStreamAsync"/>, with the shard it came from.
/// </summary>
/// <remarks>
/// <para>
/// The shard and the sequence number of the record are the position to keep: <see cref="DynamoDbStreamsClient.ReadShardAsync"/> with this
/// <see cref="ShardId"/>, <see cref="ShardIteratorType.AfterSequenceNumber"/> and <see cref="SequenceNumber"/> reads the shard on after this record.
/// </para>
/// <para>
/// The shard is here and not on <see cref="ChangeRecord"/> because <c>GetRecords</c> does not say it: a record of
/// <see cref="DynamoDbStreamsClient.GetRecordsAsync"/> comes from an iterator, which is opaque, so a property of <see cref="ChangeRecord"/> would be
/// <see langword="null"/> there and set elsewhere, and would change the equality of records that are the same change.
/// </para>
/// <para>
/// Two values are equal when their shard ids are equal, ordinal, and their records are equal. It is a plain struct and not a <c>record struct</c>, whose
/// generated <c>Equals(object)</c> carries no nullable annotation. The <see langword="default"/> value has <see langword="null"/> in both properties and comes
/// from no reading.
/// </para>
/// </remarks>
/// <param name="shardId">The identifier of the shard the record came from.</param>
/// <param name="record">The record, as <see cref="DynamoDbStreamsClient.ReadShardAsync"/> returns it.</param>
public readonly struct ShardChangeRecord(string shardId, ChangeRecord record) : IEquatable<ShardChangeRecord>
{
    /// <summary>Gets the identifier of the shard the record came from.</summary>
    public string ShardId { get; init; } = shardId;

    /// <summary>Gets the record, as <see cref="DynamoDbStreamsClient.ReadShardAsync"/> returns it.</summary>
    public ChangeRecord Record { get; init; } = record;

    /// <summary>
    /// Gets the sequence number of the record, <see cref="StreamRecord.SequenceNumber"/> of <see cref="ChangeRecord.DynamoDb"/>, or <see langword="null"/> if the record has none,
    /// which DynamoDB always sends. It orders the records of one shard, and it is no number to compare across shards.
    /// </summary>
    public string? SequenceNumber => Record?.DynamoDb?.SequenceNumber;

    /// <summary>Tells whether two values hold the same shard and equal records.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public static bool operator ==(ShardChangeRecord left, ShardChangeRecord right) => left.Equals(right);

    /// <summary>Tells whether two values differ in their shard or their record.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if they are not equal.</returns>
    public static bool operator !=(ShardChangeRecord left, ShardChangeRecord right) => !left.Equals(right);

    /// <summary>Takes the value apart, as <c>var (shardId, record) = change</c>.</summary>
    /// <param name="shardId">The identifier of the shard.</param>
    /// <param name="record">The record.</param>
    public void Deconstruct(out string shardId, out ChangeRecord record)
    {
        shardId = ShardId;
        record = Record;
    }

    /// <summary>Tells whether this value holds the same shard as <paramref name="other"/>, ordinal, and an equal record.</summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true"/> if they are equal.</returns>
    public bool Equals(ShardChangeRecord other) => string.Equals(ShardId, other.ShardId, StringComparison.Ordinal) && Equals(Record, other.Record);

    /// <summary>Tells whether <paramref name="obj"/> is a <see cref="ShardChangeRecord"/> equal to this one.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if it is an equal value.</returns>
    public override bool Equals(object? obj) => obj is ShardChangeRecord other && Equals(other);

    /// <summary>Gets a hash code from the shard and the record.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(ShardId, Record);

    /// <summary>Gets the shard and the record, as a <c>record</c> prints itself: <c>ShardChangeRecord { ShardId = ..., Record = ... }</c>.</summary>
    /// <returns>The text, which holds the record with its images and so the values of the item.</returns>
    public override string ToString() => $"ShardChangeRecord {{ ShardId = {ShardId}, Record = {Record} }}";
}
