using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DynaSharp.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// Holds the temporary credentials of a remote source (container endpoint, instance metadata, web identity) and refreshes them before they
/// expire, with the windows of botocore's <c>RefreshableCredentials</c>.
/// </summary>
/// <remarks>
/// <para>
/// The same <see cref="AwsCredentials"/> instance is returned until a refresh produces new ones, because the signer caches
/// its derived key per instance. With 15 minutes or more left, a call costs one clock read.
/// </para>
/// <para>
/// With less than 15 minutes left (advisory period) a fetch starts in the background and every caller keeps using the current
/// credentials, so a slow or failing source never delays a request. If the fetch fails, a warning is logged and the next one
/// starts after <see cref="AdvisoryRetryInterval"/>; the same pause follows a fetch that succeeds, for credentials that are in the advisory period
/// as they arrive. botocore makes one caller wait and tries again on every call, which costs
/// seconds per request during an outage of the source. With less than 10 minutes left (mandatory period) every caller waits for
/// the fetch, and a failure is thrown.
/// </para>
/// <para>
/// There is at most one fetch at a time (single flight), also across the first load. It runs on its own, independent of the
/// cancellation tokens of the callers, so a caller that gives up never cancels the fetch for the others; it only stops waiting.
/// No lock is held while a fetch runs, and no thread is blocked.
/// </para>
/// <para>
/// The windows trust the expiration, which can be wrong: the source may renew late, or a session may be revoked. <see cref="Drop"/> is
/// for the service saying so. It forgets the credentials, which makes every caller wait for the next fetch, as with none at all.
/// It compares instances, because that is what tells apart the credentials a request used from their replacement: requests that
/// fail one after the other with the same credentials drop them once, so together they cause one fetch.
/// </para>
/// </remarks>
internal sealed class RefreshingCredentialsCache
{
    /// <summary>Gets the time before expiry from which a fetch runs in the background while every caller keeps using the current credentials.</summary>
    public static readonly TimeSpan AdvisoryWindow = TimeSpan.FromMinutes(15);

    /// <summary>Gets the time before expiry in which every caller waits for a refresh.</summary>
    public static readonly TimeSpan MandatoryWindow = TimeSpan.FromMinutes(10);

    /// <summary>Gets the pause after a failed fetch in the advisory period.</summary>
    public static readonly TimeSpan AdvisoryRetryInterval = TimeSpan.FromSeconds(30);

    private readonly string _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Func<ValueTask<CredentialsOutcome>> _fetch;
    private readonly Lock _gate = new();

    // Written under _gate, read without it on the fast path.
    private AwsCredentials? _current;
    private TaskCompletionSource<FetchResult>? _flight;
    private DateTimeOffset _nextAdvisoryFetch;
    private bool _everFetched;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshingCredentialsCache"/> class.
    /// </summary>
    /// <param name="source">Names the source in logs, for example <c>container credentials endpoint</c>.</param>
    /// <param name="timeProvider">
    /// The clock expiry is judged on. For a provider that a client created this is the clock of the client, corrected from the responses of the
    /// service: expirations are times of the service, so on a local clock that is behind, the windows below would open too late.
    /// </param>
    /// <param name="logger">Receives a warning for a failed refresh.</param>
    /// <param name="fetch">
    /// Fetches new credentials. It runs without a cancellation token and must bound its own duration. A broken source throws
    /// <see cref="CredentialsUnavailableException"/> or answers <see cref="CredentialsOutcome.Broken"/>. An answer of
    /// <see cref="CredentialsOutcome.DoesNotApply"/> lets the default chain move on, which only the last source of the chain can afford.
    /// </param>
    public RefreshingCredentialsCache(string source, TimeProvider timeProvider, ILogger logger, Func<ValueTask<CredentialsOutcome>> fetch)
    {
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
        _fetch = fetch;
    }

