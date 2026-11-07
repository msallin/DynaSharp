using System.Runtime.CompilerServices;
using System.Text;
using DynaSharp.Retries;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Sends a batch: cuts it into requests DynamoDB accepts, and sends again what comes back unprocessed until nothing is left or nothing moves any
/// more. Each request goes through the pipeline as a call of its own, with the retries of a call. A batch that makes no progress waits, with the
/// backoff of the retry policy and on the clock of the client, and gives up after a bounded number of rounds. A failure after a request went through ends
/// it too, with what was done in the exception, and so does a failure of the first request that the pipeline retries.
/// </summary>
/// <remarks>
/// <para>
/// DynamoDB answers a batch with what it did not get to, in the form the request had, so that JSON goes straight back into the next request: no
/// action is parsed unless the batch gives up. Requests run one after the other: a request is not started before the one before it is done, unprocessed
/// actions included. That leaves capacity to whatever else the table serves, and there is no buffer that two requests share.
/// </para>
/// <para>
/// The bound counts rounds in a row without progress, not rounds, because a round that gets something done is not stuck: a BatchGetItem whose items
/// exceed the 16 MB of one response takes as many rounds as it needs. Every round with progress takes at least one action, so the number of requests
/// is bounded too.
/// </para>
/// </remarks>
internal static class BatchDriver
{
    /// <summary>The number of requests in a row without progress after which a batch gives up, unless <c>MaxStalledRounds</c> sets its own. With the jitter of the policy they wait 6 s on average.</summary>
    public const int MaxStalledRounds = 8;

    /// <summary>Sends a batch. The state belongs to this method from here on, whatever happens.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TResult">What the batch returns.</typeparam>
    /// <param name="state">The state, entered by the caller.</param>
    /// <param name="cancellationToken">Cancels the batch, also while it waits.</param>
    /// <returns>The result, a task that the caller awaits exactly once.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<TResult> SendAsync<TOperation, TResult>(CommandState state, CancellationToken cancellationToken)
        where TOperation : IBatchOperation<TResult>
    {
        var version = state.Version;
        try
        {
            var actions = state.Actions;
            actions.Seal(state.Body.WrittenCount);
            var maxStalled = state.MaxStalledRounds > 0 ? state.MaxStalledRounds : MaxStalledRounds;
            var totals = default(BatchTotals);
            for (actions.First = 0; actions.First < actions.Count; actions.First = actions.End)
            {
                actions.End = TOperation.FindEnd(actions);
                var sent = actions.End - actions.First;
                var stalled = 0;
                while (true)
                {
                    BatchResponse response;
                    try
                    {
                        response = await state.SendActionsAsync<TOperation, BatchResponse>(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (EndsIncomplete(exception, totals.Requests))
                    {
                        if (actions.ReadFailed)
                        {
                            totals.AddUnread();
                        }

                        // The call has used up its attempts by now, so this is the end of the batch, and the caller gets to see what was done before it.
                        throw GiveUp<TOperation, TResult>(state, totals, exception);
                    }

                    totals.Add(response, sent);
                    if (response.Unprocessed == 0)
                    {
                        break;
                    }

                    stalled = response.Unprocessed < sent ? 0 : stalled + 1;
                    sent = response.Unprocessed;
                    if (stalled == maxStalled)
                    {
                        throw GiveUp<TOperation, TResult>(state, totals, null);
                    }

                    await state.Table.Core.BackoffAsync(stalled + 1, cancellationToken).ConfigureAwait(false);
                }
            }

            return TOperation.CreateResult(state, totals);
        }
        finally
        {
            state.Release(version);
        }
    }

    /// <summary>Writes the body of a request: the actions in <see cref="ActionList.First"/> to <see cref="ActionList.End"/>, or what the last request left of them.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TResult">What the batch returns.</typeparam>
    /// <param name="state">The state of the command.</param>
    /// <param name="request">Where the request goes.</param>
    /// <returns>The body.</returns>
    public static ArraySegment<byte> WriteBody<TOperation, TResult>(CommandState state, PooledByteBuffer request)
        where TOperation : IBatchOperation<TResult>
    {
        var actions = state.Actions;
        request.Append("{\"RequestItems\":"u8);
        if (actions.Carry.WrittenCount > 0)
        {
            request.Append(actions.Carry.WrittenSpan);
        }
        else
        {
            TOperation.WriteRequestItems(state, request, actions.First, actions.End);
        }

        state.WriteRequestOptions(request);
        request.Append((byte)'}');
        return request.WrittenSegment;
    }

    /// <summary>Renders the first request of a batch and gives the state back.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TResult">What the batch returns.</typeparam>
    /// <param name="state">The state, entered by the caller.</param>
    /// <returns>The body.</returns>
    public static string RenderJson<TOperation, TResult>(CommandState state)
        where TOperation : IBatchOperation<TResult>
    {
        var version = state.Version;
        try
        {
            var actions = state.Actions;
            actions.Seal(state.Body.WrittenCount);
            actions.End = TOperation.FindEnd(actions);
            using var request = new PooledByteBuffer();
            return Encoding.UTF8.GetString(WriteBody<TOperation, TResult>(state, request));
        }
        finally
        {
            state.Release(version);
        }
    }

    // A permanent failure of the first request, such as a rejected request or a missing table, applied none of it and stays as it is. Anything the pipeline retries can have
    // been applied in part, a timeout or a lost connection, or says that DynamoDB took none of it, a throttle: the whole batch is then unprocessed and safe to send again.
    // A caller who cancels has stopped listening, and a cancellation stays one.
    private static bool EndsIncomplete(Exception exception, int answered) =>
        exception is not OperationCanceledException && (answered > 0 || FailureClassifier.Classify(exception) != FailureKind.Permanent);

    private static Exception GiveUp<TOperation, TResult>(CommandState state, BatchTotals totals, Exception? cause)
        where TOperation : IBatchOperation<TResult>
    {
        var actions = state.Actions;
        state.Table.Core.LogBatchIncomplete(TOperation.Name, actions.OnlyTableName, actions.Count - totals.Processed, totals.Requests);
        return TOperation.CreateIncomplete(state, totals, actions.Carry.WrittenCount > 0 ? actions.End : actions.First, cause);
    }
}
