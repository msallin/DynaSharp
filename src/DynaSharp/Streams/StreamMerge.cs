using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DynaSharp.Http;

namespace DynaSharp.Streams;

/// <summary>
/// Reads every shard of a stream, each with <see cref="DynamoDbStreamsClient.ReadShardAsync"/> on a task of its own, and hands the records over through one
/// small bounded channel, so a shard waits while the consumer is behind. A shard that ends starts its children, which is how a parent comes before them.
/// </summary>
/// <remarks>
/// <para>
/// The set of readers grows while the stream is read, so there is no list of tasks: a count of what runs, the discovery at the start included, completes the channel
/// and <see cref="_idle"/> when it reaches zero. A reader increments it for its children before it decrements it for itself, so it never reaches zero early.
/// </para>
/// <para>
/// The first failure wins the completion of the channel, drops what the channel still holds so that it is the next thing the consumer sees, and stops the other
/// readers. A reader that was stopped is not a failure. However the enumeration ends, its <c>finally</c> stops what still runs and waits for it, so no reader
/// outlives the enumerator.
/// </para>
/// </remarks>
internal sealed class StreamMerge
{
    // Small on purpose: it decouples the shards from the consumer, and a deeper buffer only holds records a failure would drop.
    private static readonly BoundedChannelOptions HandoverOptions = new(16);

    private readonly Channel<ShardChangeRecord> _records = Channel.CreateBounded<ShardChangeRecord>(HandoverOptions);
    private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DynamoDbStreamsClient _client;
    private readonly ClientCore _core;
    private readonly ReadStreamRequest _request;
    private readonly CancellationTokenSource _stop;

    // The span that was current when the enumeration started. Each shard is a span under it, a child shard too, which links to its parent instead: as a
    // child of its parent, a stream read for days would be one trace that grows for days.
    private readonly ActivityContext _caller;
    private int _running;

    private StreamMerge(DynamoDbStreamsClient client, ClientCore core, ReadStreamRequest request, CancellationTokenSource stop)
    {
        _client = client;
        _core = core;
        _request = request;
        _stop = stop;
        _caller = Activity.Current?.Context ?? default;
    }

    /// <summary>Reads the stream, on enumeration.</summary>
    /// <param name="client">The client the shards are read with.</param>
    /// <param name="core">The core of the client, for its clock, its telemetry and its log.</param>
    /// <param name="request">The request, checked by the caller.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The records of all shards.</returns>
    public static async IAsyncEnumerable<ShardChangeRecord> ReadAsync(
        DynamoDbStreamsClient client,
        ClientCore core,
        ReadStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var run = new StreamMerge(client, core, request, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        try
        {
            run.Enter();
            _ = run.DiscoverAsync();
            await foreach (var change in run._records.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                core.Telemetry.CountStreamRecord();
                yield return change;
            }

            // Readers that were stopped end quietly, and from here that looks like the end of the stream.
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await run.StopAsync().ConfigureAwait(false);
        }
    }

    private static bool IsClosed(Shard shard) => shard.SequenceNumberRange?.EndingSequenceNumber is not null;

    // Never fails, like every reader: what goes wrong goes through the channel.
    private async Task DiscoverAsync()
    {
        try
        {
            var (shards, _) = await ShardsAsync(null).ConfigureAwait(false);
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shard in shards)
            {
                listed.Add(shard.ShardId!);
            }

            var latest = _request.ShardIteratorType == ShardIteratorType.Latest;
            foreach (var shard in shards)
            {
                var closed = IsClosed(shard);
                if (latest)
                {
                    // A shard closed at the start counts as read, so its children that are open at the start are read from now on, as every open shard.
                    if (!closed)
                    {
                        Launch(shard.ShardId!, closed: false, ShardIteratorType.Latest, default);
                    }
                }
                else if (shard.ParentShardId is not { } parent || !listed.Contains(parent))
                {
                    // A shard whose parent is listed starts once the parent has been read, as its child.
                    Launch(shard.ShardId!, closed, ShardIteratorType.TrimHorizon, default);
                }
            }
        }
#pragma warning disable CA1031 // The failure is not swallowed: the consumer gets it from the channel.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            await FailAsync(failure).ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }
    }

    private void Launch(string shardId, bool closed, ShardIteratorType position, ActivityContext parentShard)
    {
        Enter();

        // On the pool, so a reader never runs inside the one that started it, nor on the context of the consumer.
        _ = Task.Run(() => ReadShardAsync(shardId, closed, position, parentShard));
    }

