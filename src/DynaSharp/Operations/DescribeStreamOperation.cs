using System.Collections.ObjectModel;
using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Streams;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB Streams <c>DescribeStream</c>.</summary>
internal readonly struct DescribeStreamOperation : IOperation<DescribeStreamRequest, DescribeStreamResponse>
{
    public static string Name => "DescribeStream";

    public static string TargetPrefix => WireProtocol.StreamsTargetPrefix;

    public static string ServiceName => TelemetryNames.RpcServiceStreamsValue;

    // The stream ARN contains the table name, but this library does not take ARNs apart: telemetry reports no table.
    public static string? GetTableName(DescribeStreamRequest request) => null;

    public static void WriteRequest(DynamoDbWriter writer, DescribeStreamRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("StreamArn"u8, request.StreamArn);
        OperationJson.WriteInt32IfSet(json, "Limit"u8, request.Limit);
        OperationJson.WriteStringIfSet(json, "ExclusiveStartShardId"u8, request.ExclusiveStartShardId);
        if (request.ShardFilter is { } shardFilter)
        {
            json.WriteStartObject("ShardFilter"u8);
            WireNames.ShardFilterTypes.Write(json, "Type"u8, shardFilter.Type);
            json.WriteString("ShardId"u8, shardFilter.ShardId);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    public static DescribeStreamResponse ReadResponse(ref DynamoDbReader reader)
    {
        StreamDescription? description = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("StreamDescription"u8))
            {
                description = ReadStreamDescription(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new DescribeStreamResponse { StreamDescription = description };
    }

    public static void SetActivityTags(Activity activity, DescribeStreamRequest request) => ReadTags.SetLimit(activity, request.Limit);

    private static StreamDescription ReadStreamDescription(ref DynamoDbReader reader)
    {
        string? streamArn = null;
        string? streamLabel = null;
        var streamStatus = StreamStatus.Unknown;
        var streamViewType = StreamViewType.Unspecified;
        DateTimeOffset? creationRequestDateTime = null;
        string? tableName = null;
        IReadOnlyList<KeySchemaElement> keySchema = [];
        IReadOnlyList<Shard> shards = [];
        string? lastEvaluatedShardId = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("StreamArn"u8))
            {
                streamArn = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("StreamLabel"u8))
            {
                streamLabel = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("StreamStatus"u8))
            {
                streamStatus = OperationJson.ReadEnum(ref reader, WireNames.StreamStatuses);
            }
            else if (reader.AttributeNameEquals("StreamViewType"u8))
            {
                streamViewType = OperationJson.ReadEnum(ref reader, WireNames.StreamViewTypes);
            }
            else if (reader.AttributeNameEquals("CreationRequestDateTime"u8))
            {
                creationRequestDateTime = OperationJson.ReadEpochSeconds(ref reader);
            }
            else if (reader.AttributeNameEquals("TableName"u8))
            {
                tableName = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("KeySchema"u8))
            {
                keySchema = TableJson.ReadKeySchema(ref reader);
            }
            else if (reader.AttributeNameEquals("Shards"u8))
            {
                shards = ReadShards(ref reader);
            }
            else if (reader.AttributeNameEquals("LastEvaluatedShardId"u8))
            {
                lastEvaluatedShardId = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new StreamDescription
        {
            StreamArn = streamArn,
            StreamLabel = streamLabel,
            StreamStatus = streamStatus,
            StreamViewType = streamViewType,
            CreationRequestDateTime = creationRequestDateTime,
            TableName = tableName,
            KeySchema = keySchema,
            Shards = shards,
            LastEvaluatedShardId = lastEvaluatedShardId,
        };
    }

    private static ReadOnlyCollection<Shard> ReadShards(ref DynamoDbReader reader)
    {
        var shards = new List<Shard>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            string? shardId = null;
            SequenceNumberRange? sequenceNumberRange = null;
            string? parentShardId = null;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("ShardId"u8))
                {
                    shardId = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("SequenceNumberRange"u8))
                {
                    sequenceNumberRange = ReadSequenceNumberRange(ref reader);
                }
                else if (reader.AttributeNameEquals("ParentShardId"u8))
                {
                    parentShardId = OperationJson.ReadString(ref reader);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            shards.Add(new Shard
            {
                ShardId = shardId,
                SequenceNumberRange = sequenceNumberRange,
                ParentShardId = parentShardId,
            });
        }

        return shards.AsReadOnly();
    }

    private static SequenceNumberRange ReadSequenceNumberRange(ref DynamoDbReader reader)
    {
        string? starting = null;
        string? ending = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("StartingSequenceNumber"u8))
            {
                starting = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("EndingSequenceNumber"u8))
            {
                ending = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new SequenceNumberRange { StartingSequenceNumber = starting, EndingSequenceNumber = ending };
    }
}
