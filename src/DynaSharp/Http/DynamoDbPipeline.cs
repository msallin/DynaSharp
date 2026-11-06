using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Errors;
using DynaSharp.Operations;
using DynaSharp.Retries;
using DynaSharp.Serialization;
using DynaSharp.Signing;
using DynaSharp.Telemetry;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Http;

/// <summary>
/// Sends one logical call to DynamoDB: gets the request body once, hashes it once, signs and sends it with a per-attempt
/// timeout, and turns the response into a result or an exception. A failed attempt is repeated as the retry policy decides, with
/// a new request, signature, timeout and credentials lookup each time. There is one loop for every kind of call (see
/// <see cref="IWireCall{TResponse}"/>), and the pipeline is generic over the call, so there are no delegates or closures per call.
/// </summary>
internal sealed class DynamoDbPipeline : IDisposable
{
    /// <summary>
    /// Gets the most bytes a response body may have. The largest response DynamoDB sends is the 16 MB of a BatchGetItem, so this leaves
    /// room for growth but stops a body that does not end, or a lying <c>Content-Length</c>, from filling the memory.
    /// </summary>
    internal const int MaxResponseLength = 64 * 1024 * 1024;

    private const string UserAgentHeaderName = "User-Agent";
    private const string ContentTypeHeaderName = "Content-Type";

    // What the log messages of the retry events say for a call that addresses several tables, or none, as most of DynamoDB Streams.
    private const string NoSingleTable = "(no single table)";

    // What the telemetry of a conflict says for an exception that did not come from a response, which a delegate of a conflict retry can throw.
    private const string UnknownOperation = "(unknown operation)";

    // How many idle timeout sources the pipeline keeps. A source costs about 150 bytes and a timer; more calls than this in flight at
    // once simply make new ones.
    private const int TimeoutPoolCapacity = 64;

    // How many idle sets of buffers the pipeline keeps. A set holds no array, only three small objects.
    private const int BuffersPoolCapacity = 64;

    private readonly HttpClient _http;
    private readonly ICredentialsProvider _credentials;

    // The same provider if it has the capability, so that the failure path does not ask for it.
    private readonly IRefreshableCredentialsProvider? _refreshableCredentials;
    private readonly SigV4Signer _signer;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _attemptTimeout;
    private readonly Uri _endpoint;

    // What the signer needs of the endpoint, and what SigV4Signer.GetAuthority would build again on every attempt.
    private readonly string _authority;
    private readonly ArrayPool<byte> _pool;
    private readonly ILogger _logger;
    private readonly RetryPolicy _retryPolicy;
    private readonly ClockSkewCorrection _clock;
    private readonly int _maxResponseLength;
    private readonly BoundedPool<CancellationTokenSource> _timeouts = new(TimeoutPoolCapacity);
    private readonly BoundedPool<CallBuffers> _buffers = new(BuffersPoolCapacity);

    // The limit on the response body is a parameter so that tests can use a small one.
    public DynamoDbPipeline(
        ClientConfiguration configuration,
        HttpClient http,
        ArrayPool<byte> pool,
        ActivitySource activitySource,
        ILogger logger,
        Random random,
        int maxResponseLength)
    {
        _maxResponseLength = maxResponseLength;
        _http = http;
        _pool = pool;
        Telemetry = new OperationTelemetry(activitySource, configuration.TimeProvider, configuration.Region.Name, configuration.Endpoint);
        _logger = logger;
        _retryPolicy = new RetryPolicy(configuration.MaxAttempts, random);
        _clock = configuration.Clock;
        _credentials = configuration.Credentials;
        _refreshableCredentials = configuration.Credentials as IRefreshableCredentialsProvider;
        _timeProvider = configuration.TimeProvider;
        _attemptTimeout = configuration.AttemptTimeout;
        _endpoint = configuration.Endpoint;
        _authority = SigV4Signer.GetAuthority(_endpoint);
        _signer = new SigV4Signer(configuration.Region.Name, pool: pool);
    }

    /// <summary>Gets what the client reports about its operations, for the layers that report more than a call, such as the shards of a stream.</summary>
    public OperationTelemetry Telemetry { get; }

