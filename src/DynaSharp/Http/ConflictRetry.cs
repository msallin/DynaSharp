using DynaSharp.Retries;

namespace DynaSharp.Http;

/// <summary>
/// The loop behind <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> and the methods of that name on a table, whose remarks are its contract. It sits above the
/// pipeline and shares nothing with its retries: one run of the delegate is any number of calls, each with its own attempts, and this loop only sees what comes out of a run.
/// </summary>
internal static class ConflictRetry
{
    /// <summary>The number of runs of a conflict retry, the first included, unless the caller says otherwise.</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>Runs a read-modify-write, which is what the methods of that name on a client and on a table come to. What is wrong with the call throws here, at the call, which an asynchronous method could not do.</summary>
    /// <typeparam name="T">What the delegate returns.</typeparam>
    /// <param name="core">The client: its clock, its jitter and its telemetry.</param>
    /// <param name="operation">The delegate.</param>
    /// <param name="retryOn">The conflicts that run it again.</param>
    /// <param name="maxAttempts">The number of runs.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait.</param>
    /// <returns>What the last run returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> names no conflict or something else, or <paramref name="maxAttempts"/> is below 1.</exception>
    public static Task<T> StartAsync<T>(ClientCore core, Func<CancellationToken, ValueTask<T>> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Check(retryOn, maxAttempts);
        return RunAsync(core, static (operation, _, token) => operation(token), operation, retryOn, maxAttempts, cancellationToken);
    }

    /// <summary>Runs a read-modify-write whose delegate takes the number of the run, 1 for the first, as <see cref="StartAsync{T}(ClientCore, Func{CancellationToken, ValueTask{T}}, ConflictKind, int, CancellationToken)"/> does.</summary>
    /// <typeparam name="T">What the delegate returns.</typeparam>
    /// <param name="core">The client: its clock, its jitter and its telemetry.</param>
    /// <param name="operation">The delegate, with the number of the run.</param>
    /// <param name="retryOn">The conflicts that run it again.</param>
    /// <param name="maxAttempts">The number of runs.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait.</param>
    /// <returns>What the last run returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> names no conflict or something else, or <paramref name="maxAttempts"/> is below 1.</exception>
    public static Task<T> StartAsync<T>(ClientCore core, Func<int, CancellationToken, ValueTask<T>> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Check(retryOn, maxAttempts);
        return RunAsync(core, static (operation, run, token) => operation(run, token), operation, retryOn, maxAttempts, cancellationToken);
    }

    /// <summary>Runs a read-modify-write that returns nothing, as <see cref="StartAsync{T}(ClientCore, Func{CancellationToken, ValueTask{T}}, ConflictKind, int, CancellationToken)"/> does.</summary>
    /// <param name="core">The client: its clock, its jitter and its telemetry.</param>
    /// <param name="operation">The delegate.</param>
    /// <param name="retryOn">The conflicts that run it again.</param>
    /// <param name="maxAttempts">The number of runs.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> names no conflict or something else, or <paramref name="maxAttempts"/> is below 1.</exception>
    public static Task StartAsync(ClientCore core, Func<CancellationToken, ValueTask> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return StartAsync(
            core,
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            },
            retryOn,
            maxAttempts,
            cancellationToken);
    }

    /// <summary>Runs a read-modify-write that returns nothing and takes the number of the run, as <see cref="StartAsync{T}(ClientCore, Func{int, CancellationToken, ValueTask{T}}, ConflictKind, int, CancellationToken)"/> does.</summary>
    /// <param name="core">The client: its clock, its jitter and its telemetry.</param>
    /// <param name="operation">The delegate, with the number of the run.</param>
    /// <param name="retryOn">The conflicts that run it again.</param>
    /// <param name="maxAttempts">The number of runs.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> names no conflict or something else, or <paramref name="maxAttempts"/> is below 1.</exception>
    public static Task StartAsync(ClientCore core, Func<int, CancellationToken, ValueTask> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return StartAsync(
            core,
            async (run, token) =>
            {
                await operation(run, token).ConfigureAwait(false);
                return true;
            },
            retryOn,
            maxAttempts,
            cancellationToken);
    }

    private static void Check(ConflictKind retryOn, int maxAttempts)
    {
        if (retryOn is not (ConflictKind.ConditionalCheckFailed or ConflictKind.TransactionConflict or ConflictKind.Any))
        {
            throw new ArgumentOutOfRangeException(nameof(retryOn), retryOn, "Name the conflicts that run the delegate again: ConditionalCheckFailed, TransactionConflict or Any.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
    }

    // The delegate of the caller goes in as state, so that a run of either shape costs no closure of its own.
    private static async Task<T> RunAsync<T, TOperation>(
        ClientCore core, Func<TOperation, int, CancellationToken, ValueTask<T>> invoke, TOperation operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken)
    {
        var run = 1;
        while (true)
        {
            try
            {
                return await invoke(operation, run, cancellationToken).ConfigureAwait(false);
            }
            catch (DynamoDbException conflict) when (FailureClassifier.IsConflict(conflict, retryOn))
            {
                if (run == maxAttempts)
                {
                    // One run asked for no retry, so there is nothing that gave up, as with MaxAttempts on a call.
                    if (maxAttempts > 1)
                    {
                        core.ReportConflictsExhausted(conflict, run);
                    }

                    throw;
                }

                core.CountConflictRetry(conflict);
            }

            await core.ConflictBackoffAsync(run, cancellationToken).ConfigureAwait(false);
            run++;
        }
    }
}
