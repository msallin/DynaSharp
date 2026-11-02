using System.Runtime.ExceptionServices;

namespace DynaSharp.Fluent;

/// <summary>
/// Runs a number of reads with a limit on how many run at once. The first failure stops the rest and is thrown once, as the exception it is; a cancellation by the caller throws
/// the caller's token. Whatever happens, no task outlives the call, so nothing can fail unobserved and every state is out of use when it returns.
/// </summary>
internal sealed class BoundedReads
{
    private readonly int _count;
    private readonly Func<int, CancellationToken, ValueTask> _read;
    private readonly CancellationTokenSource _stop;
    private Exception? _failure;
    private int _next = -1;

    private BoundedReads(int count, Func<int, CancellationToken, ValueTask> read, CancellationTokenSource stop)
    {
        _count = count;
        _read = read;
        _stop = stop;
    }

    /// <summary>Runs the reads.</summary>
    /// <param name="count">The number of reads.</param>
    /// <param name="concurrency">The most reads at once.</param>
    /// <param name="read">One read, by its number. It must be able to run beside the others.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A task that the caller awaits exactly once.</returns>
    public static async ValueTask RunAsync(int count, int concurrency, Func<int, CancellationToken, ValueTask> read, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var run = new BoundedReads(count, read, stop);
        var workers = new Task[Math.Min(count, concurrency)];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = run.WorkAsync();
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        if (run._failure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        // Workers that were stopped end quietly, and from here that looks like the end of the work.
        cancellationToken.ThrowIfCancellationRequested();
    }

    // Never fails: the failure is kept for RunAsync, so waiting for the workers cannot throw.
    private async Task WorkAsync()
    {
        var stopped = _stop.Token;
        try
        {
            int index;
            while (!stopped.IsCancellationRequested && (index = Interlocked.Increment(ref _next)) < _count)
            {
                await _read(index, stopped).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            // Stopped by the caller or by another worker, which has kept why.
        }
#pragma warning disable CA1031 // The failure is not swallowed: RunAsync throws it.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            Interlocked.CompareExchange(ref _failure, failure, null);
            await _stop.CancelAsync().ConfigureAwait(false);
        }
    }
}
