using System.Buffers;
using System.Diagnostics;
using System.Text.Json.Nodes;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Streams;
using Microsoft.Extensions.Logging;

namespace DynaSharp.UnitTests;

/// <summary>Creates clients, requests and canned responses that the tests share.</summary>
internal static class TestClient
{
    /// <summary>
    /// The <c>[NotInParallel]</c> key of every test that reads or changes the variables of the process, for example through
    /// <c>new DynamoDbClient()</c> or <c>new EnvironmentCredentialsProvider()</c>.
    /// </summary>
    public const string ProcessEnvironment = "process environment";

    public const string StreamArn = "arn:aws:dynamodb:eu-central-2:123456789012:table/Users/stream/2026-10-31T12:00:00.000";

    /// <summary>A recognizable iterator, so that a test can look for it where it must not appear.</summary>
    public const string ShardIterator = "iterator-secret-5d1a3e9b";

    public const string GetItemResponseJson = """{"Item":{"pk":{"S":"user-1"},"name":{"S":"Alice"}}}""";

    /// <summary>
    /// The attempt timeout of the clients that <see cref="Create"/> makes unless a test says otherwise. Most tests run a stub on the real clock, where no answer may ever count as late, however
    /// loaded the machine, so this is far above what a whole run takes. A test about the timeout gives the client a timeout of its own and moves its own clock by that: nothing may wait for this
    /// one to run out, and a clock that a test moves by less than this never gets there.
    /// </summary>
    public static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromHours(1);

    public static AwsRegion Region { get; } = AwsRegion.FromName("eu-central-2");

    public static AwsCredentials Credentials { get; } = new("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");

    /// <summary>
    /// Creates a client that talks to <paramref name="handler"/>. Nothing is read from the environment. The client does not retry
    /// unless <paramref name="maxAttempts"/> says so: a test that is not about retries must neither wait for a backoff nor see a
    /// second request.
    /// </summary>
    public static DynamoDbClient Create(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        ArrayPool<byte>? pool = null,
        TimeSpan? attemptTimeout = null,
        Uri? serviceUrl = null,
        ICredentialsProvider? credentials = null,
        ILoggerFactory? loggerFactory = null,
        ActivitySource? activitySource = null,
        int maxAttempts = 1,
        Random? random = null,
        int? maxResponseLength = null)
    {
        var options = Options(handler, timeProvider, attemptTimeout, serviceUrl, credentials, loggerFactory, maxAttempts);
        return maxResponseLength is { } limit
            ? new DynamoDbClient(options, static _ => null, pool, activitySource, random, maxResponseLength: limit)
            : new DynamoDbClient(options, static _ => null, pool, activitySource, random);
    }

    /// <summary>
    /// Creates a streams client that talks to <paramref name="handler"/>, with the same defaults as <see cref="Create"/>: nothing is
    /// read from the environment and the client does not retry unless <paramref name="maxAttempts"/> says so.
    /// </summary>
    public static DynamoDbStreamsClient CreateStreams(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        ArrayPool<byte>? pool = null,
        TimeSpan? attemptTimeout = null,
        Uri? serviceUrl = null,
        ICredentialsProvider? credentials = null,
        ILoggerFactory? loggerFactory = null,
        ActivitySource? activitySource = null,
        int maxAttempts = 1,
        Random? random = null)
    {
        var options = Options(handler, timeProvider, attemptTimeout, serviceUrl, credentials, loggerFactory, maxAttempts);
        return new DynamoDbStreamsClient(options, static _ => null, pool, activitySource, random);
    }

    public static DynamoDbClientOptions Options(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        TimeSpan? attemptTimeout = null,
        Uri? serviceUrl = null,
        ICredentialsProvider? credentials = null,
        ILoggerFactory? loggerFactory = null,
        int maxAttempts = 1) => new()
        {
            Region = Region,
            ServiceUrl = serviceUrl,
            Credentials = credentials ?? new StaticCredentialsProvider(Credentials),
            HttpHandler = handler,
            AttemptTimeout = attemptTimeout ?? DefaultAttemptTimeout,
            MaxAttempts = maxAttempts,
            TimeProvider = timeProvider ?? TimeProvider.System,
            LoggerFactory = loggerFactory,
        };

