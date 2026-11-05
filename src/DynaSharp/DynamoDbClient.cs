using System.Buffers;
using System.Diagnostics;
using DynaSharp.Configuration;
using DynaSharp.Credentials;
using DynaSharp.Fluent;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp;

/// <summary>
/// The client for Amazon DynamoDB: it owns the resolved configuration, the credentials and the pooled HTTP connections that every
/// request of the library runs on. The way in is <see cref="Table(string, string, string?)"/> and <see cref="Table{T}(string)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A table is the primary API: <c>client.Table("Shop", "pk", "sk")</c> gives a <see cref="Table{T}"/>, whose methods are the DynamoDB
/// operations on items. The request API, with one method per DynamoDB operation that takes and returns the request and response records, is in the
/// <c>DynaSharp.Requests</c> namespace: add <c>using DynaSharp.Requests;</c> and see <see cref="Requests.DynamoDbRequests"/>, which also
/// describes how errors, retries and telemetry behave.
/// </para>
/// <para>
/// Create one client per application (or per region and credentials) and keep it for the lifetime of the process: it holds
/// the pooled HTTP connections. Instances are thread-safe, and every operation can run concurrently with others.
/// </para>
/// <para>
/// The client is configured through <see cref="DynamoDbClientOptions"/>. Whatever the options leave unset is read from the
/// environment the way the AWS SDKs do (<c>AWS_REGION</c>, <c>AWS_ENDPOINT_URL_DYNAMODB</c>, <c>AWS_ACCESS_KEY_ID</c> and so on).
/// Configuration problems surface when the client is created, not on the first request. Requests are signed with AWS Signature
/// Version 4.
/// </para>
/// <para>
/// Dispose the client to release its connections. A client created with an <see cref="DynamoDbClientOptions.HttpHandler"/> does
/// not dispose that handler.
/// </para>
/// </remarks>
public sealed class DynamoDbClient : IDisposable
{
    private readonly TableModel _transactions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbClient"/> class that takes all settings from the environment:
    /// the region from <c>AWS_REGION</c> (or <c>AWS_DEFAULT_REGION</c>, or the selected profile in the shared AWS config file),
    /// the endpoint from <c>AWS_ENDPOINT_URL_DYNAMODB</c> (or <c>AWS_ENDPOINT_URL</c>, otherwise the public endpoint of the region)
    /// and the credentials from the default chain of the AWS SDKs, see <see cref="DefaultCredentialsProvider"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No region is configured, or a region or endpoint environment variable is invalid.</exception>
    public DynamoDbClient()
        : this(new DynamoDbClientOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbClient"/> class.
    /// </summary>
    /// <param name="options">
    /// The settings. Options that are not set fall back to the environment, see <see cref="DynamoDbClientOptions"/>. The client
    /// reads the options once; changing the options object later has no effect.
    /// </param>
    /// <remarks>
    /// Credentials are not looked up here, but with the first request. A client without credentials is created successfully and
    /// fails its first request with a <see cref="CredentialsUnavailableException"/>. When <see cref="DynamoDbClientOptions.Credentials"/>
    /// is not set, the client uses a <see cref="DefaultCredentialsProvider"/>: environment variables, then a web identity token file
    /// (IRSA on EKS), then the shared AWS files, then the container credentials endpoint (ECS, EKS Pod Identity), then the instance metadata service (EC2). It logs the source
    /// it chose and failed refreshes to <see cref="DynamoDbClientOptions.LoggerFactory"/>. The only file read when the client is
    /// created is the shared AWS config file, and only when no region is set in the options or the environment.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its <see cref="DynamoDbClientOptions.TimeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DynamoDbClientOptions.AttemptTimeout"/> is neither positive nor <see cref="Timeout.InfiniteTimeSpan"/>, or exceeds about 49.7 days; or <see cref="DynamoDbClientOptions.MaxAttempts"/> is below 1.</exception>
    /// <exception cref="ArgumentException"><see cref="DynamoDbClientOptions.ServiceUrl"/> is not an absolute http or https URL without user info, path, query and fragment.</exception>
    /// <exception cref="InvalidOperationException">No region is configured, or a region or endpoint environment variable is invalid.</exception>
    public DynamoDbClient(DynamoDbClientOptions options)
        : this(options, ProcessEnvironment.GetVariable, null, null)
    {
    }

    // The environment, the shared files, the buffer pool, the activity source, the source of the retry jitter, the limit on the
    // response body and the credential endpoints are parameters so that tests control them.
    internal DynamoDbClient(
        DynamoDbClientOptions options,
        Func<string, string?> getEnvironmentVariable,
        ArrayPool<byte>? pool,
        ActivitySource? activitySource,
        Random? random = null,
        ReadTextFile? readFile = null,
        int maxResponseLength = DynamoDbPipeline.MaxResponseLength,
        HttpMessageHandler? credentialsHandler = null)
    {
        Core = ClientCore.Create<DynamoDbClient>(AwsService.DynamoDb, options, getEnvironmentVariable, pool, activitySource, random, readFile, maxResponseLength, credentialsHandler);
        Core.Client = this;
        _transactions = TableModel.ForTransactions(Core);
    }

    /// <summary>
    /// Gets the options as the client resolved them when it was created: what it really uses, so a region from the environment or a profile and an endpoint from <c>AWS_ENDPOINT_URL</c> are here and not null, and
    /// <see cref="DynamoDbClientOptions.Credentials"/> is the provider in use, also the default chain that the client made itself. The exceptions are the endpoints that belong to the service, see the remarks. Nothing is
    /// read again, so the environment of today can say something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second client is <c>new DynamoDbClient(client.Options with { MaxAttempts = 1 })</c>. It shares the provider and with it the cached credentials, and a handler the first client was given. A handler the first
    /// client made stays its own, so <see cref="DynamoDbClientOptions.HttpHandler"/> is <see langword="null"/> there and the second client makes one too, and each outlives the other.
    /// </para>
    /// <para>
    /// <see cref="DynamoDbClientOptions.ServiceUrl"/> holds an endpoint that came from the options or from <c>AWS_ENDPOINT_URL</c>, which a copy keeps. The public endpoint that the client derived from its region, and one
    /// from <c>AWS_ENDPOINT_URL_DYNAMODB</c> (or <c>AWS_ENDPOINT_URL_DYNAMODB_STREAMS</c> for a streams client), are in use and still stay <see langword="null"/> here, on purpose: they belong to the service and the region
    /// of this client, not to a copy. So <c>client.Options with { Region = other }</c> goes to the endpoint of the new region, and <c>new DynamoDbStreamsClient(client.Options)</c> gets its own endpoint, also when
    /// <c>AWS_ENDPOINT_URL_DYNAMODB</c> is set.
    /// </para>
    /// <para>The options hold a provider and not credentials, and <see cref="DynamoDbClientOptions.ToString"/> prints only its type.</para>
    /// </remarks>
    public DynamoDbClientOptions Options => Core.Options;

    // What the extension methods of DynaSharp.Requests and the commands of the tables run on.
    internal ClientCore Core { get; }

    /// <summary>
    /// Opens a table without a schema, which reads and writes <see cref="Item"/>. It makes no request, so a table that does not exist shows up
    /// with the first command. Hold the result in a field: it is immutable and thread-safe.
    /// </summary>
    /// <param name="name">The table name, or its ARN.</param>
    /// <param name="partitionKey">The attribute name of the partition key.</param>
    /// <param name="sortKey">The attribute name of the sort key, or <see langword="null"/> if the table has none.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="partitionKey"/> is <see langword="null"/> or empty, or <paramref name="sortKey"/> is empty.</exception>
    public Table<Item> Table(string name, string partitionKey, string? sortKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(partitionKey);
        if (sortKey is { Length: 0 })
        {
            throw new ArgumentException("A sort key name cannot be empty. Leave it out for a table without one.", nameof(sortKey));
        }

        return new Table<Item>(new TableModel(Core, name, partitionKey, sortKey));
    }

    /// <summary>
    /// Opens a table as an entity type sees it: key names, indexes and discriminator come from the entity. It makes no request, so a table that does
    /// not exist shows up with the first command. Hold the result in a field: it is immutable and thread-safe.
    /// </summary>
    /// <typeparam name="T">The entity. A nested document has no key, so it does not fit here, which the constraint enforces, and neither does <see cref="Item"/>: a table without a schema is opened with <see cref="Table(string, string, string)"/>.</typeparam>
    /// <param name="name">The table name, or its ARN.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public Table<T> Table<T>(string name)
        where T : IDynamoDbEntity<T>
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return CreateTable<T>(Core, name);
    }

