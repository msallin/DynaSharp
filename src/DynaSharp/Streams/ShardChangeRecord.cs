namespace DynaSharp.Streams;

/// <summary>
/// A record of <see cref="DynamoDbStreamsClient.ReadStreamAsync"/>, with the shard it came from.
/// </summary>
/// <remarks>
/// <para>
/// The shard and the sequence number of the record are the position to keep: <see cref="DynamoDbStreamsClient.ReadShardAsync"/> with this
/// <see cref="ShardId"/>, <see cref="ShardIteratorType.AfterSequenceNumber"/> and <see cref="StreamRecord.SequenceNumber"/> of
/// <see cref="ChangeRecord.DynamoDb"/> reads the shard on after this record.
/// </para>
/// <para>
/// The shard is here and not on <see cref="ChangeRecord"/> because <c>GetRecords</c> does not say it: a record of
/// <see cref="DynamoDbStreamsClient.GetRecordsAsync"/> comes from an iterator, which is opaque, so a property of <see cref="ChangeRecord"/> would be
/// <see langword="null"/> there and set elsewhere, and would change the equality of records that are the same change.
/// </para>
/// </remarks>
/// <param name="ShardId">The identifier of the shard the record came from.</param>
/// <param name="Record">The record, as <see cref="DynamoDbStreamsClient.ReadShardAsync"/> returns it.</param>
public readonly record struct ShardChangeRecord(string ShardId, ChangeRecord Record);