    private async Task ReadShardAsync(string shardId, bool closed, ShardIteratorType position, ActivityContext parentShard)
    {
        var stopped = _stop.Token;

        // The span of the parent shard flowed here; the span of this one goes under the caller's. The change stays in this method, as an async method
        // restores the current activity of its caller when it returns.
        Activity.Current = null;
        var activity = _core.Telemetry.StartShard(shardId, _caller, parentShard);
        var records = 0;
        Exception? failed = null;
        try
        {
            var shard = new ReadShardRequest
            {
                StreamArn = _request.StreamArn,
                ShardId = shardId,
                ShardIteratorType = position,
                EmptyPageDelay = _request.EmptyPageDelay,
            };
            try
            {
                await foreach (var record in _client.ReadShardAsync(shard, stopped).ConfigureAwait(false))
                {
                    await _records.Writer.WriteAsync(new ShardChangeRecord(shardId, record), stopped).ConfigureAwait(false);
                    records++;
                }
            }
            catch (ResourceNotFoundException gone) when (closed)
            {
                // DynamoDB removes a closed shard 24 hours after it closed, and its records with it: they are lost, as those before a trimmed position are.
                throw new TrimmedDataAccessException(
                    $"The closed shard {shardId} was removed before it was read, 24 hours after it closed, so its records are lost.",
                    gone.RequestId,
                    gone.StatusCode);
            }

            await StartChildrenAsync(shardId, activity?.Context ?? default, stopped).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            // Stopped by the consumer or by another reader, which has reported why.
        }
#pragma warning disable CA1031 // The failure is not swallowed: the consumer gets it from the channel.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            failed = failure;
            await FailAsync(failure).ConfigureAwait(false);
        }
        finally
        {
            _core.Telemetry.EndShard(activity, records, failed);
            Leave();
        }
    }

    // The filter finds the children without listing the stream, and the check of the parent keeps an emulator that ignores the filter from starting every shard.
    // DynamoDB may list the children of a shard a moment after it closed, so while the stream is enabled a shard without children is asked about again.
    private async Task StartChildrenAsync(string shardId, ActivityContext span, CancellationToken stopped)
    {
        var filter = new ShardFilter { Type = ShardFilterType.ChildShards, ShardId = shardId };
        while (true)
        {
            var (shards, status) = await ShardsAsync(filter).ConfigureAwait(false);
            var children = 0;
            foreach (var shard in shards)
            {
                if (string.Equals(shard.ParentShardId, shardId, StringComparison.Ordinal))
                {
                    Launch(shard.ShardId!, IsClosed(shard), ShardIteratorType.TrimHorizon, span);
                    children++;
                }
            }

            if (children > 0 || status is not (StreamStatus.Enabled or StreamStatus.Enabling))
            {
                _core.LogShardFinished(shardId, children);
                return;
            }

            await Task.Delay(_request.EmptyPageDelay, _core.TimeProvider, stopped).ConfigureAwait(false);
        }
    }

    // Every page; a shard without an id is no shard to read. The status is the one of the last page.
    private async Task<(List<Shard> Shards, StreamStatus Status)> ShardsAsync(ShardFilter? filter)
    {
        var shards = new List<Shard>();
        var status = StreamStatus.Unknown;
        string? start = null;
        do
        {
            var request = new DescribeStreamRequest { StreamArn = _request.StreamArn, ShardFilter = filter, ExclusiveStartShardId = start };
            var page = (await _client.DescribeStreamAsync(request, _stop.Token).ConfigureAwait(false)).StreamDescription;
            if (page is null)
            {
                break;
            }

            foreach (var shard in page.Shards)
            {
                if (shard.ShardId is not null)
                {
                    shards.Add(shard);
                }
            }

            status = page.StreamStatus;
            start = page.LastEvaluatedShardId;
        }
        while (start is not null);

        return (shards, status);
    }

    private async Task FailAsync(Exception failure)
    {
        if (_stop.IsCancellationRequested)
        {
            // The consumer stopped, or another reader failed first and reported why.
            return;
        }

        // The records still held were read before the failure; the consumer gets the failure next instead. If another failure won, this one drops nothing new.
        _records.Writer.TryComplete(failure);
        while (_records.Reader.TryRead(out _))
        {
        }

        await _stop.CancelAsync().ConfigureAwait(false);
    }

    private async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _idle.Task.ConfigureAwait(false);
        _stop.Dispose();
    }

    private void Enter() => Interlocked.Increment(ref _running);

    private void Leave()
    {
        if (Interlocked.Decrement(ref _running) == 0)
        {
            _records.Writer.TryComplete();
            _idle.TrySetResult();
        }
    }
}