    /// <summary>Gets credentials that are valid for long enough, fetching them if needed.</summary>
    /// <param name="cancellationToken">Stops this caller from waiting. It does not cancel the fetch.</param>
    /// <returns>The credentials, or why the source has none (only possible while no credentials are held: before the first fetch, or after <see cref="Drop"/>).</returns>
    /// <exception cref="CredentialsUnavailableException">A fetch failed and the credentials are in the mandatory refresh period or missing.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<CredentialsOutcome> GetAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _current);
        return current is not null && !ExpiresWithin(current, _timeProvider.GetUtcNow(), AdvisoryWindow)
            ? ValueTask.FromResult(CredentialsOutcome.Found(current))
            : GetSlowAsync(cancellationToken);
    }

    /// <summary>Forgets credentials the service rejected, whatever their expiration says. Nothing is fetched here: that is the next <see cref="GetAsync"/>.</summary>
    /// <param name="rejected">The instance that was rejected. If it is not the current one it was replaced already, and nothing happens.</param>
    public void Drop(AwsCredentials rejected)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_current, rejected))
            {
                return;
            }

            _current = null;
        }

        CredentialsLog.ExpiredCredentialsDropped(_logger, _source);
    }

    private static bool ExpiresWithin(AwsCredentials credentials, DateTimeOffset now, TimeSpan window) =>
        credentials.Expiration is { } expiration && expiration - now < window;

    private async ValueTask<CredentialsOutcome> GetSlowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        AwsCredentials? current;
        bool mandatory;
        bool startsFlight;
        TaskCompletionSource<FetchResult> flight;
        lock (_gate)
        {
            current = _current;
            if (current is not null && !ExpiresWithin(current, now, AdvisoryWindow))
            {
                // Another caller finished a refresh after this one looked.
                return CredentialsOutcome.Found(current);
            }

            mandatory = current is null || ExpiresWithin(current, now, MandatoryWindow);
            if (!mandatory && now < _nextAdvisoryFetch)
            {
                return CredentialsOutcome.Found(current!);
            }

            startsFlight = _flight is null;
            _flight ??= new TaskCompletionSource<FetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            flight = _flight;
        }

        if (startsFlight)
        {
            // Started outside the lock. RunFlightAsync completes the flight in every case, so waiting for it cannot hang.
            _ = RunFlightAsync(flight);
        }

        if (!mandatory)
        {
            return CredentialsOutcome.Found(current!);
        }

        var result = await flight.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            ExceptionDispatchInfo.Throw(error);
        }

        return current is null || result.Outcome.Credentials is not null
            ? result.Outcome
            : throw new CredentialsUnavailableException($"Refreshing the credentials from the {_source} failed: {result.Outcome.Reason}.");
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Whatever the fetch throws is handed to the callers that wait for it, and the flight must complete in any case.")]
    private async Task RunFlightAsync(TaskCompletionSource<FetchResult> flight)
    {
        FetchResult result;
        try
        {
            result = Accept(await _fetch().ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            result = new FetchResult(default, exception);
        }

        try
        {
            var fresh = result.Outcome.Credentials;
            bool hadCredentials;
            bool mandatory;
            lock (_gate)
            {
                // The period is judged now and not when the fetch started: a fetch of the advisory period that ends in the mandatory one
                // has callers waiting for it, and they get the failure.
                var now = _timeProvider.GetUtcNow();
                var current = _current;
                hadCredentials = _everFetched;
                mandatory = current is null || ExpiresWithin(current, now, MandatoryWindow);
                if (fresh is not null)
                {
                    // Credentials that are in the advisory period as they arrive, such as a session of the 15 minutes that AssumeRole allows at
                    // least, would otherwise start the next fetch with the next call, and keep a fetch running for as long as there are calls.
                    _current = fresh;
                    _everFetched = true;
                    _nextAdvisoryFetch = now + AdvisoryRetryInterval;
                }
                else if (!mandatory)
                {
                    _nextAdvisoryFetch = now + AdvisoryRetryInterval;
                }

                _flight = null;
            }

            // Failing to get the very first credentials is reported to the caller, not logged.
            if (hadCredentials && fresh is null)
            {
                LogFailure(result, mandatory);
            }
        }
        finally
        {
            flight.SetResult(result);
        }
    }

    // A source that hands out credentials that are already expired has a clock problem, or is stuck. It does apply, so the chain must not move on.
    private FetchResult Accept(CredentialsOutcome outcome) =>
        outcome.Credentials?.Expiration is { } expiration && expiration < _timeProvider.GetUtcNow()
            ? new FetchResult(CredentialsOutcome.Broken("it returned credentials that are already expired (check the system clock)"), null)
            : new FetchResult(outcome, null);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A logger that throws must not fail the flight: nobody observes it, and the callers that wait for it need its result.")]
    private void LogFailure(FetchResult result, bool mandatory)
    {
        try
        {
            // The reasons are sentences without the final period; the message of an exception may have one.
            var reason = (result.Error?.Message ?? result.Outcome.Reason!).TrimEnd('.');
            if (mandatory)
            {
                CredentialsLog.MandatoryRefreshFailed(_logger, result.Error, _source, reason);
            }
            else
            {
                CredentialsLog.AdvisoryRefreshFailed(_logger, result.Error, _source, reason);
            }
        }
        catch (Exception)
        {
            // Nothing to do: the failure of the refresh is reported to the callers that wait for it, and this is only the log of it.
        }
    }

    // Outcome means nothing when Error is set.
    private sealed record FetchResult(CredentialsOutcome Outcome, Exception? Error);
}
