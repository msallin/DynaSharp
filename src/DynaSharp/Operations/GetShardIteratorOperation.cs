using DynaSharp.Http;
using DynaSharp.Serialization;
using DynaSharp.Streams;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB Streams <c>GetShardIterator</c>. The iterator in the response is a credential and never reaches telemetry.</summary>
internal readonly struct GetShardIteratorOperation : IOperation<GetShardIteratorRequest, GetShardIteratorResponse>
{
    public static string Name => "GetShardIterator";

    public static string TargetPrefix => WireProtocol.StreamsTargetPrefix;

    public static string ServiceName => TelemetryNames.RpcServiceStreamsValue;

    public static string? GetTableName(GetShardIteratorRequest request) => null;

    public static void WriteRequest(DynamoDbWriter writer, GetShardIteratorRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("StreamArn"u8, request.StreamArn);
        json.WriteString("ShardId"u8, request.ShardId);
        WireNames.ShardIteratorTypes.Write(json, "ShardIteratorType"u8, request.ShardIteratorType);
        OperationJson.WriteStringIfSet(json, "SequenceNumber"u8, request.SequenceNumber);
        json.WriteEndObject();
    }

    public static GetShardIteratorResponse ReadResponse(ref DynamoDbReader reader)
    {
        string? shardIterator = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ShardIterator"u8))
            {
                shardIterator = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new GetShardIteratorResponse { ShardIterator = shardIterator };
    }
}
