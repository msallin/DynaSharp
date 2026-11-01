using DynaSharp.Streams;

namespace DynaSharp.Requests;

/// <summary>
/// Whether a table has a stream and what its records contain.
/// </summary>
/// <remarks>
/// The stream is read with <see cref="DynamoDbStreamsClient"/>. Stream records are kept for 24 hours.
/// </remarks>
public sealed record StreamSpecification
{
    /// <summary>Gets a value indicating whether the table has a stream.</summary>
    public required bool StreamEnabled { get; init; }

    /// <summary>
    /// Gets what the records of the stream contain. Required by DynamoDB when <see cref="StreamEnabled"/> is
    /// <see langword="true"/>; <see cref="Requests.StreamViewType.Unspecified"/> (the default) sends nothing, and DynamoDB then rejects the
    /// request. When reading, <see cref="Requests.StreamViewType.Unspecified"/> also means that none was reported, as for a table without a stream.
    /// </summary>
    public StreamViewType StreamViewType { get; init; }
}