    /// <summary>Runs an operation of the request API.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TRequest">The request type of the operation.</typeparam>
    /// <typeparam name="TResponse">The response type of the operation.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The response.</returns>
    public Task<TResponse> ExecuteAsync<TOperation, TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
        where TOperation : IOperation<TRequest, TResponse> =>
        SendAsync<RequestCall<TOperation, TRequest, TResponse>, TResponse>(new RequestCall<TOperation, TRequest, TResponse>(request), cancellationToken).AsTask();

    /// <summary>
    /// Runs a call: the one retry loop of the pipeline, for every kind of call. The state machine comes from a pool, so the returned
    /// task must be awaited exactly once. The call is released after the last attempt, so a retry sends the same bytes again.
    /// </summary>
    /// <typeparam name="TCall">The struct that implements the call.</typeparam>
    /// <typeparam name="TResponse">The response type of the call.</typeparam>
    /// <param name="call">The call. The pipeline calls <see cref="IWireCall{TResponse}.Release"/> once when it is over.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response.</returns>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<TResponse> SendAsync<TCall, TResponse>(TCall call, CancellationToken cancellationToken)
        where TCall : struct, IWireCall<TResponse>
    {
        var tableName = call.GetTableName();
        var measure = DynaSharpTelemetry.OperationDuration.Enabled;
        var startTimestamp = measure ? _timeProvider.GetTimestamp() : 0;
        var activity = Telemetry.StartActivity<TCall, TResponse>(in call, tableName);
        string? errorType = null;
        string? responseStatusCode = null;
        var buffers = _buffers.Rent() ?? new CallBuffers(_pool);
        try
        {
            // The array of the request goes back to the pool when the call is over (see the finally block). The handler no longer reads
            // the content then: that is the contract of DynamoDbClientOptions.HttpHandler, and SocketsHttpHandler keeps it on HTTP/1.1.
            var prepared = Prepare(tableName, call.WriteBody(buffers));

            // The loop lives here, not in a method of its own, so that a call costs one async state machine less. A call that works
            // the first time does not do anything for retries beyond two integers and a flag.
            var attempt = 1;
            var lastRetryCost = 0;
            var credentialsResent = false;
            TResponse result;
            while (true)
            {
                RetryDecision decision;
                try
                {
                    result = await AttemptAsync<TCall, TResponse>(call, buffers, prepared, activity, cancellationToken).ConfigureAwait(false);
                    _retryPolicy.OnSuccess(lastRetryCost);
                    break;
                }
                catch (ClockCorrectedException corrected)
                {
                    decision = Decide(corrected.Rejection, resendAtOnce: true, call.OperationName, tableName, attempt, lastRetryCost, call.MaxAttempts);
                    if (!decision.ShouldRetry)
                    {
                        throw corrected.Rejection;
                    }
                }
                catch (CredentialsDroppedException dropped)
                {
                    // Once per call, so that a source that keeps handing out a stale identity cannot use up the attempts.
                    decision = Decide(dropped.Rejection, resendAtOnce: !credentialsResent, call.OperationName, tableName, attempt, lastRetryCost, call.MaxAttempts);
                    if (!decision.ShouldRetry)
                    {
                        throw dropped.Rejection;
                    }

                    credentialsResent = true;
                }
                catch (Exception exception)
                {
                    decision = Decide(exception, resendAtOnce: false, call.OperationName, tableName, attempt, lastRetryCost, call.MaxAttempts);
                    if (!decision.ShouldRetry)
                    {
                        throw;
                    }
                }

                lastRetryCost = decision.LastRetryCost;
                attempt++;
                await DelayAsync(decision.Delay, cancellationToken).ConfigureAwait(false);
            }

            // After the loop, not inside it: a failure here must not be mistaken for a failed attempt and retried.
            Telemetry.RecordResponse<TCall, TResponse>(activity, tableName, in call, result);
            return result;
        }
        catch (Exception exception)
        {
            if (exception is DynamoDbException failure)
            {
                call.DescribeFailure(failure);
            }

            errorType = OperationTelemetry.GetErrorType(exception);
            responseStatusCode = OperationTelemetry.GetResponseStatusCode(exception);
            OperationTelemetry.RecordFailure(activity, errorType, responseStatusCode);
            throw;
        }
        finally
        {
            if (measure)
            {
                Telemetry.RecordDuration(call.OperationName, tableName, startTimestamp, errorType, responseStatusCode);
            }

            activity?.Dispose();

            // Last, so that nothing above can still be using what is released: the buffers go back for the next call, the call hands
            // back what it holds.
            buffers.Release();
            _buffers.Return(buffers);
            call.Release();
        }
    }

