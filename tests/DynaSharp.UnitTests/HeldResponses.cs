using DynaSharp.Testing;

namespace DynaSharp.UnitTests;

/// <summary>
/// Responses for a test that a step of a call does not resume on the context of its caller, see <see cref="CountingSynchronizationContext"/>: each request takes the next
/// response at once, except the one at the held position, which waits for <see cref="Release"/>. Everything before it completes without waiting, so the await of that
/// request is the first that suspends. Requests after the last response take the last one again.
/// </summary>
internal sealed class HeldResponses
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StubResponse[] _responses;
    private readonly int _held;
    private int _taken;

    /// <summary>Initializes a new instance of the <see cref="HeldResponses"/> class.</summary>
    /// <param name="held">The position of the request that waits, from 0.</param>
    /// <param name="responses">The responses, in the order of the requests.</param>
    public HeldResponses(int held, params StubResponse[] responses)
    {
        _held = held;
        _responses = responses;
    }

    /// <summary>Answers a request, for <c>new StubHandler(held.AnswerAsync)</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the wait of the held request.</param>
    /// <returns>The response.</returns>
    public async Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
    {
        var position = Interlocked.Increment(ref _taken) - 1;
        if (position == _held)
        {
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return _responses[Math.Min(position, _responses.Length - 1)];
    }

    /// <summary>Lets the held request take its response.</summary>
    public void Release() => _release.SetResult();
}
