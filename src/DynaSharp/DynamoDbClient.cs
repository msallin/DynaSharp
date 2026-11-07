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
/// The client for Amazon DynamoDB: it owns the resolved configuration, the credentials and the HTTP connections that every
/// request of the library runs on. The way in is <see cref="Table{T}()"/> for an entity that declares its table, <see cref="Table{T}(string)"/> to name it, and <see cref="Table(string, string, string?)"/> for a table without a model.
/// </summary>
/// <remarks>
/// <para>
/// A table is the primary API: <c>client.Table("Shop", "pk", "sk")</c> gives a <see cref="DynaSharp.Table{T}"/>, whose methods are the DynamoDB
/// operations on items. The request API, with one method per DynamoDB operation that takes and returns the request and response records, is in the
/// <c>DynaSharp.Requests</c> namespace: add <c>using DynaSharp.Requests;</c> and see <see cref="Requests.DynamoDbRequests"/>, which also
/// describes how errors, retries and telemetry behave.
/// </para>
/// <para>
/// Create one client per application (or per region and credentials) and keep it for the lifetime of the process: it holds
/// the HTTP connections. Instances are thread-safe, and every operation can run concurrently with others.
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
    /// Gets the options as the client resolved them when it was created: the region, the endpoint and the credentials provider it really uses, also those that came from the environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resolved once.</b> A region from the environment or a profile and an endpoint from <c>AWS_ENDPOINT_URL</c> are here and not <see langword="null"/>, and
    /// <see cref="DynamoDbClientOptions.Credentials"/> is the provider in use, also the default chain that the client made itself. Nothing is read again, so the environment of today can say something else.
    /// </para>
    /// <para>
    /// <b>A second client.</b> It is <c>new DynamoDbClient(client.Options with { MaxAttempts = 1 })</c>. It shares the provider and with it the cached credentials, and a handler the first client was given. A handler the first
    /// client made stays its own, so <see cref="DynamoDbClientOptions.HttpHandler"/> is <see langword="null"/> there and the second client makes one too, and each outlives the other.
    /// </para>
    /// <para>
    /// <b>The endpoint of the service stays out.</b> <see cref="DynamoDbClientOptions.ServiceUrl"/> holds an endpoint that came from the options or from <c>AWS_ENDPOINT_URL</c>, which a copy keeps. The public endpoint that the client derived from its region, and one
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
    /// <param name="name">The table name as deployed, or its ARN, used exactly as given: <see cref="DynamoDbClientOptions.TableNaming"/> does not apply, and <see cref="ResolveTableName"/> applies it to a declared name.</param>
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
    /// <param name="name">The table name as deployed, or its ARN, used exactly as given: <see cref="DynamoDbClientOptions.TableNaming"/> does not apply. <see cref="Table{T}()"/> opens the table the entity declares.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public Table<T> Table<T>(string name)
        where T : IDynamoDbEntity<T>
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return CreateTable<T>(Core, name);
    }

    /// <summary>
    /// Opens the table an entity declares, through <see cref="DynamoDbEntityAttribute.Table"/> or <see cref="DynamoDbDefaultsAttribute.Table"/>, as <see cref="DynamoDbClientOptions.TableNaming"/> names it.
    /// It makes no request, so a table that does not exist shows up with the first command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Declared, then named.</b> The declaration of the entity wins over the default of its assembly, and <see cref="DynamoDbClientOptions.TableNaming"/>, if set, turns the result into the name
    /// the client opens, such as <c>dev-orders</c> for <c>orders</c>. <see cref="Table{T}(string)"/> skips both and opens the name it is given.
    /// </para>
    /// <para>
    /// <b>Hold the result.</b> The table is immutable and thread-safe, so a field or a singleton keeps it. Every call runs <see cref="DynamoDbClientOptions.TableNaming"/> again.
    /// </para>
    /// <para>
    /// <b>No declaration.</b> An entity of the compilation that declares no table is <see href="https://msallin.github.io/DynaSharp/diagnostics.html#commands">DYNA238</see> at the call, and an entity
    /// from elsewhere throws here.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity, which declares its table.</typeparam>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> declares no table.</exception>
    /// <example>
    /// <code>
    /// // Order declares [DynamoDbEntity(Table = "Shop")], or the assembly [assembly: DynamoDbDefaults(Table = "Shop")].
    /// using var client = new DynamoDbClient(new DynamoDbClientOptions { TableNaming = name => $"dev-{name}" });
    /// var orders = client.Table&lt;Order&gt;();        // "dev-Shop"
    /// var scratch = client.Table&lt;Order&gt;("test-1"); // "test-1", as given
    /// </code>
    /// </example>
    public Table<T> Table<T>()
        where T : IDynamoDbEntity<T> => CreateTable<T>(Core, ResolveTableName(DeclaredTable.Of<T>()));

    /// <summary>
    /// Gives the name of the table this client opens for a declared name, after <see cref="DynamoDbClientOptions.TableNaming"/>, for code that has the name and no entity:
    /// <c>client.DescribeTableAsync(client.ResolveTableName("Shop"), ct)</c>. Without <see cref="DynamoDbClientOptions.TableNaming"/> it is the declared name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same as <see cref="Table{T}()"/>.</b> It is the name that <see cref="Table{T}()"/> opens for an entity that declares <paramref name="declaredName"/>, so a health check or an
    /// administration script that names the table as the entities declare it reaches the table of the environment. The members that take a table name, such as
    /// <see cref="DescribeTableAsync(string, CancellationToken)"/>, <see cref="Table(string, string, string)"/> and the requests of <c>DynaSharp.Requests</c>, take the name as deployed and do not apply it.
    /// </para>
    /// <para>
    /// <b>Every call runs the function.</b> A result of <see langword="null"/> or an empty name gives <paramref name="declaredName"/>, any other result is returned unchecked, and an exception
    /// of the function reaches the caller as it is.
    /// </para>
    /// </remarks>
    /// <param name="declaredName">The name as an entity declares it, in <see cref="DynamoDbEntityAttribute.Table"/> or <see cref="DynamoDbDefaultsAttribute.Table"/>.</param>
    /// <returns>The name of the table to open, or its ARN.</returns>
    /// <exception cref="ArgumentException"><paramref name="declaredName"/> is <see langword="null"/> or empty.</exception>
    public string ResolveTableName(string declaredName)
    {
        ArgumentException.ThrowIfNullOrEmpty(declaredName);
        return Core.Options.TableNaming?.Invoke(declaredName) is { Length: > 0 } name ? name : declaredName;
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
    /// Reads the definition and state of a table by its name (<c>DescribeTable</c>): status, key schema, indexes and approximate counts, which DynamoDB updates about every six hours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is <see cref="DynamoDbRequests.DescribeTableAsync(DynamoDbClient, DescribeTableRequest, CancellationToken)"/> without a request to build and without <c>using DynaSharp.Requests;</c>, and it returns
    /// what that returns. A <see cref="DynaSharp.Table{T}"/> at hand has <see cref="DynaSharp.Table{T}.DescribeTableAsync"/>.
    /// </para>
    /// <para>The result is eventually consistent: right after creating a table it may still be reported as missing, which <see cref="DynaSharp.Table{T}.WaitUntilActiveAsync(CancellationToken)"/> bears with.</para>
    /// </remarks>
    /// <param name="tableName">
    /// The table name as deployed, or its ARN, used exactly as given: <see cref="DynamoDbClientOptions.TableNaming"/> does not apply, so a declared name goes through <see cref="ResolveTableName"/> first.
    /// </param>
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