    /// <summary>
    /// Waits before a batch sends what DynamoDB left unprocessed, with the backoff and the jitter of a throttled attempt and on the clock of the
    /// client. It is not a retry of a failed call, so it takes nothing from the retry quota.
    /// </summary>
    /// <param name="attempt">The number of the round that came back without progress, starting at 1.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    public Task BackoffAsync(int attempt, CancellationToken cancellationToken) =>
        DelayAsync(_retryPolicy.GetDelay(FailureKind.Throttling, attempt), cancellationToken);

    /// <summary>Logs that a batch gave up, the one retry event of a batch that no single call sees.</summary>
    /// <param name="operationName">The operation of the batch.</param>
    /// <param name="tableName">The table of the batch, if it has a single one.</param>
    /// <param name="unprocessed">How many actions are left.</param>
    /// <param name="requests">How many requests were sent.</param>
    public void LogBatchIncomplete(string operationName, string? tableName, int unprocessed, int requests) =>
        ClientLog.BatchIncomplete(_logger, operationName, tableName ?? NoSingleTable, unprocessed, requests);

    /// <summary>
    /// Waits between two runs of a conflict retry, with the jitter of the retry policy and on the clock of the client. It is not a retry of a failed call, so it takes nothing from
    /// the retry quota, and <see cref="RetryPolicy.GetConflictDelay"/> says why it has a backoff of its own.
    /// </summary>
    /// <param name="run">The number of the run that failed, starting at 1.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    public Task ConflictBackoffAsync(int run, CancellationToken cancellationToken) =>
        DelayAsync(_retryPolicy.GetConflictDelay(run), cancellationToken);

    /// <summary>
    /// Waits before <c>ReadStreamAsync</c> reads a shard again after a transient failure, with the jitter of the retry policy and on the clock of the client. The calls it
    /// repeats take from the retry quota as any call does, so the wait takes nothing.
    /// </summary>
    /// <param name="retry">The number of the retry of this outage of the shard, starting at 1.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the wait is over.</returns>
    public Task ShardRetryBackoffAsync(int retry, CancellationToken cancellationToken) =>
        DelayAsync(_retryPolicy.GetShardRetryDelay(retry), cancellationToken);

    /// <summary>Counts a run of a conflict retry that lost and is repeated.</summary>
    /// <param name="conflict">The failure of the run.</param>
    public void CountConflictRetry(DynamoDbException conflict) =>
        Telemetry.CountConflictRetry(conflict.OperationName ?? UnknownOperation, conflict.TableName, conflict);

    /// <summary>Counts an item that a read skipped because its type could not read it.</summary>
    /// <param name="operationName">The operation of the read.</param>
    /// <param name="tableName">The table of the read.</param>
    public void CountSkippedItem(string operationName, string tableName) =>
        Telemetry.CountSkippedItem(operationName, tableName);

    /// <summary>Logs that a shard of a stream was read to its end, an event of the reading of a stream and of no single call.</summary>
    /// <param name="shardId">The shard.</param>
    /// <param name="children">How many child shards are read next.</param>
    public void LogShardFinished(string shardId, int children) => ClientLog.ShardFinished(_logger, shardId, children);

    /// <summary>Logs that DynamoDB listed a child of a shard only on a later look, the event the reader of a stream looks again for.</summary>
    /// <param name="childShardId">The child.</param>
    /// <param name="shardId">The shard it continues.</param>
    /// <param name="elapsed">The time since the first look for the children of the shard.</param>
    public void LogChildListedLate(string childShardId, string shardId, TimeSpan elapsed) => ClientLog.ChildListedLate(_logger, childShardId, shardId, elapsed);

    /// <summary>Reports that the reading of a shard failed transiently and is tried again, once per outage of the shard, an event of the reading of a stream and of no single call.</summary>
    /// <param name="shardId">The shard.</param>
    /// <param name="failure">The failure that started the outage.</param>
    /// <param name="window">How long the reader tries before the failure surfaces.</param>
    public void LogShardRetrying(string shardId, Exception failure, TimeSpan window) =>
        ClientLog.ShardRetrying(_logger, shardId, OperationTelemetry.GetErrorType(failure), window);

