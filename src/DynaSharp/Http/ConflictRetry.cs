using DynaSharp.Retries;

namespace DynaSharp.Http;

/// <summary>
/// The loop behind <see cref="DynamoDbClient.RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/>, whose remarks are its contract. It sits above the
/// pipeline and shares nothing with its retries: one run of the delegate is any number of calls, each with its own attempts, and this loop only sees what comes out of a run.
/// </summary>
internal static class ConflictRetry
{
    /// <summary>The number of runs of a conflict retry, the first included, unless the caller says otherwise.</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>Runs the delegate until it gets through, fails with something else or has used up its runs.</summary>
    /// <typeparam name="T">What the delegate returns.</typeparam>
    /// <param name="core">The client: its clock, its jitter and its telemetry.</param>
    /// <param name="operation">The delegate.</param>
    /// <param name="maxAttempts">The number of runs, at least 1.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait.</param>
    /// <returns>What the last run returned.</returns>
    public static async Task<T> RunAsync<T>(ClientCore core, Func<CancellationToken, ValueTask<T>> operation, int maxAttempts, CancellationToken cancellationToken)
    {
        var run = 1;
        while (true)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (DynamoDbException conflict) when (FailureClassifier.IsConflict(conflict))
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
