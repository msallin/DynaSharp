using DynaSharp.Requests;

namespace DynaSharp.Streams;

/// <summary>
/// The settings of a stream and one page of its shards.
/// </summary>
public sealed record StreamDescription
{
    /// <summary>
    /// Gets the ARN of the stream. DynamoDB sends it with every description: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? StreamArn { get; init; }

    /// <summary>
    /// Gets the label of the stream: a timestamp of its creation. DynamoDB sends it with every description: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? StreamLabel { get; init; }

    /// <summary>Gets the state of the stream. Records can be read only from an <see cref="Streams.StreamStatus.Enabled"/> stream or a recently disabled one.</summary>
    public StreamStatus StreamStatus { get; init; }

    /// <summary>Gets what the records of the stream contain.</summary>
    public StreamViewType StreamViewType { get; init; }

    /// <summary>
    /// Gets when the stream was requested. DynamoDB sends it with every description: <see langword="null"/> only if the response omits it.
    /// </summary>
    public DateTimeOffset? CreationRequestDateTime { get; init; }

    /// <summary>
    /// Gets the name of the table the stream belongs to. DynamoDB sends it with every description: <see langword="null"/> only if the response omits it.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>Gets the primary key of the table: the partition key, followed by the sort key if there is one.</summary>
    public IReadOnlyList<KeySchemaElement> KeySchema { get; init; } = [];

    /// <summary>
    /// Gets one page of the shards, up to 100. Empty when the stream has none. Shards that have been read completely are not
    /// removed from the list at once; check <see cref="SequenceNumberRange.EndingSequenceNumber"/> to tell open from closed shards.
    /// </summary>
    public IReadOnlyList<Shard> Shards { get; init; } = [];

    /// <summary>
    /// Gets the identifier of the last shard of this page, to pass as <see cref="DescribeStreamRequest.ExclusiveStartShardId"/> to
    /// get the next page. <see langword="null"/> on the last page, and only then: a page with fewer shards than the limit can still
    /// have a successor.
    /// </summary>
    public string? LastEvaluatedShardId { get; init; }
}