    /// <summary>Logs that the reading of a shard works again after an outage.</summary>
    /// <param name="shardId">The shard.</param>
    /// <param name="elapsed">The time since the failure that started the outage.</param>
    /// <param name="retries">How many times the shard was tried again.</param>
    public void LogShardRecovered(string shardId, TimeSpan elapsed, int retries) => ClientLog.ShardRecovered(_logger, shardId, elapsed, retries);

    /// <summary>Logs that a table that <c>CreateTable().IfNotExists()</c> found differs from what the command declares, once per command, since the program that meets it is usually not the one that can fix it.</summary>
    /// <param name="tableName">The table.</param>
    /// <param name="drift">The differences, at least one. They name the schema alone: tables, indexes and attributes.</param>
    public void LogTableDrift(string tableName, IReadOnlyList<TableDrift> drift) =>
        ClientLog.TableDrift(_logger, tableName, drift.Count, string.Join(" ", drift.Select(static difference => difference.Description)));

    /// <summary>Reports that a conflict retry gave up, the one event of a conflict retry that no single call sees.</summary>
    /// <param name="conflict">The failure of the last run.</param>
    /// <param name="runs">How many runs there were.</param>
    public void ReportConflictsExhausted(DynamoDbException conflict, int runs)
    {
        var operationName = conflict.OperationName ?? UnknownOperation;
        Telemetry.CountConflictExhausted(operationName, conflict.TableName, conflict);
        ClientLog.ConflictRunsExhausted(_logger, operationName, conflict.TableName ?? NoSingleTable, OperationTelemetry.GetErrorType(conflict), runs);
    }

    /// <summary>Releases the timeout sources that wait to be reused. Calls that are still running dispose theirs as they finish.</summary>
    public void Dispose()
    {
        foreach (var source in _timeouts.Close())
        {
            source.Dispose();
        }
    }

    private static PreparedRequest Prepare(string? tableName, ArraySegment<byte> body)
    {
        var hash = default(Sha256Hash);
        SHA256.HashData(body.AsSpan(), hash);
        return new PreparedRequest(tableName, body, hash);
    }

    private static TResponse Parse<TCall, TResponse>(in TCall call, PooledByteBuffer body, HttpResponseMessage response, string? tableName)
        where TCall : struct, IWireCall<TResponse>
    {
        try
        {
            return call.ReadResponse(body.WrittenSpan);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ErrorMapper.Unrecognized(response.StatusCode, ReadRequestId(response), exception, null, call.OperationName, tableName);
        }
    }

    private static string? ReadRequestId(HttpResponseMessage response) =>
        response.Headers.NonValidated.TryGetValues(WireProtocol.RequestIdHeaderName, out var values) ? values.ToString() : null;

