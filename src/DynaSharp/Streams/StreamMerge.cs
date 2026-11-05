using System.Diagnostics;
using System.Globalization;
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
/// A reader goes through a transient failure on its own, see <see cref="ShardRun"/>, and only what is left fails the reading. The first failure wins the completion
/// of the channel, drops what the channel still holds so that it is the next thing the consumer sees, and stops the other readers. A reader that was stopped is
/// not a failure. However the enumeration ends, its <c>finally</c> stops what still runs and waits for it, so no reader
/// outlives the enumerator.
/// </para>
/// <para>
/// <see cref="ReadStreamRequest.OnJoined"/> is a count too: one for the discovery, which it drops once it has started every shard it starts, and one for each of those
/// shards, which its reader drops once the shard holds an iterator. Zero calls it, once.
/// </para>
/// </remarks>
internal sealed class StreamMerge
{
    // Small on purpose: it decouples the shards from the consumer, and a deeper buffer only holds records a failure would drop.
    private static readonly BoundedChannelOptions HandoverOptions = new(16);

    // The looks for the children of a shard read to its end. Until one is listed they come every EmptyPageDelay, but at least a second apart, since a DescribeStream
    // costs more than a GetRecords. After the first, DynamoDB may still list more: the Developer Guide says a shard can split into several, and a child can be listed
    // late. The looks for those come every five seconds for a minute, at most twelve DescribeStream calls for a shard that ended.
    private static readonly TimeSpan FirstChildLookDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MoreChildrenLookDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MoreChildrenWindow = TimeSpan.FromMinutes(1);

    // An hour below the 24 hours DynamoDB keeps a record, for the clock of this process and for a trim that comes early.
    private static readonly TimeSpan YoungShardAge = TimeSpan.FromHours(23);
    private static readonly long MaxUnixMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    private readonly Channel<ShardChangeRecord> _records = Channel.CreateBounded<ShardChangeRecord>(HandoverOptions);
    private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DynamoDbStreamsClient _client;
    private readonly ClientCore _core;
    private readonly ReadStreamRequest _request;
    private readonly CancellationTokenSource _stop;

    // The span that was current when the enumeration started. Each shard is a span under it, a child shard too, which links to its parent instead: as a
    // child of its parent, a stream read for days would be one trace that grows for days.
    private readonly ActivityContext _caller;

    // What a shard started by the discovery calls once it holds an iterator.
    private readonly Action _shardJoined;
    private int _running;
    private int _unjoined = 1;

