using System.Diagnostics;
using System.Diagnostics.Metrics;
using DynaSharp.Errors;
using DynaSharp.Http;
using DynaSharp.Requests;

namespace DynaSharp.Telemetry;

/// <summary>
/// What a client reports about its operations: one span per operation, the duration, the retry and throttle counters and the
/// consumed capacity. Tags are the operation, the table, the server and the reason; never a key or a value. One instance per client,
/// because the region and the server are tags of every measurement.
/// </summary>
internal sealed class OperationTelemetry
{
    private readonly ActivitySource _activitySource;
    private readonly TimeProvider _timeProvider;
    private readonly string _regionName;
    private readonly string _serverAddress;
    private readonly object? _serverPort;

    /// <summary>Initializes a new instance of the <see cref="OperationTelemetry"/> class.</summary>
    /// <param name="activitySource">The source of the spans.</param>
    /// <param name="timeProvider">The clock of the durations.</param>
    /// <param name="regionName">The region of the client.</param>
    /// <param name="endpoint">The endpoint of the client.</param>
    public OperationTelemetry(ActivitySource activitySource, TimeProvider timeProvider, string regionName, Uri endpoint)
    {
        _activitySource = activitySource;
        _timeProvider = timeProvider;
        _regionName = regionName;
        _serverAddress = endpoint.DnsSafeHost;

        // Boxed once for the metric tags. Per the conventions the port is only reported when it is not the default.
        _serverPort = endpoint.IsDefaultPort ? null : endpoint.Port;
    }

    /// <summary>Gets the value of <c>error.type</c> for a failure.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The DynamoDB error code, <c>timeout</c> or the name of the exception type.</returns>
    public static string GetErrorType(Exception exception) => exception switch
    {
        DynamoDbException dynamoDbException => dynamoDbException.ErrorCode,
        TimeoutException => TelemetryNames.TimeoutErrorType,
        _ => exception.GetType().ToString(),
    };

    /// <summary>Gets the value of <c>db.response.status_code</c> for a failure.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>
    /// The error code DynamoDB sent, or <see langword="null"/> if it sent none: the code for a response that was no DynamoDB response
    /// is made up by the client.
    /// </returns>
    public static string? GetResponseStatusCode(Exception exception) =>
        exception is DynamoDbException { ErrorCode: not ErrorMapper.UnrecognizedResponseCode } dynamoDbException ? dynamoDbException.ErrorCode : null;

    /// <summary>Tags a span with the capacity that a <c>CreateTable</c> or <c>UpdateTable</c> request provisions, if it sets one.</summary>
    /// <param name="activity">The span.</param>
    /// <param name="throughput">The capacity of the request.</param>
    public static void RecordProvisionedThroughput(Activity activity, ProvisionedThroughput? throughput)
    {
        if (throughput is not null)
        {
            activity.SetTag(TelemetryNames.ProvisionedReadCapacity, (double)throughput.ReadCapacityUnits);
            activity.SetTag(TelemetryNames.ProvisionedWriteCapacity, (double)throughput.WriteCapacityUnits);
        }
    }

    /// <summary>Marks a span as failed.</summary>
    /// <param name="activity">The span, if there is one.</param>
    /// <param name="errorType">See <see cref="GetErrorType"/>.</param>
    /// <param name="responseStatusCode">See <see cref="GetResponseStatusCode"/>.</param>
    public static void RecordFailure(Activity? activity, string errorType, string? responseStatusCode)
    {
        if (activity is null)
        {
            return;
        }

        // The message is left out on purpose: service messages can quote parts of the request.
        activity.SetStatus(ActivityStatusCode.Error);
        if (activity.IsAllDataRequested)
        {
            activity.SetTag(TelemetryNames.ErrorType, errorType);
            activity.SetTag(TelemetryNames.DbResponseStatusCode, responseStatusCode);
        }
    }

