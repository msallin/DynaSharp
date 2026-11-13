namespace DynaSharp.Streams;

/// <summary>
/// Reads every shard of a stream in one process, for <see cref="DynamoDbStreamsClient.ReadStreamAsync"/>.
/// </summary>
/// <remarks>
/// The position is one of two: <see cref="ShardIteratorType.TrimHorizon"/> for everything still kept, or <see cref="ShardIteratorType.Latest"/> for what
/// is written from now on. It applies to the shards open at the start; a shard that starts later is read from its first sequence number. There is no position to resume
/// at: to resume, read the shard of a record with <see cref="DynamoDbStreamsClient.ReadShardAsync"/> and <see cref="ShardIteratorType.AfterSequenceNumber"/>.
/// To read the stream of a table without its ARN, see <see cref="TableStreamExtensions.ReadStreamAsync{T}"/>.
/// </remarks>
public sealed record ReadStreamRequest
{
    /// <summary>Gets the ARN of the stream, <see cref="Requests.TableDescription.LatestStreamArn"/> of its table.</summary>
    public required string StreamArn { get; init; }

    /// <summary>
    /// Gets where reading starts in the shards that are open at the start: <see cref="ShardIteratorType.TrimHorizon"/> or <see cref="ShardIteratorType.Latest"/>.
    /// </summary>
    /// <remarks>
    /// With <see cref="ShardIteratorType.TrimHorizon"/> the shards that are closed at the start are read too, from their oldest record, each before its children.
    /// With <see cref="ShardIteratorType.Latest"/> they have nothing to read from now on, so only their open descendants are, from the start of the reading on, and
    /// a child listed after the start, of a split in progress, from its first record.
    /// </remarks>
    public required ShardIteratorType ShardIteratorType { get; init; }

    /// <summary>
    /// Gets how long the reader of a shard waits after a page without records before it asks again, on <see cref="DynamoDbClientOptions.TimeProvider"/>, as
    /// <see cref="ReadShardRequest.EmptyPageDelay"/> does. The default is one second.
    /// </summary>
    /// <remarks>
    /// The same wait, but at least a second, comes between two looks for the children of a closed shard while the stream is still enabled and DynamoDB lists none
    /// yet. Zero asks a shard again at once. The delay must stay below the 15 minutes an iterator lives.
    /// </remarks>
    public TimeSpan EmptyPageDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets what is called once when the reading holds a position in every shard it starts with, or <see langword="null"/> for nothing. A write that DynamoDB acknowledges after
    /// the call is read, unless the reading ends before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shards it starts with are those read at the start: with <see cref="ShardIteratorType.Latest"/> every shard that is open then, with
    /// <see cref="ShardIteratorType.TrimHorizon"/> every shard whose parent is not listed. A shard holds a position once <c>GetShardIterator</c> gave it an iterator, or said that it has
    /// nothing to read. A write after the call lands in one of those shards behind that position, or in a shard that is read from its first record: a child, or a shard listed later. So a test,
    /// or a view that loads the table and then follows its changes, writes or loads once this was called, and the changes from then on come through the reading.
    /// </para>
    /// <para>
    /// It is called on the task of a reader, after the first <c>MoveNextAsync</c> and before or after the first record, never more than once, and not at all when the reading fails
    /// or is stopped before. A stream without a shard to start with calls it once it is listed. A shard that a transient failure holds up delays it, see
    /// <see cref="DynamoDbStreamsClient.ReadStreamAsync"/>. Keep it short, such as <see cref="TaskCompletionSource.TrySetResult()"/>: the readers wait while it runs, and an
    /// exception from it ends the reading with that exception.
    /// </para>
    /// </remarks>
    public Action? OnJoined { get; init; }

    /// <summary>
    /// Gets what is called with the id of a shard when the reading may have skipped records of it, or <see langword="null"/> for nothing: a shard read from
    /// <see cref="ShardIteratorType.Latest"/> that has handed out no record yet and whose iterator expired, in an outage or while the caller was behind, is read from
    /// <see cref="ShardIteratorType.Latest"/> again, and what was written in between is not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resync on it.</b> Nothing reads those records afterwards, so a view that follows the table reloads it, as at the start after <see cref="OnJoined"/>, or schedules a reload.
    /// With <see cref="ShardIteratorType.TrimHorizon"/> it is never called: every shard starts at a sequence number then, and the reading goes on after the last record. Every other
    /// loss throws <see cref="TrimmedDataAccessException"/>, see <see cref="DynamoDbStreamsClient.ReadStreamAsync"/>.
    /// </para>
    /// <para>
    /// It is called on the task of the reader of that shard, as <see cref="ReadShardRequest.OnGap"/> is, and several shards can call it at the same time, so keep it short and thread-safe,
    /// such as setting a flag; an exception from it ends the reading with that exception. The client also counts it in <c>dynasharp.client.stream.gaps</c> and logs a warning (event 15).
    /// </para>
    /// </remarks>
    public Action<string>? OnGap { get; init; }
}
