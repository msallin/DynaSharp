using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DynaSharp.Mapping;

namespace DynaSharp.Fluent;

/// <summary>
/// A scan in segments: each segment is a command of its own on a task of its own, which follows its pages and hands them over one by one. The handover holds
/// one page, so a segment waits while the consumer is behind: with n segments there are at most n requests at once and about 2n pages in memory.
/// </summary>
/// <remarks>
/// <para>
/// The first failure wins the completion of the channel, which is how it reaches the consumer exactly once, and stops the other segments. A segment that was
/// stopped is not a failure, and neither is one that the caller stopped.
/// </para>
/// <para>
/// However the enumeration ends, its <c>finally</c> stops what still runs and waits for it, so no task outlives the enumerator and every state is
/// back before the enumerator's <c>DisposeAsync</c> returns.
/// </para>
/// </remarks>
/// <typeparam name="T">How the items are read.</typeparam>
internal sealed class ParallelScan<T>
    where T : IDynamoDbDocument<T>
{
    private static readonly BoundedChannelOptions HandoverOptions = new(1) { SingleReader = true };

    private readonly Channel<T[]> _pages = Channel.CreateBounded<T[]>(HandoverOptions);
    private readonly CancellationTokenSource _stop;
    private readonly Task[] _segments;
    private int _running;

    private ParallelScan(CommandState scan, CancellationTokenSource stop)
    {
        _stop = stop;
        _running = scan.TotalSegments;
        _segments = new Task[scan.TotalSegments];
    }

    /// <summary>Reads all items of all segments, on enumeration.</summary>
    /// <param name="scan">The state of the scan, with its options and <see cref="CommandState.TotalSegments"/>. The caller entered it. It only serves as the model of the segments.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The items.</returns>
    public static IAsyncEnumerable<T> Items(CommandState scan, CancellationToken cancellationToken) =>
        new OnceEnumerable<T>(scan, ScanPageOperation<T>.Name, ReadAsync(scan, cancellationToken));

    private static async IAsyncEnumerable<T> ReadAsync(CommandState scan, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var run = new ParallelScan<T>(scan, stop);
        run.Start(scan);
        try
        {
            await foreach (var items in run._pages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var item in items)
                {
                    yield return item;
                }
            }

            // Segments that were stopped end quietly, and from here that looks like the end of the scan.
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(run._segments).ConfigureAwait(false);
        }
    }

    private void Start(CommandState scan)
    {
        for (var segment = 0; segment < _segments.Length; segment++)
        {
            _segments[segment] = ReadSegmentAsync(scan.StartSegment(segment));
        }
    }

    // Never fails: what goes wrong goes through the channel, so waiting for the segments cannot throw.
    private async Task ReadSegmentAsync(CommandState segment)
    {
        var version = segment.Version;
        var stopped = _stop.Token;
        try
        {
            PageData<T> page;
            do
            {
                page = await PagedRead<ScanPageOperation<T>, T>.SendAsync(segment, stopped).ConfigureAwait(false);
                if (page.Items.Length > 0)
                {
                    await _pages.Writer.WriteAsync(page.Items, stopped).ConfigureAwait(false);
                }
            }
            while (page.HasMore);
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            // Stopped by the caller or by another segment, which has reported why.
        }
#pragma warning disable CA1031 // The failure is not swallowed: the consumer gets it from the channel.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            _pages.Writer.TryComplete(failure);
            await _stop.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            segment.Release(version);
            if (Interlocked.Decrement(ref _running) == 0)
            {
                _pages.Writer.TryComplete();
            }
        }
    }
}
