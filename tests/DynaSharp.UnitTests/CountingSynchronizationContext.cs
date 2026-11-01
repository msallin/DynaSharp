namespace DynaSharp.UnitTests;

/// <summary>
/// A synchronization context for tests of library code, which must never resume on the context of its caller (a caller that waits
/// for the call with <c>.Result</c> on a UI thread would deadlock). It counts how often a continuation is posted to it and runs the
/// continuation in place.
/// </summary>
/// <remarks>
/// An <c>await</c> captures the context that is current when it suspends. So a test starts the operation under this context with
/// <see cref="Start"/>, which makes the context current only until the call returns, lets everything that the operation does before
/// its first pending <c>await</c> run, and then completes whatever the operation waits for. If the library awaits that without
/// <c>ConfigureAwait(false)</c>, the continuation is posted here and <see cref="Posts"/> says so. To check a later <c>await</c>,
/// everything before it has to complete without waiting.
/// </remarks>
internal sealed class CountingSynchronizationContext : SynchronizationContext
{
    private int _posts;

    /// <summary>Gets how many continuations were posted to the context.</summary>
    public int Posts => Volatile.Read(ref _posts);

    /// <summary>Starts an operation with this context as the current one and returns its task.</summary>
    /// <typeparam name="T">The result of the operation.</typeparam>
    /// <param name="start">Starts the operation. The context is current while it runs, and not any more when it returns.</param>
    /// <returns>The task of the operation.</returns>
    public Task<T> Start<T>(Func<Task<T>> start)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            return start();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    /// <summary>Starts an operation with this context as the current one and returns its task.</summary>
    /// <param name="start">Starts the operation. The context is current while it runs, and not any more when it returns.</param>
    /// <returns>The task of the operation.</returns>
    public Task Start(Func<Task> start)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            return start();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        Interlocked.Increment(ref _posts);
        d(state);
    }
}