    /// <summary>
    /// Starts a transaction of writes (<c>TransactWriteItems</c>): up to 100 puts, updates, deletes and condition checks on any tables, which take effect
    /// together or not at all. See <see cref="TransactWriteCommand"/>.
    /// </summary>
    /// <returns>The command, which needs an action before it can be sent.</returns>
    public TransactWriteCommand TransactWrite() => new(Transaction.Start(_transactions));

    /// <summary>
    /// Starts a transaction of reads (<c>TransactGetItems</c>): up to 100 gets on any tables, which see one snapshot. See <see cref="TransactGetCommand"/>.
    /// </summary>
    /// <returns>The command.</returns>
    public TransactGetCommand TransactGet() => new(Transaction.Start(_transactions));

    /// <summary>
    /// Reads the definition and state of a table by its name (DynamoDB <c>DescribeTable</c>): status, key schema, indexes and approximate counts, which DynamoDB updates about every six hours. It is
    /// <see cref="DynamoDbRequests.DescribeTableAsync(DynamoDbClient, DescribeTableRequest, CancellationToken)"/> for the usual call, with no request to build and no <c>using DynaSharp.Requests;</c>, and it returns
    /// what that returns. A <see cref="Table{T}"/> at hand has <see cref="Table{T}.DescribeAsync"/>.
    /// </summary>
    /// <remarks>The result is eventually consistent: right after creating a table it may still be reported as missing, which <see cref="Table{T}.WaitUntilActiveAsync(CancellationToken)"/> bears with.</remarks>
    /// <param name="tableName">The table name, or its ARN.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response. <see cref="DescribeTableResponse.Table"/> holds the description, and is <see langword="null"/> only if the response omits it.</returns>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the table name is malformed.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for <c>dynamodb:DescribeTable</c> on the table.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error, or a response that is not a DynamoDB response. A throttling or server error surfaces only when the last attempt failed with it too.</exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DescribeTableResponse> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        return this.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, cancellationToken);
    }

    /// <summary>
    /// Sends a DynamoDB operation by name, with a request body that you write as JSON, and returns the response body as JSON. It is for
    /// the operations and parameters that DynaSharp does not model: an operation without a request type, such as
    /// <c>DescribeLimits</c> or <c>DescribeEndpoints</c>, or a parameter that a modeled operation does not have yet.
    /// </summary>
    /// <param name="operation">
    /// The operation name as DynamoDB knows it, for example <c>DescribeLimits</c>. It consists of ASCII letters and digits; it is
    /// sent as the <c>X-Amz-Target</c> header and is the operation of the span and the metrics of the call.
    /// </param>
    /// <param name="requestJson">
    /// The request body as UTF-8 bytes, such as <c>"""{}"""u8.ToArray()</c>: a JSON object in the format of the DynamoDB API (AWS JSON 1.0), with attribute values in their typed form,
    /// for example <c>{"TableName":"Users","Key":{"pk":{"S":"user-1"}}}</c>, or <c>{}</c> for an operation without parameters. The
    /// bytes are copied; they are not read or checked, so DynamoDB reports a request it cannot parse. The memory can be reused as
    /// soon as the returned task has completed.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The body of the response as UTF-8 JSON, exactly as DynamoDB sent it. It is not parsed, so a success response that is not JSON
    /// is returned too.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The call runs through the same pipeline as the operations of <c>DynaSharp.Requests</c>: it is signed, sent to the endpoint of
    /// the client, repeated as <see cref="DynamoDbClientOptions.MaxAttempts"/> says, bounded by
    /// <see cref="DynamoDbClientOptions.AttemptTimeout"/>, and an error response surfaces as the same <see cref="DynamoDbException"/>
    /// types, which name the operation. The retries send the same bytes again. As for every call, a retry can apply a write
    /// twice when the first attempt reached DynamoDB and its response was lost: guard such requests with a condition or an idempotency
    /// token of the operation, or set <see cref="DynamoDbClientOptions.MaxAttempts"/> to 1.
    /// </para>
    /// <para>
    /// Telemetry is that of the other calls, minus what the library cannot read from JSON it does not parse: the span is
    /// named <c>DynamoDB.&lt;operation&gt;</c> and the duration, retry and throttle measurements carry the operation, but there is no
    /// table tag and no consumed capacity. The request and the response never appear in telemetry or logs.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is empty or contains a character other than an ASCII letter or digit.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because it is not valid JSON or has a parameter the operation does not know.</exception>
    /// <exception cref="AccessDeniedException">The credentials lack permission for the operation.</exception>
    /// <exception cref="DynamoDbException">
    /// DynamoDB returns any other error, an operation it does not know, or a response that is not a DynamoDB response. A throttling or server error
    /// surfaces only when the last attempt failed with it too.
    /// </exception>
    /// <exception cref="CredentialsUnavailableException">No credentials can be found.</exception>
    /// <exception cref="HttpRequestException">The request could not be sent or the response could not be read, on every attempt.</exception>
    /// <exception cref="TimeoutException">The last attempt did not complete within <see cref="DynamoDbClientOptions.AttemptTimeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<byte[]> SendRawAsync(string operation, ReadOnlyMemory<byte> requestJson, CancellationToken cancellationToken = default) =>
        Core.SendRawAsync(operation, requestJson, cancellationToken);

    /// <summary>
    /// Runs a read-modify-write, and runs it again when it loses to another writer: 5 runs in all, with a wait between them, and the failure of the last run is rethrown. Nothing in the library does that by itself, and this is how a
    /// caller asks for it, here or on a
    /// <see cref="Table{T}"/>, which needs no client at hand for it. A run has lost
    /// when it throws a <see cref="ConditionalCheckFailedException"/>, a <see cref="TransactionConflictException"/>, or a <see cref="TransactionCanceledException"/> whose reasons include
    /// <see cref="TransactionFailure.ConditionalCheckFailed"/> or <see cref="TransactionFailure.TransactionConflict"/>. That is <see cref="ConflictKind.Any"/>, and an overload that takes a <see cref="ConflictKind"/> runs again on one of them only,
    /// for a run whose failed condition is its answer, see <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, ConflictKind, CancellationToken)"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var saved = await client.RetryOnConflictAsync(async ct =>
    /// {
    ///     var cart = await carts.Get(key).Consistent().ItemAsync(ct);
    ///     return await carts.Update(key).Set(Cart.Attrs.Lines, Merge(cart)).IfVersion(cart!.Revision).SendAsync(ct);
    /// }, ct);
    /// </code>
    /// </example>
    /// <typeparam name="T">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>, and it must read what it depends on itself, see the remarks.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <remarks>
    /// <para>
    /// <b>Read inside the delegate.</b> Each run starts from nothing, so <paramref name="operation"/> reads, decides and writes. A write built from what was read before this call
    /// carries the same old version in every run, so every run fails the same way and the retry changes nothing. Make it a strongly consistent read, because another read can miss the
    /// write this run lost to.
    /// </para>
    /// <para>
    /// <b>A condition can fail for a business reason too.</b> A failed condition does not say why, and it can just as well mean that the bucket is empty or the seat is taken. The delegate
    /// reads again, so it decides again: it sees what is there now and returns without writing. A rule the delegate cannot read, such as a check on an item that it does not load, it
    /// catches itself and turns into its result, because whatever leaves the delegate as one of the three exceptions counts as lost. A transaction runs again if any of its reasons is
    /// one of the two, whatever the other actions report, and the limit on the runs is what ends it when another reason does not pass. A run that reads nothing cannot decide again, so a failed condition there is
    /// the answer, and <see cref="ConflictKind.TransactionConflict"/> lets it surface at once.
    /// </para>
    /// <para>
    /// <b>Everything else surfaces at once:</b> any other exception of a run, also from the third run after two conflicts, and cancellation. After the last run its exception surfaces unchanged, so a
    /// <see cref="ConditionalCheckFailedException"/> of the last run says which item gave up in <see cref="ConditionalCheckFailedException.Key"/>, which holds the key of a write of that run as the request named it. Neither its
    /// message nor telemetry show the key, and a transaction has none: there <see cref="TransactionCanceledException.ItemFor"/> is the way to an item.
    /// </para>
    /// <para>
    /// <b>Keep follow-up writes outside the run.</b> A run is one read-modify-write, and what depends on its result, such as a second item or an entry of an audit log, goes after this call. Inside, it runs again
    /// with every run. A retry inside a run multiplies the attempts, 5 runs of 5 are 25, and an inner one that used up its runs throws what it last caught, which this call counts as a conflict of its own, so one
    /// lost race on the inner item starts the whole run over.
    /// </para>
    /// <para>
    /// <b>A wait.</b> Between two runs it waits a random time up to <c>min(1 s, 10 ms * 2^(run - 1))</c> on <see cref="DynamoDbClientOptions.TimeProvider"/>, so that writers that failed together
    /// do not meet again; a conflict usually clears within that. This has nothing to do with <see cref="DynamoDbClientOptions.MaxAttempts"/>: it takes nothing from the retry quota, and every
    /// call of a run has its own attempts, as it would outside.
    /// </para>
    /// <para>
    /// <b>A lost response can look like a conflict.</b> When a conditional write reaches DynamoDB and its response is lost, the next attempt of that call fails its condition on what the first
    /// attempt wrote, and the delegate then runs on top of its own write. A run that adds to what it read, a deposit or a line in a cart, adds twice. Give such a write
    /// <c>MaxAttempts(1)</c>: the lost response then surfaces as a failure of the call, and it is not retried here. A transaction does not have this problem, because the attempts of one send carry
    /// one <c>ClientRequestToken</c>.
    /// </para>
    /// <para>
    /// <b>In a transaction,</b> leave <c>Token(string)</c> out. A run that read something else sends another request, and a token says that it is the same one, which DynamoDB answers with
    /// <see cref="IdempotentParameterMismatchException"/>.
    /// </para>
    /// <para>
    /// <b>Telemetry.</b> The calls of a run have their spans and metrics as usual. On top of that <c>dynasharp.client.conflict.retries</c> counts each run that lost and is repeated, and
    /// <c>dynasharp.client.conflict.exhausted</c> each conflict passed on after the last run, both by the operation, the table and the error that the conflict names. A warning is
    /// logged when the runs are used up. Neither carries a key or a value.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item, see the remarks.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<T> RetryOnConflictAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken = default) =>
        RetryOnConflictAsync(operation, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a read-modify-write up to <paramref name="maxAttempts"/> times. See <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> for
    /// what a run is, what makes it run again and how to write one.
    /// </summary>
    /// <remarks>
    /// Every failed condition on a version means that another writer got through, so <c>n</c> writers on one item need at most <c>n</c> runs each, whatever the timing. That is the number to pass
    /// when more than five writers meet on one item. <see cref="TransactionFailure.TransactionConflict"/> has no such bound, because the transaction that was in the way may fail too.
    /// </remarks>
    /// <typeparam name="T">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<T> RetryOnConflictAsync<T>(Func<CancellationToken, ValueTask<T>> operation, int maxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, ConflictKind.Any, maxAttempts, cancellationToken);

    /// <summary>
    /// Runs a read-modify-write that returns nothing, and runs it again when it loses to another writer. See
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> for what a run is, what makes it run again and how to write one.
    /// </summary>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default) =>
        RetryOnConflictAsync(operation, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a read-modify-write that returns nothing, up to <paramref name="maxAttempts"/> times. See
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> for what a run is, what makes it run again and how to write one, and
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, int, CancellationToken)"/> for the number to pass.
    /// </summary>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed. <see cref="ConditionalCheckFailedException.Key"/> says on which item.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation, int maxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, ConflictKind.Any, maxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write, and runs it again only on the conflicts that <paramref name="retryOn"/> names: 5 runs in all, with a wait between them. See
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> for what a run is and how to write one, which is the same with <see cref="ConflictKind.Any"/>.
    /// </summary>
    /// <example>
    /// A registration whose claims are a transaction: a taken value is the answer, and only a transaction in the way is asked again.
    /// <code>
    /// var outcome = await client.RetryOnConflictAsync(async ct =>
    /// {
    ///     TransactMarker taken = default;
    ///     try
    ///     {
    ///         await client.TransactWrite().Unique(emails, claim, out taken).Put(users, user).IfNotExists().SendAsync(ct);
    ///         return Registration.Done;
    ///     }
    ///     catch (TransactionCanceledException exception) when (exception.ReasonFor(taken) == TransactionFailure.ConditionalCheckFailed)
    ///     {
    ///         return Registration.EmailTaken;
    ///     }
    /// }, ConflictKind.TransactionConflict, ct);
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <see cref="ConflictKind.TransactionConflict"/> is for a run whose failed condition is an answer that asking again does not change, such as a value that is taken, or a record that exists. It surfaces at once,
    /// with no wait and no second run, and so does anything else that is not named. A cancelled transaction runs again when any of its reasons is of a kind named here, whatever the others say, and the limit on the
    /// runs ends it when another reason does not pass.
    /// </para>
    /// <para>
    /// <see cref="ConflictKind.ConditionalCheckFailed"/> alone leaves a transaction in the way to the caller, which a run that never meets a transaction does not need either way.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">The conflicts that run it again: <see cref="ConflictKind.ConditionalCheckFailed"/>, <see cref="ConflictKind.TransactionConflict"/> or <see cref="ConflictKind.Any"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<T> RetryOnConflictAsync<T>(Func<CancellationToken, ValueTask<T>> operation, ConflictKind retryOn, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, retryOn, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write up to <paramref name="maxAttempts"/> times, again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, ConflictKind, CancellationToken)"/> for which to name, and
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, int, CancellationToken)"/> for the number to pass.
    /// </summary>
    /// <typeparam name="T">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">The conflicts that run it again: <see cref="ConflictKind.ConditionalCheckFailed"/>, <see cref="ConflictKind.TransactionConflict"/> or <see cref="ConflictKind.Any"/>.</param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<T> RetryOnConflictAsync<T>(Func<CancellationToken, ValueTask<T>> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write that returns nothing, and runs it again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, ConflictKind, CancellationToken)"/> for which to name.
    /// </summary>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">The conflicts that run it again: <see cref="ConflictKind.ConditionalCheckFailed"/>, <see cref="ConflictKind.TransactionConflict"/> or <see cref="ConflictKind.Any"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation, ConflictKind retryOn, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, retryOn, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write that returns nothing up to <paramref name="maxAttempts"/> times, again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, ConflictKind, CancellationToken)"/> for which to name, and
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, int, CancellationToken)"/> for the number to pass.
    /// </summary>
    /// <param name="operation">One run. It gets <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">The conflicts that run it again: <see cref="ConflictKind.ConditionalCheckFailed"/>, <see cref="ConflictKind.TransactionConflict"/> or <see cref="ConflictKind.Any"/>.</param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<CancellationToken, ValueTask> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>
    /// Runs a read-modify-write, and runs it again when it loses to another writer, as <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, CancellationToken)"/> does, with the number of the run: 1 for the first, 2 for the second.
    /// It is for a value read before the call, which the first run takes and every later run reads again, without a variable that the delegate clears:
    /// <c>RetryOnConflictAsync(async (run, ct) =&gt; { var current = run == 1 ? scanner : await scanners.Get(key).Consistent().ItemAsync(ct); ... }, ct)</c>.
    /// </summary>
    /// <remarks>Only the first run may take what was read before: the run after a conflict has lost to a write that this value does not hold, and taking it again fails the same way, which is what <c>DYNA218</c> reports for a version from before the call.</remarks>
    /// <typeparam name="T">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<T> RetryOnConflictAsync<T>(Func<int, CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write with the number of the run up to <paramref name="maxAttempts"/> times, again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{T}(Func{int, CancellationToken, ValueTask{T}}, CancellationToken)"/> for the number, and
    /// <see cref="RetryOnConflictAsync{T}(Func{CancellationToken, ValueTask{T}}, ConflictKind, int, CancellationToken)"/> for the rest. <see cref="ConflictKind.Any"/> and 5 are the defaults of the others.
    /// </summary>
    /// <typeparam name="T">What a run returns.</typeparam>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">The conflicts that run it again: <see cref="ConflictKind.ConditionalCheckFailed"/>, <see cref="ConflictKind.TransactionConflict"/> or <see cref="ConflictKind.Any"/>.</param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>What the run that got through returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task<T> RetryOnConflictAsync<T>(Func<int, CancellationToken, ValueTask<T>> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>Runs a read-modify-write that returns nothing, with the number of the run. See <see cref="RetryOnConflictAsync{T}(Func{int, CancellationToken, ValueTask{T}}, CancellationToken)"/>.</summary>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of the last run failed.</exception>
    /// <exception cref="TransactionConflictException">The last run met an ongoing transaction.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<int, CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, ConflictKind.Any, ConflictRetry.DefaultMaxAttempts, cancellationToken);

    /// <summary>
    /// Runs a write that returns nothing, with the number of the run, up to <paramref name="maxAttempts"/> times, again only on the conflicts that <paramref name="retryOn"/> names. See
    /// <see cref="RetryOnConflictAsync{T}(Func{int, CancellationToken, ValueTask{T}}, ConflictKind, int, CancellationToken)"/>.
    /// </summary>
    /// <param name="operation">One run. It gets the number of the run and <paramref name="cancellationToken"/>.</param>
    /// <param name="retryOn">The conflicts that run it again: <see cref="ConflictKind.ConditionalCheckFailed"/>, <see cref="ConflictKind.TransactionConflict"/> or <see cref="ConflictKind.Any"/>.</param>
    /// <param name="maxAttempts">How many runs there can be, the first one included, so 1 means no second run.</param>
    /// <param name="cancellationToken">Goes to each run, and ends a wait between two runs at once.</param>
    /// <returns>A task that completes when a run got through.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retryOn"/> is not one of the three values, or <paramref name="maxAttempts"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled during a wait, with that token. A run reports it in its own way.</exception>
    /// <exception cref="ConditionalCheckFailedException">The condition of a run failed, of the last run when <paramref name="retryOn"/> names it, else of the first that failed.</exception>
    /// <exception cref="TransactionConflictException">A run met an ongoing transaction, the last run when <paramref name="retryOn"/> names it, else the first that met one.</exception>
    /// <exception cref="TransactionCanceledException">The last run was cancelled with a named conflict among its reasons, or any run was cancelled with none.</exception>
    public Task RetryOnConflictAsync(Func<int, CancellationToken, ValueTask> operation, ConflictKind retryOn, int maxAttempts, CancellationToken cancellationToken = default) =>
        ConflictRetry.StartAsync(Core, operation, retryOn, maxAttempts, cancellationToken);

    /// <summary>
    /// Releases the HTTP connections. Requests still running may fail. Calling it again has no effect.
    /// </summary>
    /// <remarks>
    /// The handler given in <see cref="DynamoDbClientOptions.HttpHandler"/> is not disposed; its owner does that.
    /// </remarks>
    public void Dispose() => Core.Dispose();

    internal static Table<T> CreateTable<T>(ClientCore core, string name)
        where T : IDynamoDbEntity<T>
    {
        var info = T.Info;
        var type = typeof(T).ToString();

        // Shop.Orders+Order goes by Order.
        var readTypeName = type[(type.LastIndexOfAny(['.', '+']) + 1)..];
        return new Table<T>(new TableModel(core, name, info.PartitionKeyName, info.SortKeyName, info.Discriminator, info.DiscriminatorAttributeName) { Writes = EntityWrites.Of(type, info), Entity = info, ReadTypeName = readTypeName });
    }
}