    /// <summary>Starts the span of an operation.</summary>
    /// <remarks>
    /// <c>db.collection.name</c> and <c>aws.dynamodb.table_names</c> are set here only for a call that addresses one table. For several
    /// tables the operation sets <c>aws.dynamodb.table_names</c> itself, and <c>db.collection.name</c> stays out (semconv: only when the
    /// call has a single collection).
    /// </remarks>
    /// <typeparam name="TCall">The struct that implements the call.</typeparam>
    /// <typeparam name="TResponse">The response type of the call.</typeparam>
    /// <param name="call">The call.</param>
    /// <param name="tableName">The table of the call, if it has a single one.</param>
    /// <returns>The span, or <see langword="null"/> if nobody listens.</returns>
    public Activity? StartActivity<TCall, TResponse>(in TCall call, string? tableName)
        where TCall : struct, IWireCall<TResponse>
    {
        var activity = _activitySource.StartActivity(call.SpanName, ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(TelemetryNames.DbSystemName, TelemetryNames.DbSystemNameValue);
            activity.SetTag(TelemetryNames.DbOperationName, call.OperationName);
            activity.SetTag(TelemetryNames.RpcSystem, TelemetryNames.RpcSystemValue);
            activity.SetTag(TelemetryNames.RpcSystemName, TelemetryNames.RpcSystemValue);
            activity.SetTag(TelemetryNames.RpcService, call.ServiceName);
            activity.SetTag(TelemetryNames.RpcMethod, call.OperationName);
            if (tableName is not null)
            {
                activity.SetTag(TelemetryNames.DbCollectionName, tableName);
                activity.SetTag(TelemetryNames.TableNames, new[] { tableName });
            }

            activity.SetTag(TelemetryNames.CloudRegion, _regionName);
            activity.SetTag(TelemetryNames.ServerAddress, _serverAddress);
            activity.SetTag(TelemetryNames.ServerPort, _serverPort);
            call.SetActivityTags(activity);
        }

        return activity;
    }

    /// <summary>
    /// Records what a successful response reports: its span tags and its consumed capacity. The operation hooks are not called unless
    /// the span records or the metric has a listener, so a client nobody observes pays for two checks.
    /// </summary>
    /// <typeparam name="TCall">The struct that implements the call.</typeparam>
    /// <typeparam name="TResponse">The response type of the call.</typeparam>
    /// <param name="activity">The span, if there is one.</param>
    /// <param name="tableName">The table of the call, if it has a single one.</param>
    /// <param name="call">The call.</param>
    /// <param name="response">The response.</param>
    public void RecordResponse<TCall, TResponse>(Activity? activity, string? tableName, in TCall call, TResponse response)
        where TCall : struct, IWireCall<TResponse>
        => RecordResponse<TCall, TResponse>(activity, tableName, in call, response, DynaSharpTelemetry.ConsumedCapacity.Enabled);

    // The metric's listeners are process-wide, so only a parameter makes "nobody observes" reproducible next to tests that listen.
    public void RecordResponse<TCall, TResponse>(Activity? activity, string? tableName, in TCall call, TResponse response, bool metering)
        where TCall : struct, IWireCall<TResponse>
    {
        var recording = activity is { IsAllDataRequested: true };
        if (!recording && !metering)
        {
            return;
        }

        if (recording)
        {
            call.SetResponseTags(activity!, response);
        }

        var capacities = call.GetConsumedCapacity(response);
        if (capacities is not { Count: > 0 })
        {
            return;
        }

        if (recording)
        {
            activity!.SetTag(TelemetryNames.ConsumedCapacity, ConsumedCapacityTelemetry.ToAttributeValue(capacities));
        }

        if (metering)
        {
            CountCapacity(call.OperationName, tableName, capacities);
        }
    }