    /// <summary>
    /// One HTTP attempt: credentials, request, signature, send, response body, result or exception. Everything a repeated
    /// attempt needs again is created here; the serialized body and its hash are reused. The state machine comes from a pool, which a
    /// call that suspends would otherwise allocate for every attempt; so the returned task must be awaited exactly once, as the loop does.
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ReturnTimeout puts the source back into the pool or disposes it.")]
    private async ValueTask<TResponse> AttemptAsync<TCall, TResponse>(TCall call, CallBuffers buffers, PreparedRequest prepared, Activity? activity, CancellationToken cancellationToken)
        where TCall : struct, IWireCall<TResponse>
    {
        // One source for the timeout and for the caller's token. It follows the configured TimeProvider, which CancelAfter on a linked
        // source would not, and it is reused: see ReturnTimeout.
        var timeout = _attemptTimeout == Timeout.InfiniteTimeSpan ? null : RentTimeout();
        var link = timeout is not null && cancellationToken.CanBeCanceled
            ? cancellationToken.UnsafeRegister(static source => ((CancellationTokenSource)source!).Cancel(), timeout)
            : default;
        var token = timeout?.Token ?? cancellationToken;
        try
        {
            var credentials = await _credentials.GetCredentialsAsync(token).ConfigureAwait(false);
            var signedAt = _clock.GetUtcNow();
            using var request = CreateRequest(call.Target, prepared, credentials, signedAt);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

            // The id is a string per response, so it is only read for what needs it: a span that records it and an error.
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag(TelemetryNames.AwsRequestId, ReadRequestId(response));
            }

            await ReadBodyAsync(response, buffers.Response, token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Parse<TCall, TResponse>(in call, buffers.Response, response, prepared.TableName)
                : throw MapError(response, buffers.Response, signedAt, credentials, call.OperationName, prepared.TableName);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // The exception carries the linked token. Callers expect their own.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
        catch (OperationCanceledException exception) when (timeout is { IsCancellationRequested: true })
        {
            throw new TimeoutException($"DynamoDB {call.OperationName} did not complete within the attempt timeout of {_attemptTimeout}.", exception);
        }
        finally
        {
            // In this order: once the registration is gone, the caller's token cannot reach the source any more.
            await link.DisposeAsync().ConfigureAwait(false);
            ReturnTimeout(timeout);

            // The response is read or failed by now, whatever happened to the attempt.
            buffers.Response.Release();
        }
    }

    private CancellationTokenSource RentTimeout()
    {
        var source = _timeouts.Rent() ?? new CancellationTokenSource(Timeout.InfiniteTimeSpan, _timeProvider);
        source.CancelAfter(_attemptTimeout);
        return source;
    }

    // A source goes back only if TryReset says that nothing can cancel it any more: it was not cancelled, and if its timer has not fired
    // it is stopped; registrations that a handler left behind are dropped. A source that was cancelled, by the timeout or by the
    // caller, or that does not fit the pool, is disposed, so a cancelled state never reaches another call.
    private void ReturnTimeout(CancellationTokenSource? source)
    {
        if (source is not null && !(source.TryReset() && _timeouts.Return(source)))
        {
            source.Dispose();
        }
    }

    private HttpRequestMessage CreateRequest(string target, PreparedRequest prepared, AwsCredentials credentials, DateTimeOffset signedAt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.TryAddWithoutValidation(WireProtocol.TargetHeaderName, target);
        request.Headers.TryAddWithoutValidation(UserAgentHeaderName, WireProtocol.UserAgent);

        // Exactly this content type, as text: parsing it into a MediaTypeHeaderValue costs more than the rest of the request, and
        // the signature covers the value as it is sent.
        var content = new ByteArrayContent(prepared.Body.Array!, prepared.Body.Offset, prepared.Body.Count);
        content.Headers.TryAddWithoutValidation(ContentTypeHeaderName, WireProtocol.ContentType);
        request.Content = content;

        var hash = prepared.Hash;
        _signer.Sign(request, _authority, target, hash, credentials, signedAt);
        return request;
    }

    // Reads into the buffer of the call, which the caller of this method releases when the attempt is over, however it ended.
    private async ValueTask ReadBodyAsync(HttpResponseMessage response, PooledByteBuffer buffer, CancellationToken cancellationToken)
    {
        // A Content-Length above the limit sizes no buffer: the read fails at once.
        var length = response.Content.Headers.ContentLength;
        buffer.Reserve(length is { } announced && announced > 0 && announced <= _maxResponseLength ? (int)announced : PooledByteBuffer.DefaultInitialCapacity);
        bool complete;
        try
        {
            // Not ReadAsStreamAsync: the content of SocketsHttpHandler has its stream at hand, so the task would only wrap it, for 72 bytes
            // per call. Only an unusual handler, with a content that makes its stream on demand, can block here.
#pragma warning disable CA1849
            using var stream = response.Content.ReadAsStream(cancellationToken);
#pragma warning restore CA1849
            complete = await buffer.ReadFromAsync(stream, length, _maxResponseLength, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelling a read can surface as an IOException. That is the cancellation or the timeout of the attempt, which the caller
            // of this method turns into the exception the caller of the client expects, and not a connection that dropped (retryable).
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
        catch (IOException exception)
        {
            // A connection that drops mid-body must look like one that drops before the headers.
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "The DynamoDB response ended before it was read completely.", exception);
        }

        if (!complete)
        {
            // Not retried, see FailureClassifier: the same request gets the same, too large, response.
            throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, $"The DynamoDB response is larger than the limit of {_maxResponseLength} bytes.");
        }
    }

    // A response that rejects the signature or the age of the request is checked against the Date header of the service. If the time
    // the request was signed with is off, the clock is corrected and sending the request again can work, see ClockCorrectedException.
    // A response that rejects the credentials as expired lets the provider drop them, see CredentialsDroppedException. This happens
    // whether or not the request is sent again: without it, the calls that follow would fail the same way until the expiry says otherwise.
    private Exception MapError(HttpResponseMessage response, PooledByteBuffer body, DateTimeOffset signedAt, AwsCredentials credentials, string operationName, string? tableName)
    {
        var rejection = ErrorMapper.Map(response.StatusCode, ReadRequestId(response), body.WrittenSpan, operationName, tableName);
        if (ClockSkewCorrection.IsClockSkewCode(rejection.ErrorCode)
            && response.Headers.Date is { } serverTime
            && _clock.TryCorrect(signedAt, serverTime, out var offset))
        {
            ClientLog.ClockSkewCorrected(_logger, offset);
            return new ClockCorrectedException(rejection);
        }

        return FailureClassifier.IsExpiredCredentialsCode(rejection.ErrorCode) && _refreshableCredentials?.TryInvalidate(credentials) == true
            ? new CredentialsDroppedException(rejection)
            : rejection;
    }

    // Classifies the failure, asks the policy and reports what happened: throttle and retry counters, and a warning when a
    // retryable failure is passed on to the caller because the budget for repeating it is gone.
    private RetryDecision Decide(Exception exception, bool resendAtOnce, string operationName, string? tableName, int attempt, int lastRetryCost, int callMaxAttempts)
    {
        var kind = FailureClassifier.Classify(exception);
        if (kind == FailureKind.Throttling)
        {
            Telemetry.CountThrottle(operationName, tableName, exception);
        }

        var decision = _retryPolicy.Decide(kind, resendAtOnce, attempt, lastRetryCost, callMaxAttempts);
        switch (decision.Verdict)
        {
            case RetryVerdict.AttemptsExhausted:
                ClientLog.RetriesExhausted(_logger, operationName, tableName ?? NoSingleTable, OperationTelemetry.GetErrorType(exception), attempt);
                break;
            case RetryVerdict.QuotaExhausted:
                ClientLog.RetryQuotaExhausted(_logger, operationName, tableName ?? NoSingleTable, OperationTelemetry.GetErrorType(exception), attempt);
                break;
            case RetryVerdict.Resend or RetryVerdict.Retry:
                Telemetry.CountRetry(operationName, tableName, exception);
                break;
        }

        return decision;
    }

    private async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            // Task.Delay throws a TaskCanceledException. Callers expect the same exception, with their token, as from an attempt.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
    }

