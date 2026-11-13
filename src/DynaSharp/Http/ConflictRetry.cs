using DynaSharp.Retries;

namespace DynaSharp.Http;

/// <summary>
/// The loop behind <see cref="Table{T}.RetryOnConflictAsync{TResult}(Func{CancellationToken, ValueTask{TResult}}, CancellationToken, ConflictKind, int, Action{int, DynamoDbException})"/> and the methods of that name on a table, whose remarks are its contract. It sits above the
/// pipeline and shares nothing with its retries: one run of the delegate is any number of calls, each with its own attempts, and this loop only sees what comes out of a run.
/// </summary>
internal static class ConflictRetry
{
    /// <summary>The number of runs of a conflict retry, the first included, unless the caller says otherwise.</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>Runs a read-modify-write, which is what the methods of that name on a table come to. What is wrong with the call throws here, at the call, which an asynchronous method could not do.</summary>
    /// <typeparam name="T">What a run gives.</typeparam>
    /// <typeparam name="TOperation">The delegate of the caller, in any of its shapes.</typeparam>
    /// <param name="core">The client: its clock, its jitter and its telemetry.</param>
    /// <param name="invoke">Runs the delegate with the number of the run and the token, a static lambda, so that no shape costs a closure.</param>
    /// <param name="operation">The delegate.</param>
    /// <param name="retryOn">The conflicts that run it again.</param>
    /// <param name="maxAttempts">The number of runs.</param>
    /// <param name="onConflict">Hears of each run that lost and is followed by another, before the wait, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait.</param>
    /// <returns>What the last run returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> names no conflict or something else, or <paramref name="maxAttempts"/> is below 1.</exception>
    public static Task<T> StartAsync<T, TOperation>(
        ClientCore core,
        Func<TOperation, int, CancellationToken, ValueTask<T>> invoke,
        TOperation operation,
        ConflictKind retryOn,
        int maxAttempts,
        Action<int, DynamoDbException>? onConflict,
        CancellationToken cancellationToken)
        where TOperation : Delegate
    {
        ArgumentNullException.ThrowIfNull(operation);
        Check(retryOn, maxAttempts);
        return RunAsync(core, invoke, operation, retryOn, maxAttempts, onConflict, cancellationToken);
    }

    private static void Check(ConflictKind retryOn, int maxAttempts)
    {
        if (retryOn is not (ConflictKind.ConditionalCheckFailed or ConflictKind.TransactionConflict or ConflictKind.Any))
        {
            throw new ArgumentOutOfRangeException(nameof(retryOn), retryOn, "Name the conflicts that run the delegate again: ConditionalCheckFailed, TransactionConflict or Any.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
    }

    // The delegate of the caller goes in as state, so that a run of any shape costs no closure of its own.
    private static async Task<T> RunAsync<T, TOperation>(
        ClientCore core,
        Func<TOperation, int, CancellationToken, ValueTask<T>> invoke,
        TOperation operation,
        ConflictKind retryOn,
        int maxAttempts,
        Action<int, DynamoDbException>? onConflict,
        CancellationToken cancellationToken)
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
                    conflict.ConflictAttempts = run;

                    // One run asked for no retry, so there is nothing that gave up, as with MaxAttempts on a call.
                    if (maxAttempts > 1)
                    {
                        core.ReportConflictsExhausted(conflict, run);
                    }

                    throw;
                }

                core.CountConflictRetry(conflict);

                // Inside the catch, so that what the hook throws ends the loop in place of the conflict.
                onConflict?.Invoke(run, conflict);
            }

            await core.ConflictBackoffAsync(run, cancellationToken).ConfigureAwait(false);
            run++;
        }
    }
}
