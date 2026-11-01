namespace DynaSharp.Telemetry;

/// <summary>
/// Names of the activity source, the meter and their attributes, following the OpenTelemetry semantic conventions v1.44.0
/// for AWS SDK client spans (<c>cloud-providers/aws-sdk</c>), DynamoDB (<c>db/dynamodb</c>) and database client metrics.
/// </summary>
internal static class TelemetryNames
{
    public const string Source = "DynaSharp";
    public const string DurationMetric = "db.client.operation.duration";
    public const string RetriesMetric = "dynasharp.client.operation.retries";
    public const string ThrottlesMetric = "dynasharp.client.operation.throttles";
    public const string ConflictRetriesMetric = "dynasharp.client.conflict.retries";
    public const string ConflictsExhaustedMetric = "dynasharp.client.conflict.exhausted";
    public const string ConsumedCapacityMetric = "dynasharp.client.consumed_capacity";

    public const string DbSystemName = "db.system.name";
    public const string DbSystemNameValue = "aws.dynamodb";
    public const string DbOperationName = "db.operation.name";
    public const string DbCollectionName = "db.collection.name";
    public const string DbResponseStatusCode = "db.response.status_code";

    // rpc.system and rpc.service are deprecated, but the AWS SDK conventions still prescribe them, so they are emitted beside
    // rpc.system.name, which replaces rpc.system. The conventions list no well-known value of rpc.system.name for AWS; they prescribe
    // aws-api for rpc.system, and a custom value is allowed, so both carry it.
    public const string RpcSystem = "rpc.system";
    public const string RpcSystemName = "rpc.system.name";
    public const string RpcSystemValue = "aws-api";
    public const string RpcService = "rpc.service";
    public const string RpcServiceValue = "DynamoDB";
    public const string RpcServiceStreamsValue = "DynamoDB Streams";
    public const string RpcMethod = "rpc.method";

    public const string CloudRegion = "cloud.region";
    public const string ServerAddress = "server.address";
    public const string ServerPort = "server.port";
    public const string ErrorType = "error.type";
    public const string AwsRequestId = "aws.request_id";

    public const string TableNames = "aws.dynamodb.table_names";
    public const string ConsistentRead = "aws.dynamodb.consistent_read";
    public const string Projection = "aws.dynamodb.projection";
    public const string IndexName = "aws.dynamodb.index_name";
    public const string Limit = "aws.dynamodb.limit";
    public const string Select = "aws.dynamodb.select";
    public const string ScanForward = "aws.dynamodb.scan_forward";
    public const string ProvisionedReadCapacity = "aws.dynamodb.provisioned_read_capacity";
    public const string ProvisionedWriteCapacity = "aws.dynamodb.provisioned_write_capacity";
    public const string Segment = "aws.dynamodb.segment";
    public const string TotalSegments = "aws.dynamodb.total_segments";
    public const string Count = "aws.dynamodb.count";
    public const string ScannedCount = "aws.dynamodb.scanned_count";
    public const string ConsumedCapacity = "aws.dynamodb.consumed_capacity";
    public const string ExclusiveStartTable = "aws.dynamodb.exclusive_start_table";
    public const string TableCount = "aws.dynamodb.table_count";

    // Value of error.type when an attempt timed out.
    public const string TimeoutErrorType = "timeout";
}
