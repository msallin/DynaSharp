using DynaSharp.Http;
using DynaSharp.Retries;

namespace DynaSharp.Streams;

/// <summary>
/// What <see cref="StreamMerge"/> adds to the reading of one shard: the shard is read again after a transient failure, for a while, and it says when the shard first
/// holds a position. <see cref="DynamoDbStreamsClient.ReadShardAsync"/> reads without one, so a failure there surfaces at once. A shard is read by one task, one call
/// after the other, so nothing here is shared between threads.
/// </summary>
/// <remarks>
/// An outage starts with a transient failure and ends with the next call that brings the shard on: a page of <c>GetRecords</c> or a look for children. A new iterator
/// is no progress, since an iterator that keeps failing on its first page would otherwise restart the outage every 15 minutes, and the reading would never fail.
/// </remarks>
/// <param name="core">The core of the client, for its clock, its backoff, its telemetry and its log.</param>
/// <param name="shardId">The shard, which the log names.</param>
/// <param name="positioned">Called once when the shard first holds an iterator, or <see langword="null"/>.</param>
internal sealed class ShardRun(ClientCore core, string shardId, Action? positioned)
{
    /// <summary>
    /// How long a shard is tried again, from the failure that started an outage. It is the life of an iterator: within it the reader goes on with the iterator it
    /// holds, so even a shard that has handed out nothing yet loses nothing, and nothing the stream keeps for 24 hours is near its trim. What lasts longer is no
    /// blip, and the caller hears of it.
    /// </summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(15);

    private Action? _positioned = positioned;
    private long _outageStarted;
    private int _outageRetries;

    /// <summary>Gets how many times the shard was read again, over all its outages.</summary>
    public int Retries { get; private set; }

    /// <summary>Tells whether a failure is one the same call can pass later: <see cref="DynamoDbException.IsTransient"/>, or what the pipeline repeats of the others.</summary>
    /// <param name="failure">The failure of the last attempt of a call.</param>
    /// <returns><see langword="true"/> for a throttling or server error, a network failure and a timeout of an attempt.</returns>
    public static bool IsTransient(Exception failure) =>
        failure is DynamoDbException dynamoDb ? dynamoDb.IsTransient : FailureClassifier.Classify(failure) != FailureKind.Permanent;

    /// <summary>Says once that the shard holds an iterator, or that it has none to hold.</summary>
    public void Positioned()
    {
        var positioned = _positioned;
        _positioned = null;
        positioned?.Invoke();
    }

    /// <summary>Ends an outage, if there is one: a call brought the shard on.</summary>
    public void Progressed()
    {
        if (_outageRetries == 0)
        {
            return;
        }

        core.LogShardRecovered(shardId, core.TimeProvider.GetElapsedTime(_outageStarted), _outageRetries);
        _outageRetries = 0;
    }

    /// <summary>Tells whether the shard is tried again after a failure: one that is transient, within <see cref="RetryWindow"/> of the start of the outage.</summary>
    /// <param name="failure">The failure of the last attempt of a call.</param>
    /// <returns><see langword="true"/> to wait with <see cref="WaitAsync"/> and send the call again.</returns>
    public bool CanRetry(Exception failure) =>
        IsTransient(failure) && (_outageRetries == 0 || core.TimeProvider.GetElapsedTime(_outageStarted) < RetryWindow);

    /// <summary>Counts and, at the start of an outage, logs a retry, and waits the backoff before it.</summary>
    /// <param name="failure">The failure.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes when the call may be sent again.</returns>
    public Task WaitAsync(Exception failure, CancellationToken cancellationToken)
    {
        if (_outageRetries == 0)
        {
            _outageStarted = core.TimeProvider.GetTimestamp();
            core.LogShardRetrying(shardId, failure, RetryWindow);
        }

        _outageRetries++;
        Retries++;
        core.Telemetry.CountStreamRetry(failure);
        return core.ShardRetryBackoffAsync(_outageRetries, cancellationToken);
    }
}