    /// <summary>Records the duration of an operation.</summary>
    /// <param name="operationName">The operation.</param>
    /// <param name="tableName">The table of the call, if it has a single one.</param>
    /// <param name="startTimestamp">When the operation started, from the <see cref="TimeProvider"/> of this instance.</param>
    /// <param name="errorType">See <see cref="GetErrorType"/>, or <see langword="null"/> if the operation succeeded.</param>
    /// <param name="responseStatusCode">See <see cref="GetResponseStatusCode"/>.</param>
    public void RecordDuration(string operationName, string? tableName, long startTimestamp, string? errorType, string? responseStatusCode)
    {
        var tags = new TagList
        {
            { TelemetryNames.DbSystemName, TelemetryNames.DbSystemNameValue },
            { TelemetryNames.DbOperationName, operationName },
        };
        if (tableName is not null)
        {
            tags.Add(TelemetryNames.DbCollectionName, tableName);
        }

        AddServer(ref tags);
        if (errorType is not null)
        {
            tags.Add(TelemetryNames.ErrorType, errorType);
        }

        if (responseStatusCode is not null)
        {
            tags.Add(TelemetryNames.DbResponseStatusCode, responseStatusCode);
        }

        DynaSharpTelemetry.OperationDuration.Record(_timeProvider.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    /// <summary>Counts a throttling error, whether or not the attempt is repeated.</summary>
    /// <param name="operationName">The operation.</param>
    /// <param name="tableName">The table of the call, if it has a single one.</param>
    /// <param name="exception">The failure of the attempt.</param>
    public void CountThrottle(string operationName, string? tableName, Exception exception) =>
        Count(DynaSharpTelemetry.Throttles, operationName, tableName, exception);

    /// <summary>Counts an attempt that is repeated.</summary>
    /// <param name="operationName">The operation.</param>
    /// <param name="tableName">The table of the call, if it has a single one.</param>
    /// <param name="exception">The failure of the attempt.</param>
    public void CountRetry(string operationName, string? tableName, Exception exception) =>
        Count(DynaSharpTelemetry.Retries, operationName, tableName, exception);

    /// <summary>Counts a run of a conflict retry that lost and is repeated.</summary>
    /// <param name="operationName">The operation that met the conflict.</param>
    /// <param name="tableName">The table of that operation, if it has a single one.</param>
    /// <param name="conflict">The failure of the run.</param>
    public void CountConflictRetry(string operationName, string? tableName, Exception conflict) =>
        Count(DynaSharpTelemetry.ConflictRetries, operationName, tableName, conflict);

    /// <summary>Counts a conflict that a conflict retry passes on, because it has no run left.</summary>
    /// <param name="operationName">The operation that met the conflict.</param>
    /// <param name="tableName">The table of that operation, if it has a single one.</param>
    /// <param name="conflict">The failure of the last run.</param>
    public void CountConflictExhausted(string operationName, string? tableName, Exception conflict) =>
        Count(DynaSharpTelemetry.ConflictsExhausted, operationName, tableName, conflict);

    /// <summary>Counts an item that a read with <c>OnMappingFailure</c> skipped. The reason is always the same, so it has no <c>error.type</c>.</summary>
    /// <param name="operationName">The operation of the read.</param>
    /// <param name="tableName">The table of the read.</param>
    public void CountSkippedItem(string operationName, string tableName) =>
        Count(DynaSharpTelemetry.SkippedItems, operationName, tableName, null);

    /// <summary>
    /// Starts the span of a shard that <c>ReadStreamAsync</c> reads, and counts the shard as being read until <see cref="EndShard"/>. The span is
    /// <c>Internal</c>: the calls of the shard are its children and the clients.
    /// </summary>
    /// <param name="shardId">The shard, which is no key and no data of an item.</param>
    /// <param name="caller">The span the reading started under, or <see langword="default"/> for <see cref="Activity.Current"/>.</param>
    /// <param name="parentShard">The span of the parent shard, to link to, or <see langword="default"/> for a shard read without its parent.</param>
    /// <returns>The span, or <see langword="null"/> if nobody listens.</returns>
    public Activity? StartShard(string shardId, ActivityContext caller, ActivityContext parentShard)
    {
        AddShard(1);

        var links = parentShard == default ? null : new[] { new ActivityLink(parentShard) };
        var activity = _activitySource.StartActivity(TelemetryNames.ReadStreamSpan, ActivityKind.Internal, caller, links: links);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(TelemetryNames.DbSystemName, TelemetryNames.DbSystemNameValue);
            activity.SetTag(TelemetryNames.RpcService, TelemetryNames.RpcServiceStreamsValue);
            activity.SetTag(TelemetryNames.CloudRegion, _regionName);
            activity.SetTag(TelemetryNames.ServerAddress, _serverAddress);
            activity.SetTag(TelemetryNames.ServerPort, _serverPort);
            activity.SetTag(TelemetryNames.ShardId, shardId);
        }

        return activity;
    }

    /// <summary>
    /// Ends what <see cref="StartShard"/> started: the span, with the number of records, the number of retries if there were any, and the failure, and the count of shards being read.
    /// </summary>
    /// <param name="activity">The span, if there is one.</param>
    /// <param name="records">How many records of the shard were handed over.</param>
    /// <param name="retries">How many times the shard was read again after a transient failure.</param>
    /// <param name="failure">The failure that ended the shard, or <see langword="null"/> if it ended or was stopped.</param>
    public void EndShard(Activity? activity, int records, int retries, Exception? failure)
    {
        AddShard(-1);

        if (activity is null)
        {
            return;
        }

        if (activity.IsAllDataRequested)
        {
            activity.SetTag(TelemetryNames.ShardRecords, records);
            if (retries > 0)
            {
                activity.SetTag(TelemetryNames.ShardRetries, retries);
            }
        }

        if (failure is not null)
        {
            RecordFailure(activity, GetErrorType(failure), GetResponseStatusCode(failure));
        }

        activity.Dispose();
    }

    /// <summary>Counts a record that <c>ReadStreamAsync</c> handed to its caller.</summary>
    public void CountStreamRecord()
    {
        if (!DynaSharpTelemetry.StreamRecords.Enabled)
        {
            return;
        }

        var tags = default(TagList);
        AddServer(ref tags);
        DynaSharpTelemetry.StreamRecords.Add(1, tags);
    }

    /// <summary>Counts a retry of a shard that <c>ReadStreamAsync</c> reads, after a transient failure.</summary>
    /// <param name="failure">The failure, whose type is the <c>error.type</c>.</param>
    public void CountStreamRetry(Exception failure)
    {
        if (!DynaSharpTelemetry.StreamRetries.Enabled)
        {
            return;
        }

        var tags = default(TagList);
        AddServer(ref tags);
        tags.Add(TelemetryNames.ErrorType, GetErrorType(failure));
        DynaSharpTelemetry.StreamRetries.Add(1, tags);
    }

    // Once per shard and not per record, so a check of Enabled would save nothing worth its branch.
    private void AddShard(int delta)
    {
        var tags = default(TagList);
        AddServer(ref tags);
        DynaSharpTelemetry.StreamShards.Add(delta, tags);
    }

    // A call that spans several tables has no table tag. The server is what tells the counters of two clients apart, as it does for
    // the duration.
    private void Count(Counter<long> counter, string operationName, string? tableName, Exception? exception)
    {
        if (!counter.Enabled)
        {
            return;
        }

        var tags = new TagList { { TelemetryNames.DbOperationName, operationName } };
        if (tableName is not null)
        {
            tags.Add(TelemetryNames.DbCollectionName, tableName);
        }

        AddServer(ref tags);
        if (exception is not null)
        {
            tags.Add(TelemetryNames.ErrorType, GetErrorType(exception));
        }

        counter.Add(1, tags);
    }

    // One measurement per table in the response. Capacity units are the total, which includes the indexes. The table is the one
    // DynamoDB names in the response, or the request's when it names none.
    private void CountCapacity(string operationName, string? tableName, IReadOnlyList<ConsumedCapacity> capacities)
    {
        foreach (var capacity in capacities)
        {
            if (capacity.CapacityUnits is not { } units)
            {
                continue;
            }

            var tags = new TagList { { TelemetryNames.DbOperationName, operationName } };
            if ((capacity.TableName ?? tableName) is { } collection)
            {
                tags.Add(TelemetryNames.DbCollectionName, collection);
            }

            AddServer(ref tags);
            DynaSharpTelemetry.ConsumedCapacity.Add(units, tags);
        }
    }

    private void AddServer(ref TagList tags)
    {
        tags.Add(TelemetryNames.ServerAddress, _serverAddress);
        if (_serverPort is not null)
        {
            tags.Add(TelemetryNames.ServerPort, _serverPort);
        }
    }
}
