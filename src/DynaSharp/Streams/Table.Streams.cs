using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp;

/// <content>
/// The typed reads of several entities of the stream of the table. Every table has them, whatever type it was opened for: <c>orders.ReadChangesAsync&lt;Customer, Order&gt;(...)</c> of a
/// <c>Table&lt;Order&gt;</c> reads the same changes as <c>shop.ReadChangesAsync&lt;Customer, Order&gt;(...)</c>. They are members of the table, not extensions in <see cref="TableStreamExtensions"/> as the reading of one entity is,
/// because they need the client, the name and the keys of the table and nothing of its type, so every table has them: <c>shop.ReadChangesAsync&lt;Customer, Order&gt;(...)</c> of a
/// <c>Table&lt;Item&gt;</c> and <c>orders.ReadChangesAsync&lt;Customer, Order&gt;(...)</c> of <c>client.Table&lt;Order&gt;("Shop")</c> read the same changes. An extension over
/// <c>Table&lt;TTable&gt;</c> could not be called so: explicit type arguments name every type parameter of the method, that of the table too.
/// </content>
public sealed partial class Table<T>
{
    /// <summary>
    /// Reads the changes of 2 entities of a table from its stream, typed: <c>shop.ReadChangesAsync&lt;Customer, Order&gt;(streams, ShardIteratorType.Latest, cancellationToken: ct)</c>
    /// gives a <see cref="MixedChange{T1, T2}"/> per change of one of them, which a <c>switch</c> (or <c>Match</c> in C# 14) tells apart, and skips the other entities of the table.
    /// </summary>
    /// <typeparam name="T1">The first entity.</typeparam>
    /// <typeparam name="T2">The second entity.</typeparam>
    /// <param name="streams">The client the stream is read with, for the same account and region as the client of the table.</param>
    /// <param name="shardIteratorType">Where reading starts, see <see cref="ReadStreamRequest.ShardIteratorType"/>.</param>
    /// <param name="emptyPageDelay">The wait after an empty page, see <see cref="ReadStreamRequest.EmptyPageDelay"/>, or <see langword="null"/> for its default of one second.</param>
    /// <param name="onJoined">What is called once the reading holds a position in every shard it starts with, see <see cref="ReadStreamRequest.OnJoined"/>, or <see langword="null"/>.</param>
    /// <param name="onMappingFailure">
    /// Called when an image has the discriminator of a type of the read but cannot be read as it; the change is then skipped, and counted in
    /// <c>dynasharp.client.operation.skipped_items</c>. <see langword="null"/>, the default, ends the read with the <see cref="DynamoDbMappingException"/> instead. Log
    /// <see cref="ShardChangeRecord.ShardId"/>, <see cref="ShardChangeRecord.SequenceNumber"/> and <see cref="ChangeRecord.EventName"/>, not the record: it holds the values of the item.
    /// </param>
    /// <param name="onGap">What is called with the id of a shard when the reading may have skipped records of it, so the caller can reload what it keeps, see <see cref="ReadStreamRequest.OnGap"/>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>The changes of the entities, in the order <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> returns their records.</returns>
    /// <inheritdoc cref="TableStreamExtensions.ReadChangesAsync{T}" path="/remarks"/>
    /// <exception cref="ArgumentNullException"><paramref name="streams"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The types cannot be told apart: a discriminator occurs twice, or they keep it in different attributes, as for <c>Of&lt;...&gt;()</c> of a query.</exception>
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
    public IAsyncEnumerable<MixedChange<T1, T2>> ReadChangesAsync<T1, T2>(
        DynamoDbStreamsClient streams,
        ShardIteratorType shardIteratorType,
        TimeSpan? emptyPageDelay = null,
        Action? onJoined = null,
        Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure = null,
        Action<string>? onGap = null,
        CancellationToken cancellationToken = default)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
    {
        Mixed<T1, T2>.Schema.Check();
        return TableStreamExtensions.StartChanges<T, MixedChange<T1, T2>, MixedChange<T1, T2>>(this, streams, shardIteratorType, emptyPageDelay, onJoined, onMappingFailure, onGap, cancellationToken);
    }

