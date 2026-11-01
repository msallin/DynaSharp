using System.Collections.ObjectModel;
using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Serialization;
using DynaSharp.Streams;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>DynamoDB Streams <c>ListStreams</c>.</summary>
internal readonly struct ListStreamsOperation : IOperation<ListStreamsRequest, ListStreamsResponse>
{
    public static string Name => "ListStreams";

    public static string TargetPrefix => WireProtocol.StreamsTargetPrefix;

    public static string ServiceName => TelemetryNames.RpcServiceStreamsValue;

    // The request may name a table; without one it lists the streams of all tables.
    public static string? GetTableName(ListStreamsRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, ListStreamsRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        OperationJson.WriteStringIfSet(json, "TableName"u8, request.TableName);
        OperationJson.WriteInt32IfSet(json, "Limit"u8, request.Limit);
        OperationJson.WriteStringIfSet(json, "ExclusiveStartStreamArn"u8, request.ExclusiveStartStreamArn);
        json.WriteEndObject();
    }

    public static ListStreamsResponse ReadResponse(ref DynamoDbReader reader)
    {
        IReadOnlyList<StreamSummary> streams = [];
        string? lastEvaluatedStreamArn = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Streams"u8))
            {
                streams = ReadStreams(ref reader);
            }
            else if (reader.AttributeNameEquals("LastEvaluatedStreamArn"u8))
            {
                lastEvaluatedStreamArn = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ListStreamsResponse { Streams = streams, LastEvaluatedStreamArn = lastEvaluatedStreamArn };
    }

    public static void SetActivityTags(Activity activity, ListStreamsRequest request) => ReadTags.SetLimit(activity, request.Limit);

    private static ReadOnlyCollection<StreamSummary> ReadStreams(ref DynamoDbReader reader)
    {
        var streams = new List<StreamSummary>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            string? streamArn = null;
            string? tableName = null;
            string? streamLabel = null;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("StreamArn"u8))
                {
                    streamArn = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("TableName"u8))
                {
                    tableName = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("StreamLabel"u8))
                {
                    streamLabel = OperationJson.ReadString(ref reader);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            streams.Add(new StreamSummary
            {
                StreamArn = streamArn,
                TableName = tableName,
                StreamLabel = streamLabel,
            });
        }

        return streams.AsReadOnly();
    }
}
