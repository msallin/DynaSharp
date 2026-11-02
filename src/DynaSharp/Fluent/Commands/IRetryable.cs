namespace DynaSharp;

/// <summary>A command that takes per-call options of the retry loop, for a helper that works on all of them, see <see cref="IConditional{TSelf}"/>.</summary>
/// <typeparam name="TSelf">The command.</typeparam>
public interface IRetryable<TSelf>
    where TSelf : IRetryable<TSelf>, allows ref struct
{
    /// <summary>
    /// Sets the number of attempts of this call including the first, in place of <see cref="DynamoDbClientOptions.MaxAttempts"/>. A retry after a
    /// lost response repeats a write that may have been applied, so <c>1</c> makes an <c>ADD</c> count at most once, and a lost response then reports a failure after the write was applied.
    /// </summary>
    /// <param name="attempts">The number of attempts.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts"/> is below 1.</exception>
    public TSelf MaxAttempts(int attempts);
}
