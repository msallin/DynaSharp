using System.Runtime.CompilerServices;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.Streams;

/// <summary>Reads the stream of a table, which <c>DescribeTable</c> names, so the caller needs no ARN.</summary>
public static class TableStreamExtensions
{
    /// <summary>
    /// Reads every shard of the stream the table has now, as <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> does: <c>orders.ReadStreamAsync(streams, ShardIteratorType.Latest, cancellationToken: ct)</c>.
    /// </summary>
    /// <typeparam name="T">How the table reads its items, which does not change the records: an image is an <see cref="Item"/>.</typeparam>
    /// <param name="table">The table, whose client describes it.</param>
    /// <param name="streams">The client the stream is read with, for the same account and region as the client of the table.</param>
    /// <param name="shardIteratorType">Where reading starts, see <see cref="ReadStreamRequest.ShardIteratorType"/>.</param>
    /// <param name="emptyPageDelay">The wait after an empty page, see <see cref="ReadStreamRequest.EmptyPageDelay"/>, or <see langword="null"/> for its default of one second.</param>
    /// <param name="onJoined">What is called once the reading holds a position in every shard it starts with, see <see cref="ReadStreamRequest.OnJoined"/>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>The records of all shards, as <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> returns them.</returns>
    /// <remarks>
    /// <para>
    /// Each enumeration starts with <c>DescribeTable</c> through <see cref="Table{T}.Client"/>, and reads the stream that <see cref="TableDescription.LatestStreamArn"/> names then.
    /// A stream that is switched off and on again is a new stream with a new ARN, so an enumeration that starts after that reads the new one; one that runs reads its stream
    /// until that ends. The table takes <c>dynamodb:DescribeTable</c> for this, besides the permissions of the stream.
    /// </para>
    /// <para>
    /// The view of the stream is not checked: with <see cref="StreamViewType.KeysOnly"/> a record has no image, and <see cref="Table{T}.DescribeAsync"/> tells the view,
    /// <see cref="StreamSpecification.StreamViewType"/>. Arguments are checked at the call, and nothing is requested until the first record is asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="streams"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="shardIteratorType"/> is neither <see cref="ShardIteratorType.TrimHorizon"/> nor <see cref="ShardIteratorType.Latest"/>, or <paramref name="emptyPageDelay"/> is
    /// negative or 15 minutes or more.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The table has no enabled stream: <see cref="StreamSpecification.StreamEnabled"/> is not <see langword="true"/>, or no stream is named (raised while reading).
    /// </exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist (raised while reading), or what <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> throws it for.</exception>
    /// <exception cref="DynamoDbException">
    /// <c>DescribeTable</c> fails, or the reading of the stream does, with what <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> throws, <see cref="TrimmedDataAccessException"/> among it.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static IAsyncEnumerable<ShardChangeRecord> ReadStreamAsync<T>(
        this Table<T> table,
        DynamoDbStreamsClient streams,
        ShardIteratorType shardIteratorType,
        TimeSpan? emptyPageDelay = null,
        Action? onJoined = null,
        CancellationToken cancellationToken = default)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(streams);

        // The ARN comes with the first record; the rest is checked now, as the client checks a request.
        var request = new ReadStreamRequest { StreamArn = string.Empty, ShardIteratorType = shardIteratorType, OnJoined = onJoined };
        if (emptyPageDelay is { } delay)
        {
            request = request with { EmptyPageDelay = delay };
        }

        DynamoDbStreamsClient.CheckStreamRequest(request);
        return ReadAsync(table, streams, request, cancellationToken);
    }

    private static async IAsyncEnumerable<ShardChangeRecord> ReadAsync<T>(
        Table<T> table,
        DynamoDbStreamsClient streams,
        ReadStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDynamoDbDocument<T>
    {
        var description = await table.DescribeAsync(cancellationToken).ConfigureAwait(false);

        // LatestStreamArn stays after the stream was switched off, so the specification tells whether it is on.
        if (description.StreamSpecification?.StreamEnabled != true || description.LatestStreamArn is not { } streamArn)
        {
            throw new InvalidOperationException(
                $"The table {table.Name} has no enabled stream. Turn one on with Stream(...) of CreateTable(), or with StreamSpecification of UpdateTableAsync.");
        }

        await foreach (var change in streams.ReadStreamAsync(request with { StreamArn = streamArn }, cancellationToken).ConfigureAwait(false))
        {
            yield return change;
        }
    }
}