    /// <summary>
    /// Reads the changes of 3 entities of a table from its stream, typed: <c>shop.ReadChangesAsync&lt;Customer, Order, Invoice&gt;(streams, ShardIteratorType.Latest, cancellationToken: ct)</c>
    /// gives a <see cref="MixedChange{T1, T2, T3}"/> per change of one of them, which a <c>switch</c> (or <c>Match</c> in C# 14) tells apart, and skips the other entities of the table.
    /// </summary>
    /// <typeparam name="T1">The first entity.</typeparam>
    /// <typeparam name="T2">The second entity.</typeparam>
    /// <typeparam name="T3">The third entity.</typeparam>
    /// <param name="streams">The client the stream is read with, for the same account and region as the client of the table.</param>
    /// <param name="shardIteratorType">Where reading starts, see <see cref="ReadStreamRequest.ShardIteratorType"/>.</param>
    /// <param name="emptyPageDelay">The wait after an empty page, see <see cref="ReadStreamRequest.EmptyPageDelay"/>, or <see langword="null"/> for its default of one second.</param>
    /// <param name="onJoined">What is called once the reading holds a position in every shard it starts with, see <see cref="ReadStreamRequest.OnJoined"/>, or <see langword="null"/>.</param>
    /// <param name="onMappingFailure">
    /// Called when an image has the discriminator of a type of the read but cannot be read as it; the change is then skipped, and counted in
    /// <c>dynasharp.client.operation.skipped_items</c>. <see langword="null"/>, the default, ends the read with the <see cref="DynamoDbMappingException"/> instead. Log
    /// <see cref="ShardChangeRecord.ShardId"/>, <see cref="ShardChangeRecord.SequenceNumber"/> and <see cref="ChangeRecord.EventName"/>, not the record: it holds the values of the item.
    /// </param>
    /// <param name="onGap">What is called with the id of a shard when the reading may have skipped records of it, so the caller can reload what it keeps, see <see cref="ReadStreamRequest.OnGap"/>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>The changes of the entities, in the order <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> returns their records.</returns>
    /// <inheritdoc cref="TableStreamExtensions.ReadChangesAsync{T}" path="/remarks"/>
    /// <exception cref="ArgumentNullException"><paramref name="streams"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The types cannot be told apart: a discriminator occurs twice, or they keep it in different attributes, as for <c>Of&lt;...&gt;()</c> of a query.</exception>
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
    public IAsyncEnumerable<MixedChange<T1, T2, T3>> ReadChangesAsync<T1, T2, T3>(
        DynamoDbStreamsClient streams,
        ShardIteratorType shardIteratorType,
        TimeSpan? emptyPageDelay = null,
        Action? onJoined = null,
        Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure = null,
        Action<string>? onGap = null,
        CancellationToken cancellationToken = default)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
        where T3 : IDynamoDbDiscriminated<T3>
    {
        Mixed<T1, T2, T3>.Schema.Check();
        return TableStreamExtensions.StartChanges<T, MixedChange<T1, T2, T3>, MixedChange<T1, T2, T3>>(this, streams, shardIteratorType, emptyPageDelay, onJoined, onMappingFailure, onGap, cancellationToken);
    }

    /// <summary>
    /// Reads the changes of 4 entities of a table from its stream, typed: <c>shop.ReadChangesAsync&lt;Customer, Order, Invoice, Payment&gt;(streams, ShardIteratorType.Latest, cancellationToken: ct)</c>
    /// gives a <see cref="MixedChange{T1, T2, T3, T4}"/> per change of one of them, which a <c>switch</c> (or <c>Match</c> in C# 14) tells apart, and skips the other entities of the table.
    /// </summary>
    /// <typeparam name="T1">The first entity.</typeparam>
    /// <typeparam name="T2">The second entity.</typeparam>
    /// <typeparam name="T3">The third entity.</typeparam>
    /// <typeparam name="T4">The fourth entity.</typeparam>
    /// <param name="streams">The client the stream is read with, for the same account and region as the client of the table.</param>
    /// <param name="shardIteratorType">Where reading starts, see <see cref="ReadStreamRequest.ShardIteratorType"/>.</param>
    /// <param name="emptyPageDelay">The wait after an empty page, see <see cref="ReadStreamRequest.EmptyPageDelay"/>, or <see langword="null"/> for its default of one second.</param>
    /// <param name="onJoined">What is called once the reading holds a position in every shard it starts with, see <see cref="ReadStreamRequest.OnJoined"/>, or <see langword="null"/>.</param>
    /// <param name="onMappingFailure">
    /// Called when an image has the discriminator of a type of the read but cannot be read as it; the change is then skipped, and counted in
    /// <c>dynasharp.client.operation.skipped_items</c>. <see langword="null"/>, the default, ends the read with the <see cref="DynamoDbMappingException"/> instead. Log
    /// <see cref="ShardChangeRecord.ShardId"/>, <see cref="ShardChangeRecord.SequenceNumber"/> and <see cref="ChangeRecord.EventName"/>, not the record: it holds the values of the item.
    /// </param>
    /// <param name="onGap">What is called with the id of a shard when the reading may have skipped records of it, so the caller can reload what it keeps, see <see cref="ReadStreamRequest.OnGap"/>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>The changes of the entities, in the order <see cref="DynamoDbStreamsClient.ReadStreamAsync"/> returns their records.</returns>
    /// <inheritdoc cref="TableStreamExtensions.ReadChangesAsync{T}" path="/remarks"/>
    /// <exception cref="ArgumentNullException"><paramref name="streams"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The types cannot be told apart: a discriminator occurs twice, or they keep it in different attributes, as for <c>Of&lt;...&gt;()</c> of a query.</exception>
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
    public IAsyncEnumerable<MixedChange<T1, T2, T3, T4>> ReadChangesAsync<T1, T2, T3, T4>(
        DynamoDbStreamsClient streams,
        ShardIteratorType shardIteratorType,
        TimeSpan? emptyPageDelay = null,
        Action? onJoined = null,
        Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure = null,
        Action<string>? onGap = null,
        CancellationToken cancellationToken = default)
        where T1 : IDynamoDbDiscriminated<T1>
        where T2 : IDynamoDbDiscriminated<T2>
        where T3 : IDynamoDbDiscriminated<T3>
        where T4 : IDynamoDbDiscriminated<T4>
    {
        Mixed<T1, T2, T3, T4>.Schema.Check();
        return TableStreamExtensions.StartChanges<T, MixedChange<T1, T2, T3, T4>, MixedChange<T1, T2, T3, T4>>(this, streams, shardIteratorType, emptyPageDelay, onJoined, onMappingFailure, onGap, cancellationToken);
    }
}
