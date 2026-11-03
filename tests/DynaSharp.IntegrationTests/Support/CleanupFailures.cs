using System.Collections.Concurrent;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>
/// Keeps the failure of a test the one it reports when its cleanup fails as well. A fixture under <c>await using</c> cannot see whether the body threw, and an exception from its
/// <c>DisposeAsync</c> replaces the one of the body: a table that could not be deleted then hides why the test failed. So <see cref="Sandbox"/> hands what its cleanup could not do
/// here, and the hook after each test decides: a test that failed keeps its exception, with the cleanup failures in its error output, and a test that passed fails with them.
/// </summary>
internal static class CleanupFailures
{
    private const string Key = "DynaSharp.IntegrationTests.CleanupFailures";

    /// <summary>Records what the cleanup of the running test could not do. Outside a test there is nothing to keep primary, so it throws at once.</summary>
    /// <param name="failures">The failures, at least one.</param>
    /// <exception cref="AggregateException">There is no running test.</exception>
    public static void Report(IReadOnlyList<Exception> failures)
    {
        if (TestContext.Current is not { } context)
        {
            throw new AggregateException(failures);
        }

        var recorded = context.StateBag.GetOrAdd(Key, static _ => new ConcurrentQueue<Exception>());
        foreach (var failure in failures)
        {
            recorded.Enqueue(failure);
        }
    }

    /// <summary>Fails a test that passed when its cleanup failed, and writes the cleanup failures of a test that failed to its error output.</summary>
    /// <param name="context">The test that ended.</param>
    /// <exception cref="AggregateException">The test passed and its cleanup did not.</exception>
    [AfterEvery(HookType.Test)]
    public static void AfterEveryTest(TestContext context)
    {
        if (!context.StateBag.TryGetValue<ConcurrentQueue<Exception>>(Key, out var recorded) || recorded.IsEmpty)
        {
            return;
        }

        var failures = recorded.ToArray();
        if (ToThrow(context.Execution.Result?.Exception, failures) is { } failure)
        {
            throw failure;
        }

        foreach (var cleanup in failures)
        {
            context.Output.WriteError($"The cleanup failed as well, after the failure of the test: {cleanup}");
        }
    }

    /// <summary>Decides what a test with failed cleanup throws.</summary>
    /// <param name="testFailure">The exception of the test body, or <see langword="null"/> if it passed.</param>
    /// <param name="cleanupFailures">What the cleanup could not do.</param>
    /// <returns>The cleanup failures together for a test that passed, or <see langword="null"/> for one that failed, which keeps its own exception.</returns>
    internal static AggregateException? ToThrow(Exception? testFailure, IReadOnlyList<Exception> cleanupFailures) =>
        testFailure is null && cleanupFailures.Count > 0 ? new AggregateException("The test passed, and its cleanup failed.", cleanupFailures) : null;
}