    /// <summary>
    /// Returns a host name that no other test uses. A test that cannot tell its metric measurements apart by a table, because its call
    /// names none or several, gives its client a service URL with this host and filters by it.
    /// </summary>
    /// <returns>The host name.</returns>
    public static string UniqueHost() => $"host-{Guid.NewGuid():N}.test";

    /// <summary>
    /// Returns a table name that no other test uses, for a test that tells its metric measurements apart by the table.
    /// </summary>
    /// <returns>The table name.</returns>
    public static string UniqueTable() => "table-" + Guid.NewGuid().ToString("N");

    /// <summary>Reads the <c>ClientRequestToken</c> of a serialized <c>TransactWriteItems</c> request.</summary>
    /// <param name="json">The request body.</param>
    /// <returns>The token.</returns>
    public static string TokenOf(string json) => JsonNode.Parse(json)!["ClientRequestToken"]!.GetValue<string>();

    public static GetItemRequest GetItem(string tableName = "Users", string key = "user-1") => new()
    {
        TableName = tableName,
        Key = new Dictionary<string, AttributeValue> { ["pk"] = key },
    };

    public static PutItemRequest PutItem(string tableName = "Users", string key = "user-1") => new()
    {
        TableName = tableName,
        Item = new Dictionary<string, AttributeValue> { ["pk"] = key, ["name"] = "Alice" },
    };

    public static QueryRequest CreateQuery(string tableName = "Users") => new()
    {
        TableName = tableName,
        KeyConditionExpression = "pk = :pk",
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":pk"] = "user-1" },
    };

    public static DescribeTableRequest DescribeTable(string tableName = "Users") => new() { TableName = tableName };

    public static CreateTableRequest CreateTable(string tableName = "Users") => new()
    {
        TableName = tableName,
        AttributeDefinitions = [new("pk", ScalarAttributeType.String)],
        KeySchema = [new("pk", KeyType.Hash)],
        BillingMode = BillingMode.PayPerRequest,
    };

    public static DeleteTableRequest DeleteTable(string tableName = "Users") => new() { TableName = tableName };

    public static UpdateTimeToLiveRequest UpdateTimeToLive(string tableName = "Users") => new()
    {
        TableName = tableName,
        TimeToLiveSpecification = new() { Enabled = true, AttributeName = "ttl" },
    };

    public static ListTablesRequest ListTables() => new();

    public static DescribeTimeToLiveRequest DescribeTimeToLive(string tableName = "Users") => new() { TableName = tableName };

    public static UpdateTableRequest UpdateTable(string tableName = "Users") => new()
    {
        TableName = tableName,
        BillingMode = BillingMode.PayPerRequest,
    };

    public static ListStreamsRequest ListStreams(string? tableName = null) => new() { TableName = tableName };

    public static DescribeStreamRequest DescribeStream() => new() { StreamArn = StreamArn };

    public static GetShardIteratorRequest GetShardIterator() => new() { StreamArn = StreamArn, ShardId = "shardId-1", ShardIteratorType = ShardIteratorType.TrimHorizon };

    public static GetRecordsRequest GetRecords(string iterator = ShardIterator) => new() { ShardIterator = iterator };

    /// <summary>Runs an asynchronous action that must fail and returns the exception.</summary>
    public static async Task<TException> CaptureAsync<TException>(Func<Task> act)
        where TException : Exception
    {
        try
        {
            await act();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"The action did not throw {typeof(TException).Name}.");
    }

    /// <summary>Runs an action that must fail as a command that is misused fails, and returns the exception.</summary>
    public static Exception Catch(Action act)
    {
        try
        {
            act();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or DynamoDbMappingException)
        {
            return exception;
        }

        throw new InvalidOperationException("The action did not throw.");
    }
}
