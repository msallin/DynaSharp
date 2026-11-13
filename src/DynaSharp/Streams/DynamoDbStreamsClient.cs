using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// There is no consumer: no checkpointing, no leasing and no sharing of shards among processes. <see cref="ReadStreamAsync"/> reads every shard of a
/// stream in this process, parents before their children. <see cref="ReadShardAsync"/> reads one shard, which a reader lists with
/// <see cref="DescribeStreamAsync"/>: it takes the iterator, pages through <see cref="GetRecordsAsync"/> and renews an expired iterator.
/// <see cref="GetShardIteratorAsync"/> and <see cref="GetRecordsAsync"/> are there for a loop of its own. A stream is enabled per table, with <see cref="CreateTableRequest.StreamSpecification"/>
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
    private const string EmptyPageDelayMessage = "EmptyPageDelay must be zero or more and below the 15 minutes an iterator lives.";

    // The range of the Limit of GetRecords.
    private const int MinRecordsLimit = 1;
    private const int MaxRecordsLimit = 1000;

    private static readonly TimeSpan IteratorLifetime = TimeSpan.FromMinutes(15);

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
    /// Gets the options as the client resolved them when it was created: what it really uses, so a region from the environment or a profile and an endpoint from the environment are here and not null, and
    /// <see cref="DynamoDbClientOptions.Credentials"/> is the provider in use, also the default chain that the client made itself. The one exception is the public endpoint of the region, see the remarks. Nothing is
    /// read again, so the environment of today can say something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second client is <c>new DynamoDbStreamsClient(client.Options with { MaxAttempts = 1 })</c>. It shares the provider and with it the cached credentials, and a handler the first client was given. A handler the first
    /// client made stays its own, so <see cref="DynamoDbClientOptions.HttpHandler"/> is <see langword="null"/> there and the second client makes one too, and each outlives the other.
    /// </para>
    /// <para>
    /// <see cref="DynamoDbClientOptions.ServiceUrl"/> holds an endpoint that came from the options or the environment, which a copy keeps. The public endpoint that the client derived from its region is in use and still stays
    /// <see langword="null"/> here, on purpose: it is no choice of anyone's, but a consequence of the region and of the kind of client. So <c>client.Options with { Region = other }</c> goes to the endpoint of the new region, and
    /// <c>new DynamoDbClient(client.Options)</c> gets its own endpoint.
    /// </para>
    /// <para>The options hold a provider and not credentials, and <see cref="DynamoDbClientOptions.ToString"/> prints only its type.</para>
    /// </remarks>
    public DynamoDbClientOptions Options => _core.Options;

    /// <summary>
    /// Gets or sets what gives the next change of the streams of the server, as a task that completes then, for a server that can tell: only <c>FakeDynamoDb</c> of
    /// <c>DynaSharp.Testing</c>, which sets it on the client of its <c>CreateStreamsClient</c>. A wait of a reader, after an empty page or before the next look for children, then
    /// ends at a write or a change of the shards instead of after its whole time, so a test on the fake reads a write at once. It is <see langword="null"/> otherwise, and against
    /// DynamoDB, which cannot tell, a reader waits its whole time.
    /// </summary>
    internal Func<Task>? NextStreamChange { get; set; }

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
    /// <returns>The iterator, or a response without one, which the service model allows: there is nothing to read from the shard then.</returns>
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
    /// Reads one shard from a position to its end, as a sequence of records: it takes the iterator, pages through <c>GetRecords</c>, takes a new
    /// iterator when one expires, and waits after an empty page.
    /// </summary>
    /// <param name="request">The stream, the shard, the position to start at and the paging settings.</param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>
    /// The records in the order of the shard, which is the order of the changes to each item in it. The sequence ends when the shard is closed
    /// and every record of it has been returned, and an open shard has no end: DynamoDB closes a shard after some hours, or when its partition splits.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This reads one shard and is no consumer: nothing stores how far it got, and nothing shares the shards among workers. The position to
    /// keep is on each record, <see cref="StreamRecord.SequenceNumber"/> of <see cref="ChangeRecord.DynamoDb"/>: a reader that stores the one of
    /// the last record it handled starts again after it with <see cref="ShardIteratorType.AfterSequenceNumber"/>. Changes of one item can move to
    /// a child shard when a shard ends, so a reader that wants them in order reads a parent to its end before its children, which
    /// <see cref="DescribeStreamAsync"/> lists with a <see cref="ShardFilter"/>. <see cref="ReadStreamAsync"/> does that for every shard of a stream.
    /// </para>
    /// <para>
    /// Nothing is requested until the first record is asked for. An iterator lives 15 minutes, and a caller that takes longer between two
    /// records holds an expired one when the next page is due. The reader then takes a new iterator after the sequence number of the last
    /// record it returned, so nothing is skipped and nothing repeats. Before the first record it takes one at the position of the request
    /// again, which for <see cref="ShardIteratorType.Latest"/> skips what was written in between and calls <see cref="ReadShardRequest.OnGap"/>. An iterator that is expired the moment it was
    /// taken is not renewed again: the <see cref="ExpiredIteratorException"/> surfaces.
    /// </para>
    /// <para>
    /// An open shard answers an empty page when nothing changed, and also when DynamoDB looked at a part of the shard without finding a record,
    /// so an empty page is no end: the reader waits <see cref="ReadShardRequest.EmptyPageDelay"/> and asks again. A shard read to its end
    /// answers without a next iterator, and that ends the sequence. A <see cref="GetShardIteratorAsync"/> without an iterator ends it at once.
    /// </para>
    /// <para>
    /// Each call is a span and a measurement of its own, as with the methods it is made of, and a renewal shows as a failed <c>GetRecords</c>
    /// followed by a <c>GetShardIterator</c>. A record without a sequence number does not move the position.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="ReadShardRequest.ShardIteratorType"/> is <see cref="ShardIteratorType.Unknown"/> or undefined, <see cref="ReadShardRequest.Limit"/> is not from 1 to 1,000, or
    /// <see cref="ReadShardRequest.EmptyPageDelay"/> is negative or 15 minutes or more.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="ReadShardRequest.SequenceNumber"/> is missing or empty for <see cref="ShardIteratorType.AtSequenceNumber"/> or
    /// <see cref="ShardIteratorType.AfterSequenceNumber"/>, or set for <see cref="ShardIteratorType.TrimHorizon"/> or <see cref="ShardIteratorType.Latest"/>.
    /// </exception>
    /// <exception cref="TrimmedDataAccessException">
    /// The position is older than the 24 hours that records are kept, at the start or while reading, so changes are lost and the reader cannot
    /// tell which. It is not handled here: start again at <see cref="ShardIteratorType.TrimHorizon"/>, and rebuild from the table what the gap matters for.
    /// </exception>
    /// <exception cref="ExpiredIteratorException">A new iterator was expired before it could be used.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed (raised while reading).</exception>
    /// <exception cref="ResourceNotFoundException">The stream or the shard does not exist (any more): DynamoDB removes a shard 24 hours after it closed.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, for example <see cref="ValidationException"/> for a malformed ARN or a limit out of range. A throttling or
    /// server error surfaces only when the last attempt of a call failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">A request could not be sent or its response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt of a call did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public IAsyncEnumerable<ChangeRecord> ReadShardAsync(ReadShardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var needsSequenceNumber = request.ShardIteratorType switch
        {
            ShardIteratorType.TrimHorizon or ShardIteratorType.Latest => false,
            ShardIteratorType.AtSequenceNumber or ShardIteratorType.AfterSequenceNumber => true,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.ShardIteratorType, "ShardIteratorType must be TrimHorizon, Latest, AtSequenceNumber or AfterSequenceNumber."),
        };
        if (needsSequenceNumber != request.SequenceNumber is not null)
        {
            throw new ArgumentException("AtSequenceNumber and AfterSequenceNumber need a SequenceNumber, and TrimHorizon and Latest take none.", nameof(request));
        }

        if (request.SequenceNumber is { Length: 0 })
        {
            throw new ArgumentException("SequenceNumber is empty: AtSequenceNumber and AfterSequenceNumber need the sequence number of a record.", nameof(request));
        }

        if (request.Limit is < MinRecordsLimit or > MaxRecordsLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, "Limit must be from 1 to 1,000 records, or null for the default of GetRecords.");
        }

        if (!IsEmptyPageDelayValid(request.EmptyPageDelay))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.EmptyPageDelay, EmptyPageDelayMessage);
        }

        return ReadShardCoreAsync(request, null, cancellationToken);
    }

    /// <summary>
    /// Reads every shard of a stream in this process, as one sequence of records: it lists the shards, reads them at the same time with
    /// <see cref="ReadShardAsync"/>, and starts the children of a shard once the shard has been read to its end.
    /// </summary>
    /// <param name="request">The stream, the position to start at and the wait on an idle shard.</param>
    /// <param name="cancellationToken">Cancels the reading. A token given to <c>WithCancellation</c> works too.</param>
    /// <returns>
    /// The records of all shards, each with the shard it came from. The changes of one item come in their order, because a shard is read to its end before its
    /// children; records of different shards come in no particular order. The sequence ends only when no shard is left to read, which is when the stream was
    /// switched off and every shard has been read; while the stream is enabled it runs until it is cancelled.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This reads one stream in one process and is no consumer: nothing stores how far it got, nothing shares the shards with other processes, and there is no
    /// position to resume at, no checkpoint store, no lease, no setting of the retries of a shard or of the buffer, and no typed record. An image is an
    /// <see cref="Item"/>: <see cref="Item.Is{T}"/> tells the entities apart and <see cref="Item.To{T}"/> reads one. The position to keep is on each record:
    /// <see cref="ShardChangeRecord.ShardId"/> and <see cref="ShardChangeRecord.SequenceNumber"/>, from which <see cref="ReadShardAsync"/> with
    /// <see cref="ShardIteratorType.AfterSequenceNumber"/> reads that shard on. The shard is on <see cref="ShardChangeRecord"/> and not on
    /// <see cref="ChangeRecord"/>, because <see cref="GetRecordsAsync"/> cannot know it.
    /// </para>
    /// <para>
    /// <see cref="DescribeStreamAsync"/> lists the shards, page by page. A stream that is <see cref="StreamStatus.Enabled"/> or <see cref="StreamStatus.Enabling"/> and
    /// lists no shard yet, as one just enabled can, is listed again every <see cref="ReadStreamRequest.EmptyPageDelay"/> but at least every second, until it lists one,
    /// and the shards of that listing opened after the start, so they are read from their first record whatever the position. With <see cref="ShardIteratorType.TrimHorizon"/> each shard whose parent is not listed is
    /// read from its oldest record, closed or open, and the others wait for their parent. With <see cref="ShardIteratorType.Latest"/> each shard open at the
    /// start is read from now on, and a shard closed at the start has nothing to read from now on, but is watched for children as a shard read to its end is, unless
    /// its children listed at the start opened more than a minute before: a split in progress at the start can list its second child only later. A shard read to its
    /// end starts its children, which <see cref="DescribeStreamAsync"/> lists with a <see cref="ShardFilter"/> of <see cref="ShardFilterType.ChildShards"/>. A child
    /// is read with <see cref="ShardIteratorType.AtSequenceNumber"/> at the <see cref="SequenceNumberRange.StartingSequenceNumber"/> of its range, so that
    /// records of it trimmed before its turn came throw instead of being passed over; without a starting sequence number it is read from the trim horizon. A shard
    /// counts as one of the children only if its <see cref="Shard.ParentShardId"/> names the parent, also when the service ignores the filter.
    /// </para>
    /// <para>
    /// DynamoDB can list a child a moment after its parent closed, and the Developer Guide says a shard can split into several. So each child starts as soon as it is
    /// listed, and the reader of the parent keeps asking: while the stream is <see cref="StreamStatus.Enabled"/> or <see cref="StreamStatus.Enabling"/> and no child is
    /// listed, every <see cref="ReadStreamRequest.EmptyPageDelay"/> but at least every second; after the first child, every five seconds for one more minute. A stream
    /// that is no longer enabled opens no more shards, which ends the asking.
    /// </para>
    /// <para>
    /// Nothing is requested until the first record is asked for. The records go through a small bounded buffer, so a shard waits while the caller is behind, and
    /// a reader that keeps a shard busy holds the others back too. Each shard is read as <see cref="ReadShardAsync"/> reads it: an expired iterator is renewed,
    /// and an open shard is asked again after <see cref="ReadStreamRequest.EmptyPageDelay"/>.
    /// </para>
    /// <para>
    /// A shard that fails for a moment is read on from where it stopped. When the last attempt of a call of a shard fails with what <see cref="DynamoDbException.IsTransient"/> calls transient,
    /// or with a network failure or a timeout of an attempt, the reader of the shard waits, from a second up, doubling to half a minute, with jitter, on
    /// <see cref="DynamoDbClientOptions.TimeProvider"/>, and sends the call again: a page with the same iterator, which returns the same records, and a look for children
    /// again. An iterator that expired in the meantime is renewed after the last record the shard handed out, so nothing repeats and nothing is skipped, or at the start
    /// position of the shard when it has handed out none. For a shard read from <see cref="ShardIteratorType.Latest"/> that is <see cref="ShardIteratorType.Latest"/>
    /// again, which skips what was written while the iterator was expired, since DynamoDB Streams has no position by time, and calls <see cref="ReadStreamRequest.OnGap"/>
    /// so the caller can reload what it keeps; or read from <see cref="ShardIteratorType.TrimHorizon"/>, or resume a shard after a stored sequence number with <see cref="ReadShardAsync"/>, where that matters. A position
    /// trimmed in the meantime throws <see cref="TrimmedDataAccessException"/>. The reader tries for
    /// 15 minutes from the failure that started the outage, the life of an iterator; a page or a look that succeeds ends the
    /// outage. The first listing of the shards is not tried again: nothing has been read then, and a setup that does not work shows at once.
    /// </para>
    /// <para>
    /// Any other failure, and one that outlasts the 15 minutes, ends the sequence with that exception: the other readers are stopped and awaited, and the records
    /// still in the buffer are dropped. Apart from that case, which <see cref="ReadStreamRequest.OnGap"/> reports, also when the iterator expired because the caller was behind, nothing is skipped: a position older than the 24 hours that records are kept throws <see cref="TrimmedDataAccessException"/>, and so does a
    /// closed shard that DynamoDB removed, 24 hours after it closed, before it was read, whose records are lost. Disposing the enumerator or cancelling stops
    /// every reader and waits for it, also one that waits to try again, so none outlives the enumeration. <see cref="ReadStreamRequest.OnJoined"/> says when every
    /// shard read from the start holds its position.
    /// </para>
    /// <para>
    /// Each call it makes is a span and a measurement of its own, as with the methods it is made of. Each shard it reads is an <c>Internal</c> span too, named
    /// <c>DynamoDB Streams.ReadStream</c>, under the span that was current when the enumeration started, with the calls of the shard under it and a link to the
    /// span of its parent shard. It ends when the looks for the children of the shard end, and a shard that DynamoDB keeps open for hours makes a span of hours.
    /// The metric <c>dynasharp.client.stream.shards</c> counts the shards being read, <c>dynasharp.client.stream.records</c> the records handed out, and
    /// <c>dynasharp.client.stream.retries</c> the retries of a shard, which its span counts in <c>dynasharp.stream.retry_count</c>, and <c>dynasharp.client.stream.gaps</c> each gap of
    /// <see cref="ReadStreamRequest.OnGap"/>, which also logs a warning. A shard
    /// read to its end logs its children at the debug level, and so does a child listed only on a later look. A shard that starts to fail transiently logs a
    /// warning, once, and an information when it reads again.
    /// Neither names a key, an image or the ARN of the stream.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="ReadStreamRequest.ShardIteratorType"/> is neither <see cref="ShardIteratorType.TrimHorizon"/> nor <see cref="ShardIteratorType.Latest"/>, or
    /// <see cref="ReadStreamRequest.EmptyPageDelay"/> is negative or 15 minutes or more.
    /// </exception>
    /// <exception cref="TrimmedDataAccessException">
    /// The position in a shard is older than the 24 hours that records are kept, or a closed shard was removed before it was read, so changes are lost. It is
    /// not handled here: start again, and rebuild from the table what the gap matters for.
    /// </exception>
    /// <exception cref="ExpiredIteratorException">A new iterator was expired before it could be used.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed (raised while reading).</exception>
    /// <exception cref="ResourceNotFoundException">The stream does not exist (any more), or a shard that was open when it was listed is gone.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, for example <see cref="ValidationException"/> for a malformed ARN. A throttling or server error
    /// surfaces only when the first listing failed with it, or when a shard kept failing with it for 15 minutes.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">A request could not be sent or its response could not be read, on every attempt of the first listing, or for 15 minutes of a shard.</exception>
    /// <exception cref="TimeoutException">The last attempt of the first listing, or of a shard for 15 minutes, did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public IAsyncEnumerable<ShardChangeRecord> ReadStreamAsync(ReadStreamRequest request, CancellationToken cancellationToken = default)
    {
        CheckStreamRequest(request);
        return StreamMerge.ReadAsync(this, _core, request, cancellationToken);
    }

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

    /// <summary>Checks the arguments of <see cref="ReadStreamAsync"/>, which reading the stream of a table checks too.</summary>
    /// <param name="request">The request.</param>
    internal static void CheckStreamRequest(ReadStreamRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShardIteratorType is not (ShardIteratorType.TrimHorizon or ShardIteratorType.Latest))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.ShardIteratorType, "ShardIteratorType must be TrimHorizon or Latest.");
        }

        if (!IsEmptyPageDelayValid(request.EmptyPageDelay))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.EmptyPageDelay, EmptyPageDelayMessage);
        }
    }

    /// <summary>Counts a change that a typed read of a table skipped because its type could not read it, under the call that brought the record.</summary>
    /// <param name="tableName">The table.</param>
    internal void CountSkippedItem(string tableName) => _core.CountSkippedItem(GetRecordsOperation.Name, tableName);

    /// <summary>
    /// Waits as a reader waits on an idle stream: <paramref name="delay"/> on <see cref="DynamoDbClientOptions.TimeProvider"/>, or, on a server that tells, until
    /// <paramref name="changed"/> completes if that is sooner.
    /// </summary>
    /// <param name="delay">How long.</param>
    /// <param name="changed">
    /// What <see cref="NextStreamChange"/> gave before the call whose answer made the reader wait, so that a change in between ends the wait at once; <see langword="null"/> against DynamoDB.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The wait.</returns>
    internal Task IdleAsync(TimeSpan delay, Task? changed, CancellationToken cancellationToken) =>
        changed is null ? Task.Delay(delay, _core.TimeProvider, cancellationToken) : IdleUntilChangedAsync(delay, changed, cancellationToken);

    /// <summary>
    /// Reads a shard as <see cref="ReadShardAsync"/> does, for a request that is checked. With a <paramref name="run"/>, as <see cref="ReadStreamAsync"/> reads, a transient failure
    /// of a call is waited out and the call sent again, and the run hears when the shard first holds a position.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="run">The retries and the signal of a shard of <see cref="ReadStreamAsync"/>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The records.</returns>
    internal async IAsyncEnumerable<ChangeRecord> ReadShardCoreAsync(ReadShardRequest request, ShardRun? run, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? last = null;
        var iterator = await IteratorAsync(request, last, run, cancellationToken).ConfigureAwait(false);
        run?.Positioned();

        // True while the iterator comes from GetShardIterator and has not been used: one that expired then would expire again.
        var fresh = true;
        while (true)
        {
            if (iterator is null)
            {
                yield break;
            }

            var changed = NextStreamChange?.Invoke();
            var page = await PageAsync(iterator, request.Limit, renewable: !fresh, run, cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                if (last is null && request.ShardIteratorType == ShardIteratorType.Latest)
                {
                    // Latest again is the only position left, since DynamoDB Streams has no position by time, and it passes over what was written while the iterator was expired.
                    _core.ReportStreamGap(request.ShardId);
                    request.OnGap?.Invoke(request.ShardId);
                }

                iterator = await IteratorAsync(request, last, run, cancellationToken).ConfigureAwait(false);
                fresh = true;
                continue;
            }

            fresh = false;
            for (var i = 0; i < page.Records.Count; i++)
            {
                var record = page.Records[i];
                if (record.DynamoDb?.SequenceNumber is { } sequenceNumber)
                {
                    last = sequenceNumber;
                }

                yield return record;
            }

            iterator = page.NextShardIterator;
            if (iterator is not null && page.Records.Count == 0)
            {
                await IdleAsync(request.EmptyPageDelay, changed, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsEmptyPageDelayValid(TimeSpan delay) => delay >= TimeSpan.Zero && delay < IteratorLifetime;

    // The timer is cancelled when the change comes first, so that a test clock holds no wait that is over.
    private async Task IdleUntilChangedAsync(TimeSpan delay, Task changed, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayed = Task.Delay(delay, _core.TimeProvider, timer.Token);
        if (await Task.WhenAny(changed, delayed).ConfigureAwait(false) == delayed)
        {
            // Throws when the caller cancelled.
            await delayed.ConfigureAwait(false);
        }
        else
        {
            await timer.CancelAsync().ConfigureAwait(false);
        }
    }

    // Null when the iterator expired and may be renewed. A transient failure sends the same iterator again, which returns the same records, so nothing repeats and
    // nothing is skipped; it may have expired during the wait, and then it is renewed as any other.
    private async Task<GetRecordsResponse?> PageAsync(string iterator, int? limit, bool renewable, ShardRun? run, CancellationToken cancellationToken)
    {
        try
        {
            var page = await GetRecordsAsync(new GetRecordsRequest { ShardIterator = iterator, Limit = limit }, cancellationToken).ConfigureAwait(false);
            run?.Progressed();
            return page;
        }
        catch (ExpiredIteratorException) when (renewable)
        {
            return null;
        }
        catch (Exception failure) when (run is not null && run.CanRetry(failure))
        {
            await run.WaitAsync(failure, cancellationToken).ConfigureAwait(false);
        }

        // Once per retry, of which an outage has a few dozen at most.
        return await PageAsync(iterator, limit, renewable: true, run, cancellationToken).ConfigureAwait(false);
    }

    // At the position of the request until a record was returned, and after the last record returned from then on: Latest again would
    // skip what was written in between. A transient failure asks again for the same position.
    private async Task<string?> IteratorAsync(ReadShardRequest request, string? last, ShardRun? run, CancellationToken cancellationToken)
    {
        var position = new GetShardIteratorRequest
        {
            StreamArn = request.StreamArn,
            ShardId = request.ShardId,
            ShardIteratorType = last is null ? request.ShardIteratorType : ShardIteratorType.AfterSequenceNumber,
            SequenceNumber = last ?? request.SequenceNumber,
        };
        while (true)
        {
            try
            {
                return (await GetShardIteratorAsync(position, cancellationToken).ConfigureAwait(false)).ShardIterator;
            }
            catch (Exception failure) when (run is not null && run.CanRetry(failure))
            {
                await run.WaitAsync(failure, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
