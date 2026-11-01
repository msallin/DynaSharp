using System.Buffers;
using System.Diagnostics;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Streams;

/// <summary>
/// A client for the Amazon DynamoDB Streams low-level API: it lists the streams of tables, describes their shards and reads the
/// records of changes to items. It is the counterpart of <see cref="DynamoDbClient"/> for the change log, and is configured with the
/// same <see cref="DynamoDbClientOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the plain API: there is no consumer, no checkpointing and no assignment of shards to workers. A reader lists the
/// shards with <see cref="DescribeStreamAsync"/>, takes an iterator for each with <see cref="GetShardIteratorAsync"/> and calls
/// <see cref="GetRecordsAsync"/> in a loop. A stream is enabled per table, with <see cref="CreateTableRequest.StreamSpecification"/>
/// when the table is created or later through <c>UpdateTable</c>; <see cref="TableDescription.LatestStreamArn"/> names the stream.
/// </para>
/// <para>
/// Streams have their own endpoint, <c>https://streams.dynamodb.&lt;region&gt;.amazonaws.com</c>. The settings follow the same order as
/// for <see cref="DynamoDbClient"/>, with the endpoint taken from <see cref="DynamoDbClientOptions.ServiceUrl"/>, then the
/// <c>AWS_ENDPOINT_URL_DYNAMODB_STREAMS</c> environment variable, then <c>AWS_ENDPOINT_URL</c>, and otherwise the public endpoint of
/// the region (<see cref="AwsRegion.DynamoDbStreamsEndpoint"/>). Region, credentials, retries, timeouts, telemetry and logging work
/// as they do for <see cref="DynamoDbClient"/>, and <see cref="Requests.DynamoDbRequests"/> describes the retries, the errors and the telemetry that both share; requests are signed for the service name <c>dynamodb</c>. A client without
/// <see cref="DynamoDbClientOptions.Credentials"/> makes its own default credentials provider, so an application that uses this client
/// together with a <see cref="DynamoDbClient"/> on EC2 or ECS should pass one provider to both, as described there. Note that
/// <see cref="DynamoDbClientOptions.ServiceUrl"/> applies to both clients: an emulator such as DynamoDB Local serves both APIs on
/// one address, but for AWS itself the two clients need no explicit URL.
/// </para>
/// <para>
/// Errors surface as <see cref="DynamoDbException"/> or a derived type. <see cref="ExpiredIteratorException"/> and
/// <see cref="TrimmedDataAccessException"/> are not retried: repeating the request cannot fix them. Instances are thread-safe. Create
/// one client per application and dispose it to release its connections; a handler passed in
/// <see cref="DynamoDbClientOptions.HttpHandler"/> is not disposed.
/// </para>
/// <para>
/// Each operation emits one <c>CLIENT</c> activity named <c>DynamoDB Streams.&lt;operation&gt;</c> and one measurement of
/// <c>db.client.operation.duration</c>, like the operations of <see cref="DynamoDbClient"/>. Shard iterators are credentials, and
/// neither they nor the keys and images of records ever appear in telemetry or logs.
/// </para>
/// </remarks>
public sealed class DynamoDbStreamsClient : IDisposable
{
    private readonly ClientCore _core;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbStreamsClient"/> class that takes all settings from the environment:
    /// the region from <c>AWS_REGION</c> (or <c>AWS_DEFAULT_REGION</c>, or the selected profile in the shared AWS config file),
    /// the endpoint from <c>AWS_ENDPOINT_URL_DYNAMODB_STREAMS</c> (or <c>AWS_ENDPOINT_URL</c>, otherwise the public endpoint of
    /// the region) and the credentials from the default chain of the AWS SDKs, see <see cref="DefaultCredentialsProvider"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No region is configured, or a region or endpoint environment variable is invalid.</exception>
    public DynamoDbStreamsClient()
        : this(new DynamoDbClientOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbStreamsClient"/> class.
    /// </summary>
    /// <param name="options">
    /// The settings. Options that are not set fall back to the environment, see <see cref="DynamoDbClientOptions"/>. The client
    /// reads the options once; changing the options object later has no effect.
    /// </param>
    /// <remarks>
    /// Credentials are not looked up here, but with the first request. See <see cref="DynamoDbClient"/> for the details of the
    /// configuration, which this client shares.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its <see cref="DynamoDbClientOptions.TimeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DynamoDbClientOptions.AttemptTimeout"/> is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/>, or exceeds about 49.7 days; or <see cref="DynamoDbClientOptions.MaxAttempts"/> is below 1.</exception>
    /// <exception cref="ArgumentException"><see cref="DynamoDbClientOptions.ServiceUrl"/> is not an absolute http or https URL without user info, path, query and fragment.</exception>
    /// <exception cref="InvalidOperationException">No region is configured, or a region or endpoint environment variable is invalid.</exception>
    public DynamoDbStreamsClient(DynamoDbClientOptions options)
        : this(options, ProcessEnvironment.GetVariable, null, null)
    {
    }

    // The environment, the shared files, the buffer pool, the activity source, the source of the retry jitter, the limit on the
    // response body and the credential endpoints are parameters so that tests control them.
    internal DynamoDbStreamsClient(
        DynamoDbClientOptions options,
        Func<string, string?> getEnvironmentVariable,
        ArrayPool<byte>? pool,
        ActivitySource? activitySource,
        Random? random = null,
        ReadTextFile? readFile = null,
        int maxResponseLength = DynamoDbPipeline.MaxResponseLength,
        HttpMessageHandler? credentialsHandler = null)
    {
        _core = ClientCore.Create<DynamoDbStreamsClient>(AwsService.DynamoDbStreams, options, getEnvironmentVariable, pool, activitySource, random, readFile, maxResponseLength, credentialsHandler);
    }

    /// <summary>
    /// Lists the streams of all tables of the account in the region, or of one table (DynamoDB Streams <c>ListStreams</c>).
    /// </summary>
    /// <param name="request">The optional table and paging settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One page of at most 100 streams. Continue with <see cref="ListStreamsResponse.LastEvaluatedStreamArn"/> until it is
    /// <see langword="null"/>. A table has at most one enabled stream, but streams that were switched off stay listed for 24 hours.
    /// </returns>
    /// <remarks>
    /// To find the stream of a table you know, <see cref="TableDescription.LatestStreamArn"/> from <c>DescribeTable</c> is simpler.
    /// A page can have fewer streams than the limit and still have a successor. The call is safe to repeat.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table named in the request does not exist.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because <see cref="ListStreamsRequest.Limit"/> is below 1.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:ListStreams</c>.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<ListStreamsResponse> ListStreamsAsync(ListStreamsRequest request, CancellationToken cancellationToken = default) =>
        _core.ExecuteAsync<ListStreamsOperation, ListStreamsRequest, ListStreamsResponse>(request, cancellationToken);

    /// <summary>
    /// Reads the settings of a stream and one page of its shards (DynamoDB Streams <c>DescribeStream</c>).
    /// </summary>
    /// <param name="request">The stream, the paging settings and an optional shard filter.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The description, with at most 100 shards. Continue with <see cref="StreamDescription.LastEvaluatedShardId"/> until it is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Shards form a tree: a closed shard names its successors as the shards whose <see cref="Shard.ParentShardId"/> points at it.
    /// A reader that wants the changes of an item in order finishes a parent before it starts its children. To find the children of
    /// a shard that was read to its end, pass a <see cref="ShardFilter"/> instead of listing everything again. The list of shards
    /// changes over time, so a long-running reader describes the stream again from time to time, and whenever a shard ends.
    /// </para>
    /// <para>
    /// DescribeStream is limited to ten calls per second and stream. The call is safe to repeat.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The stream does not exist (any more).</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the stream ARN is malformed.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:DescribeStream</c> on the stream.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DescribeStreamResponse> DescribeStreamAsync(DescribeStreamRequest request, CancellationToken cancellationToken = default) =>
        _core.ExecuteAsync<DescribeStreamOperation, DescribeStreamRequest, DescribeStreamResponse>(request, cancellationToken);

    /// <summary>
    /// Gets an iterator that marks a position in a shard, to read from with <see cref="GetRecordsAsync"/> (DynamoDB Streams
    /// <c>GetShardIterator</c>).
    /// </summary>
    /// <param name="request">The stream, the shard and the position.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The iterator.</returns>
    /// <remarks>
    /// <para>
    /// An iterator is valid for 15 minutes and is meant to be used in a chain: each <see cref="GetRecordsAsync"/> returns the next
    /// one. A reader that wants to survive a restart stores the sequence number of the last record it processed and resumes with
    /// <see cref="ShardIteratorType.AfterSequenceNumber"/>. The iterator is a credential for the shard: do not log it.
    /// </para>
    /// <para>
    /// <see cref="ShardIteratorType.Latest"/> skips everything written before the call, so changes between the end of the previous
    /// iterator and the new one are lost. Use it to start a new reader, not to recover from an expired iterator. The call is safe
    /// to repeat.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The request has no <see cref="GetShardIteratorRequest.ShardIteratorType"/> or an undefined value (raised as the faulted task).</exception>
    /// <exception cref="ResourceNotFoundException">The stream or the shard does not exist (any more).</exception>
    /// <exception cref="TrimmedDataAccessException">The position is older than the 24 hours that records are kept.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because a sequence number is missing for <see cref="ShardIteratorType.AtSequenceNumber"/>.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:GetShardIterator</c> on the stream.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<GetShardIteratorResponse> GetShardIteratorAsync(GetShardIteratorRequest request, CancellationToken cancellationToken = default) =>
        _core.ExecuteAsync<GetShardIteratorOperation, GetShardIteratorRequest, GetShardIteratorResponse>(request, cancellationToken);

    /// <summary>
    /// Reads records from a shard, starting at the position of an iterator (DynamoDB Streams <c>GetRecords</c>).
    /// </summary>
    /// <param name="request">The iterator and the maximum number of records.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The records and the iterator for the next call. An empty <see cref="GetRecordsResponse.Records"/> list with a
    /// <see cref="GetRecordsResponse.NextShardIterator"/> is normal and does not mean that the shard is finished.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Read in a loop, each time with the <see cref="GetRecordsResponse.NextShardIterator"/> of the response, and wait a moment (a
    /// second is typical) when a response comes back empty: a shard allows five calls per second, and an empty response is how an
    /// open shard says that no change happened. A response can also be empty because DynamoDB looked through a part of the shard
    /// without finding a record; keep going.
    /// </para>
    /// <para>
    /// A <see langword="null"/> <see cref="GetRecordsResponse.NextShardIterator"/> means that the shard is closed and everything in
    /// it has been returned. That ends the loop for this shard; the successors are found with <see cref="DescribeStreamAsync"/>.
    /// </para>
    /// <para>
    /// A call returns at most 1 MB and 1,000 records. The call itself changes nothing, so it is safe to repeat with the same
    /// iterator within its 15 minutes, which returns the same records. Records stay available for 24 hours.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ExpiredIteratorException">The iterator is older than 15 minutes. Get a new one with <see cref="GetShardIteratorAsync"/>.</exception>
    /// <exception cref="TrimmedDataAccessException">The records at the position of the iterator are older than 24 hours and have been removed.</exception>
    /// <exception cref="LimitExceededException">The request rate is too high. The client retries it, so this surfaces only when every attempt failed with it.</exception>
    /// <exception cref="ResourceNotFoundException">The stream or shard does not exist (any more).</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the iterator is malformed or the limit out of range.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:GetRecords</c> on the stream.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error (for example <see cref="InternalServerErrorException"/>)
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<GetRecordsResponse> GetRecordsAsync(GetRecordsRequest request, CancellationToken cancellationToken = default) =>
        _core.ExecuteAsync<GetRecordsOperation, GetRecordsRequest, GetRecordsResponse>(request, cancellationToken);

    /// <summary>
    /// Sends a DynamoDB Streams operation by name, with a request body that you write as JSON, and returns the response body as
    /// JSON. It is for the operations and parameters that this client does not model.
    /// </summary>
    /// <param name="operation">
    /// The operation name as DynamoDB Streams knows it, for example <c>GetRecords</c>. It consists of ASCII letters and digits; it is
    /// sent as the <c>X-Amz-Target</c> header and is the operation of the span and the metrics of the call.
    /// </param>
    /// <param name="requestJson">
    /// The request body: a JSON object in the format of the DynamoDB Streams API (AWS JSON 1.0). The bytes are copied; they are not read
    /// or checked, so the service reports a request it cannot parse. The memory can be reused as soon as the returned task has completed.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The body of the response as UTF-8 JSON, exactly as the service sent it. It is not parsed.</returns>
    /// <remarks>
    /// The call is signed, repeated, bounded and reported like the other calls of this client; see
    /// <see cref="DynamoDbClient.SendRawAsync"/>, which describes what that means. The span is named
    /// <c>DynamoDB Streams.&lt;operation&gt;</c>, and there is no table tag. Shard iterators in a request or response are credentials and
    /// never appear in telemetry or logs.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is empty or contains a character other than an ASCII letter or digit.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">The service rejects the request.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for the operation.</exception>
    /// <exception cref="DynamoDbException">
    /// The service returns any other error, an operation it does not know, or a response that is not a DynamoDB response. A throttling or server error
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<byte[]> SendRawAsync(string operation, ReadOnlyMemory<byte> requestJson, CancellationToken cancellationToken = default) =>
        _core.SendRawAsync(operation, requestJson, cancellationToken);

    /// <summary>
    /// Releases the HTTP connections. Requests still running may fail. Calling it again has no effect.
    /// </summary>
    /// <remarks>
    /// The handler given in <see cref="DynamoDbClientOptions.HttpHandler"/> is not disposed; its owner does that.
    /// </remarks>
    public void Dispose() => _core.Dispose();
}
