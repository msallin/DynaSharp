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
        return ReadAsync(table, streams, Check(shardIteratorType, emptyPageDelay, onJoined), typed: false, cancellationToken);
    }

    /// <summary>
    /// Reads the changes of the entity of the table from its stream, typed: <c>shop.Of&lt;Order&gt;().ReadChangesAsync(streams, ShardIteratorType.Latest, cancellationToken: ct)</c>
    /// gives a <see cref="Change{T}"/> per change of an order, and skips the other entities of the table.
    /// </summary>
    /// <typeparam name="T">The entity. One without a discriminator takes every change, as <see cref="ChangeRecord.As{T}"/> does.</typeparam>
    /// <param name="table">The table, whose client describes it.</param>
    /// <param name="streams">The client the stream is read with, for the same account and region as the client of the table.</param>
    /// <param name="shardIteratorType">Where reading starts, see <see cref="ReadStreamRequest.ShardIteratorType"/>.</param>
    /// <param name="emptyPageDelay">The wait after an empty page, see <see cref="ReadStreamRequest.EmptyPageDelay"/>, or <see langword="null"/> for its default of one second.</param>
    /// <param name="onJoined">What is called once the reading holds a position in every shard it starts with, see <see cref="ReadStreamRequest.OnJoined"/>, or <see langword="null"/>.</param>
    /// <param name="onMappingFailure">
    /// What takes a change whose image is of a type and cannot be read as it, which is then left out, or <see langword="null"/> to end the reading with the
    /// <see cref="DynamoDbMappingException"/>, as <c>OnMappingFailure</c> of a query or scan does; it counts in <c>dynasharp.client.operation.skipped_items</c>. It gets the
    /// exception and the record with its shard, so a log names the change by <see cref="ShardChangeRecord.ShardId"/> and <see cref="ShardChangeRecord.SequenceNumber"/>. The
    /// record holds the images, the values of the item: log those two and <see cref="ChangeRecord.EventName"/>, not the record, and its keys only where keys may be logged.
    /// </param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>The changes of the entity, in the order <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> returns their records.</returns>
    /// <remarks>
    /// <para>
    /// The reading is that of <see cref="ReadStreamAsync{T}"/>, with its <c>DescribeTable</c> at the start of each enumeration, and it also checks the view there: it must be
    /// <see cref="StreamViewType.NewAndOldImages"/>, because without the old image a removal could not be typed and would be missed without a word, and without the new image an
    /// insert would. Another view throws <see cref="InvalidOperationException"/>; <see cref="ChangeRecord.As{T}"/> types what a record of any view has.
    /// </para>
    /// <para>
    /// A stream cannot be filtered at its source, so every change of the table is read and typed here, from the discriminator of its images, and a change of no type of the reading is
    /// skipped. An item whose type changed at its key gives two changes, the removal of the old type, then the insert of the new one, each when its type is read. Arguments are
    /// checked at the call, and nothing is requested until the first change is asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="streams"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="shardIteratorType"/> is neither <see cref="ShardIteratorType.TrimHorizon"/> nor <see cref="ShardIteratorType.Latest"/>, or <paramref name="emptyPageDelay"/> is
    /// negative or 15 minutes or more.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The table has no enabled stream, or its view is not <see cref="StreamViewType.NewAndOldImages"/> (raised while reading).
    /// </exception>
    /// <exception cref="DynamoDbMappingException">An image of a type cannot be read as it and <paramref name="onMappingFailure"/> is <see langword="null"/> (raised while reading).</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist (raised while reading), or what <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> throws it for.</exception>
    /// <exception cref="DynamoDbException">
    /// <c>DescribeTable</c> fails, or the reading of the stream does, with what <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> throws, <see cref="TrimmedDataAccessException"/> among it.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static IAsyncEnumerable<Change<T>> ReadChangesAsync<T>(
        this Table<T> table,
        DynamoDbStreamsClient streams,
        ShardIteratorType shardIteratorType,
        TimeSpan? emptyPageDelay = null,
        Action? onJoined = null,
        Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure = null,
        CancellationToken cancellationToken = default)
        where T : IDynamoDbEntity<T> =>
        StartChanges<T, SingleChange<T>, Change<T>>(table, streams, shardIteratorType, emptyPageDelay, onJoined, onMappingFailure, cancellationToken);

    internal static IAsyncEnumerable<TOut> StartChanges<TTable, TTypes, TOut>(
        Table<TTable> table,
        DynamoDbStreamsClient streams,
        ShardIteratorType shardIteratorType,
        TimeSpan? emptyPageDelay,
        Action? onJoined,
        Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure,
        CancellationToken cancellationToken)
        where TTable : IDynamoDbDocument<TTable>
        where TTypes : IChangeTypes<TOut>
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(streams);
        var request = Check(shardIteratorType, emptyPageDelay, onJoined);

        // A skipped change counts once the callback has had it, as a skipped item of a query does. One delegate per call, none per record.
        Action<DynamoDbMappingException, ShardChangeRecord>? skip = null;
        if (onMappingFailure is not null)
        {
            skip = (failure, change) =>
            {
                onMappingFailure(failure, change);
                streams.CountSkippedItem(table.Name);
            };
        }

        return ReadTypedAsync<TTable, TTypes, TOut>(table, streams, request, skip, cancellationToken);
    }

    private static async IAsyncEnumerable<ShardChangeRecord> ReadAsync<T>(
        Table<T> table,
        DynamoDbStreamsClient streams,
        ReadStreamRequest request,
        bool typed,
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

        if (typed && description.StreamSpecification.StreamViewType != StreamViewType.NewAndOldImages)
        {
            throw new InvalidOperationException(
                $"The stream of the table {table.Name} has the view {description.StreamSpecification.StreamViewType}, and a typed reading needs NewAndOldImages: without the old image " +
                "a removal has nothing to be typed by, and without the new image an insert has not. Read the records with ReadStreamAsync, or switch the stream to NewAndOldImages, which starts a new stream.");
        }

        await foreach (var change in streams.ReadStreamAsync(request with { StreamArn = streamArn }, cancellationToken).ConfigureAwait(false))
        {
            yield return change;
        }
    }

    private static ReadStreamRequest Check(ShardIteratorType shardIteratorType, TimeSpan? emptyPageDelay, Action? onJoined)
    {
        // The ARN comes with the first record; the rest is checked now, as the client checks a request.
        var request = new ReadStreamRequest { StreamArn = string.Empty, ShardIteratorType = shardIteratorType, OnJoined = onJoined };
        if (emptyPageDelay is { } delay)
        {
            request = request with { EmptyPageDelay = delay };
        }

        DynamoDbStreamsClient.CheckStreamRequest(request);
        return request;
    }

    private static async IAsyncEnumerable<TOut> ReadTypedAsync<TTable, TTypes, TOut>(
        Table<TTable> table,
        DynamoDbStreamsClient streams,
        ReadStreamRequest request,
        Action<DynamoDbMappingException, ShardChangeRecord>? skip,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TTable : IDynamoDbDocument<TTable>
        where TTypes : IChangeTypes<TOut>
    {
        await foreach (var change in ReadAsync(table, streams, request, typed: true, cancellationToken).ConfigureAwait(false))
        {
            var count = ChangeTyping.Split<TTypes, TOut>(change, skip, out var first, out var second);
            if (count > 0)
            {
                yield return first;
            }

            if (count > 1)
            {
                yield return second;
            }
        }
    }
}