    private StreamMerge(DynamoDbStreamsClient client, ClientCore core, ReadStreamRequest request, CancellationTokenSource stop)
    {
        _client = client;
        _core = core;
        _request = request;
        _stop = stop;
        _caller = Activity.Current?.Context ?? default;
        _shardJoined = Joined;
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

    // shardId-<20 digits of Unix milliseconds>-<suffix>, or null for an id of another form.
    private static DateTimeOffset? OpenedAt(string shardId)
    {
        const string Prefix = "shardId-";
        const int Digits = 20;
        var id = shardId.AsSpan();
        if (id.Length <= Prefix.Length + Digits || !id.StartsWith(Prefix, StringComparison.Ordinal) || id[Prefix.Length + Digits] != '-'
            || !long.TryParse(id.Slice(Prefix.Length, Digits), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || milliseconds > MaxUnixMilliseconds)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    // The children listed at the start, by parent.
    private static Dictionary<string, List<string>> ChildrenOf(List<Shard> shards)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var shard in shards)
        {
            if (shard.ParentShardId is { } parent)
            {
                if (!children.TryGetValue(parent, out var known))
                {
                    children[parent] = known = [];
                }

                known.Add(shard.ShardId!);
            }
        }

        return children;
    }

    // Never fails, like every reader: what goes wrong goes through the channel.
    private async Task DiscoverAsync()
    {
        try
        {
            // Not tried again: nothing has been read yet, and a setup that does not work, such as a wrong endpoint, shows at once.
            var (shards, _) = await ShardsAsync(null, null).ConfigureAwait(false);
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shard in shards)
            {
                listed.Add(shard.ShardId!);
            }

            var latest = _request.ShardIteratorType == ShardIteratorType.Latest;
            var children = latest ? ChildrenOf(shards) : null;
            foreach (var shard in shards)
            {
                var closed = IsClosed(shard);
                if (latest)
                {
                    // A shard open at the start is read from now on. A closed one has no records to read from now on, but it can still get a child that
                    // is not listed yet, so it is watched as a shard read to its end is.
                    if (!closed)
                    {
                        LaunchFirst(shard.ShardId!, closed: false, ShardIteratorType.Latest);
                    }
                    else
                    {
                        Watch(shard.ShardId!, children!.GetValueOrDefault(shard.ShardId!));
                    }
                }
                else if (shard.ParentShardId is not { } parent || !listed.Contains(parent))
                {
                    // The trim horizon of a shard without a listed parent is where the caller asked to start. A shard whose parent is listed starts once the parent has been
                    // read, as its child.
                    LaunchFirst(shard.ShardId!, closed, ShardIteratorType.TrimHorizon);
                }
            }

            Joined();
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

    // A shard closed at the start under Latest: its children listed then are handled by their own state at the start, and only a child listed later is
    // started, from its first record. The look is the one of a shard read to its end, with the window for more children counted from the opening of the
    // first known child, which its id tells: a shard whose children opened more than MoreChildrenWindow ago gets no look at all, which keeps a stream with
    // a long history from paying a look for every closed shard. Without a child it is looked at as long as the stream is enabled.
    private void Watch(string shardId, List<string>? known)
    {
        DateTimeOffset? firstChild = null;
        if (known is not null)
        {
            var now = _core.TimeProvider.GetUtcNow();
            var first = now;
            foreach (var child in known)
            {
                if (OpenedAt(child) is { } opened && opened < first)
                {
                    first = opened;
                }
            }

            if (now - first >= MoreChildrenWindow)
            {
                return;
            }

            firstChild = first;
        }

        Enter();
        _ = Task.Run(() => WatchAsync(shardId, known, firstChild));
    }

    // A watch that was stopped, by the consumer or by a reader that has reported why, ends in FailAsync too, which reports nothing once the reading stops. Unlike a
    // reader, a watch has no span to tell a stop from a failure for.
    private async Task WatchAsync(string shardId, List<string>? known, DateTimeOffset? firstChild)
    {
        try
        {
            await StartChildrenAsync(shardId, default, known, firstChild, new ShardRun(_core, shardId, null), read: false, _stop.Token).ConfigureAwait(false);
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

    // A shard the discovery starts, which the signal waits for.
    private void LaunchFirst(string shardId, bool closed, ShardIteratorType position)
    {
        Interlocked.Increment(ref _unjoined);
        Launch(shardId, closed, position, null, default, _shardJoined);
    }

    private void Launch(string shardId, bool closed, ShardIteratorType position, string? sequenceNumber, ActivityContext parentShard, Action? positioned = null)
    {
        Enter();

        // On the pool, so a reader never runs inside the one that started it, nor on the context of the consumer.
        _ = Task.Run(() => ReadShardAsync(shardId, closed, position, sequenceNumber, parentShard, positioned));
    }

    // An exception of the callback fails the reading, through the reader or the discovery that called it.
    private void Joined()
    {
        if (Interlocked.Decrement(ref _unjoined) == 0)
        {
            _request.OnJoined?.Invoke();
        }
    }

    // A child must not lose records to the trim horizon unnoticed. Every record of a shard is younger than the shard, and DynamoDB keeps a record 24 hours, so a
    // child that opened less than YoungShardAge ago has lost nothing yet, and its trim horizon is its first record. Its id tells when it opened, as DynamoDB's
    // read (shardId-00000001414562045508-2bac9cd2, Unix milliseconds). An older child, or one whose id tells nothing, is read at the starting sequence number
    // of its range, where DynamoDB answers TrimmedDataAccessException if records before the trim horizon are gone. That is not the rule for every child, because
    // a store may number the records of a shard from above its starting number and then answer so although nothing is gone, as DynamoDB Local does: a reader a
    // day behind is where that can show. Without a starting sequence number, which an emulator leaves out of an empty shard, the trim horizon is the start.
    private void LaunchChild(Shard child, ActivityContext parentShard)
    {
        var closed = IsClosed(child);
        if (OpenedAt(child.ShardId!) is { } opened && _core.TimeProvider.GetUtcNow() - opened < YoungShardAge)
        {
            Launch(child.ShardId!, closed, ShardIteratorType.TrimHorizon, null, parentShard);
        }
        else if (child.SequenceNumberRange?.StartingSequenceNumber is { Length: > 0 } start)
        {
            Launch(child.ShardId!, closed, ShardIteratorType.AtSequenceNumber, start, parentShard);
        }
        else
        {
            Launch(child.ShardId!, closed, ShardIteratorType.TrimHorizon, null, parentShard);
        }
    }

    private async Task ReadShardAsync(string shardId, bool closed, ShardIteratorType position, string? sequenceNumber, ActivityContext parentShard, Action? positioned)
    {
        var stopped = _stop.Token;

        // The span of the parent shard flowed here; the span of this one goes under the caller's. The change stays in this method, as an async method
        // restores the current activity of its caller when it returns.
        Activity.Current = null;
        var activity = _core.Telemetry.StartShard(shardId, _caller, parentShard);
        var records = 0;
        var run = new ShardRun(_core, shardId, positioned);
        Exception? failed = null;
        try
        {
            var shard = new ReadShardRequest
            {
                StreamArn = _request.StreamArn,
                ShardId = shardId,
                ShardIteratorType = position,
                SequenceNumber = sequenceNumber,
                EmptyPageDelay = _request.EmptyPageDelay,
            };
            try
            {
                await foreach (var record in _client.ReadShardCoreAsync(shard, run, stopped).ConfigureAwait(false))
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

            await StartChildrenAsync(shardId, activity?.Context ?? default, null, null, run, read: true, stopped).ConfigureAwait(false);
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
            _core.Telemetry.EndShard(activity, records, run.Retries, failed);
            Leave();
        }
    }

    // The filter finds the children without listing the stream, and the check of the parent keeps an emulator that ignores the filter from starting every shard.
    // DynamoDB may list a child a moment after its parent closed, so each child starts as soon as it is listed, and the looks go on: while the stream is enabled
    // and none is listed, until one is; after the first, for a while longer, see MoreChildrenWindow. A stream that is no longer enabled opens no more shards.
    // The children in known are not started again, and firstChild is when the first of them was there. A look that fails transiently is tried again through the run.
    private async Task StartChildrenAsync(string shardId, ActivityContext span, List<string>? known, DateTimeOffset? firstChild, ShardRun run, bool read, CancellationToken stopped)
    {
        var time = _core.TimeProvider;
        var filter = new ShardFilter { Type = ShardFilterType.ChildShards, ShardId = shardId };
        var started = known is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(known, StringComparer.Ordinal);
        var ended = time.GetTimestamp();
        for (var look = 0; ; look++)
        {
            var (shards, status) = await ShardsAsync(filter, run).ConfigureAwait(false);
            foreach (var shard in shards)
            {
                if (string.Equals(shard.ParentShardId, shardId, StringComparison.Ordinal) && started.Add(shard.ShardId!))
                {
                    if (look > 0)
                    {
                        _core.LogChildListedLate(shard.ShardId!, shardId, time.GetElapsedTime(ended));
                    }

                    LaunchChild(shard, span);
                }
            }

            firstChild ??= started.Count > 0 ? time.GetUtcNow() : null;
            if (status is not (StreamStatus.Enabled or StreamStatus.Enabling) || (firstChild is { } first && time.GetUtcNow() - first >= MoreChildrenWindow))
            {
                if (read)
                {
                    _core.LogShardFinished(shardId, started.Count);
                }

                return;
            }

            var delay = firstChild is null ? FirstChildLookDelay : MoreChildrenLookDelay;
            await Task.Delay(_request.EmptyPageDelay > delay ? _request.EmptyPageDelay : delay, time, stopped).ConfigureAwait(false);
        }
    }

    // Every page; a shard without an id is no shard to read. The status is the one of the last page. A page that fails transiently is asked for again through the run.
    private async Task<(List<Shard> Shards, StreamStatus Status)> ShardsAsync(ShardFilter? filter, ShardRun? run)
    {
        var shards = new List<Shard>();
        var status = StreamStatus.Unknown;
        string? start = null;
        do
        {
            var request = new DescribeStreamRequest { StreamArn = _request.StreamArn, ShardFilter = filter, ExclusiveStartShardId = start };
            var page = (await DescribeAsync(request, run).ConfigureAwait(false)).StreamDescription;
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

    private async Task<DescribeStreamResponse> DescribeAsync(DescribeStreamRequest request, ShardRun? run)
    {
        while (true)
        {
            try
            {
                var response = await _client.DescribeStreamAsync(request, _stop.Token).ConfigureAwait(false);
                run?.Progressed();
                return response;
            }
            catch (Exception failure) when (run is not null && run.CanRetry(failure))
            {
                await run.WaitAsync(failure, _stop.Token).ConfigureAwait(false);
            }
        }
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