    [InlineArray(SHA256.HashSizeInBytes)]
    private struct Sha256Hash
    {
        private byte _element0;
    }

    private readonly struct PreparedRequest(string? tableName, ArraySegment<byte> body, Sha256Hash hash)
    {
        public string? TableName { get; } = tableName;

        public ArraySegment<byte> Body { get; } = body;

        public Sha256Hash Hash { get; } = hash;
    }

    /// <summary>
    /// How an attempt tells the retry loop that the service rejected its signing time and that the clock is corrected now, so that
    /// a rejection that is permanent by its error code is worth sending again. It never leaves the pipeline: if the request is not
    /// sent again, the caller gets the <see cref="Rejection"/>.
    /// </summary>
    /// <param name="rejection">The error of the service.</param>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "It never leaves this class.")]
    private sealed class ClockCorrectedException(DynamoDbException rejection) : Exception
    {
        public DynamoDbException Rejection { get; } = rejection;
    }

    /// <summary>
    /// How an attempt tells the retry loop that the service rejected its credentials as expired and that the provider dropped them, so
    /// that the next attempt looks up other ones. It never leaves the pipeline: if the request is not sent again, the caller gets the
    /// <see cref="Rejection"/>.
    /// </summary>
    /// <param name="rejection">The error of the service.</param>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "It never leaves this class.")]
    private sealed class CredentialsDroppedException(DynamoDbException rejection) : Exception
    {
        public DynamoDbException Rejection { get; } = rejection;
    }
}
