using Microsoft.Extensions.Logging;

namespace DynaSharp.Telemetry;

/// <summary>
/// Log messages of the client. Only events outside a single request are logged: the configuration when the client is created, and
/// the retry and clock events that a caller of a single operation cannot see. Event ids are stable; add new messages with the next
/// free id.
/// </summary>
internal static partial class ClientLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "DynamoDB client uses region {Region} (source: {Source}).")]
    public static partial void RegionResolved(ILogger logger, string region, string source);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "DynamoDB client uses endpoint {Endpoint} (source: {Source}).")]
    public static partial void EndpointResolved(ILogger logger, Uri endpoint, string source);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "DynamoDB client uses credentials provider {CredentialsProvider}.")]
    public static partial void CredentialsProviderConfigured(ILogger logger, Type credentialsProvider);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "DynamoDB {Operation} on table {Table} failed with {ErrorType} on attempt {Attempts}, the last one allowed. The error is passed on to the caller.")]
    public static partial void RetriesExhausted(ILogger logger, string operation, string table, string errorType, int attempts);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "DynamoDB {Operation} on table {Table} failed with {ErrorType} on attempt {Attempts}. The retry quota of the client is used up, so the request is not repeated and the error is passed on to the caller.")]
    public static partial void RetryQuotaExhausted(ILogger logger, string operation, string table, string errorType, int attempts);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "The local clock differs from the DynamoDB clock by {Offset}. Requests are signed with the corrected time from now on.")]
    public static partial void ClockSkewCorrected(ILogger logger, TimeSpan offset);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "DynamoDB {Operation} on table {Table} gave up with {Unprocessed} actions unprocessed after {Requests} requests. The caller gets them in an exception.")]
    public static partial void BatchIncomplete(ILogger logger, string operation, string table, int unprocessed, int requests);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "DynamoDB {Operation} on table {Table} lost to another writer with {ErrorType} on run {Runs} of RetryOnConflictAsync, the last one allowed. The error is passed on to the caller.")]
    public static partial void ConflictRunsExhausted(ILogger logger, string operation, string table, string errorType, int runs);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "DynamoDB client sends requests to {Host} over plain http. The requests, their signatures and the session token travel unencrypted.")]
    public static partial void UnencryptedEndpoint(ILogger logger, string host);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "DynamoDB Streams shard {ShardId} was read to its end. Child shards to read next: {Children}.")]
    public static partial void ShardFinished(ILogger logger, string shardId, int children);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "DynamoDB Streams listed the child shard {ChildShardId} of shard {ShardId} only {Elapsed} after the first look for its children.")]
    public static partial void ChildListedLate(ILogger logger, string childShardId, string shardId, TimeSpan elapsed);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "DynamoDB Streams shard {ShardId} failed with {ErrorType} on the last attempt of a call. ReadStreamAsync reads it on from where it stopped, with a backoff, for up to {Window}, and then passes the error on.")]
    public static partial void ShardRetrying(ILogger logger, string shardId, string errorType, TimeSpan window);

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "DynamoDB Streams shard {ShardId} is read again, {Elapsed} after it failed and after {Retries} retries.")]
    public static partial void ShardRecovered(ILogger logger, string shardId, TimeSpan elapsed, int retries);
}
