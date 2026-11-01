using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Streams;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>
/// DynamoDB Streams <c>GetRecords</c>. The shard iterator, in the request and in the response, is a credential and never reaches
/// telemetry; neither do the keys and images of the records.
/// </summary>
internal readonly struct GetRecordsOperation : IOperation<GetRecordsRequest, GetRecordsResponse>
{
    public static string Name => "GetRecords";

    public static string TargetPrefix => WireProtocol.StreamsTargetPrefix;

    public static string ServiceName => TelemetryNames.RpcServiceStreamsValue;

    public static string? GetTableName(GetRecordsRequest request) => null;

    public static void WriteRequest(DynamoDbWriter writer, GetRecordsRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("ShardIterator"u8, request.ShardIterator);
        OperationJson.WriteInt32IfSet(json, "Limit"u8, request.Limit);
        json.WriteEndObject();
    }

    public static GetRecordsResponse ReadResponse(ref DynamoDbReader reader) => ReadResponse(ref reader, ArrayPool<ChangeRecord>.Shared);

    // The pool is a parameter so that tests can verify that the builder's array is returned.
    public static GetRecordsResponse ReadResponse(ref DynamoDbReader reader, ArrayPool<ChangeRecord> pool)
    {
        IReadOnlyList<ChangeRecord> records = [];
        string? nextShardIterator = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Records"u8))
            {
                records = ReadRecords(ref reader, pool);
            }
            else if (reader.AttributeNameEquals("NextShardIterator"u8))
            {
                nextShardIterator = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new GetRecordsResponse { Records = records, NextShardIterator = nextShardIterator };
    }

    public static void SetActivityTags(Activity activity, GetRecordsRequest request) => ReadTags.SetLimit(activity, request.Limit);

    private static ReadOnlyCollection<ChangeRecord> ReadRecords(ref DynamoDbReader reader, ArrayPool<ChangeRecord> pool)
    {
        OperationJson.ReadStartArray(ref reader);
        var records = new ArrayBuilder<ChangeRecord>(pool);
        try
        {
            while (reader.TryReadStartItem())
            {
                records.Add(ReadChangeRecord(ref reader));
            }

            return Array.AsReadOnly(records.ToArray());
        }
        finally
        {
            records.Dispose();
        }
    }

    private static ChangeRecord ReadChangeRecord(ref DynamoDbReader reader)
    {
        string? eventId = null;
        var eventName = OperationType.Unknown;
        string? eventVersion = null;
        string? eventSource = null;
        string? region = null;
        StreamRecord? dynamoDb = null;
        RecordIdentity? userIdentity = null;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("eventID"u8))
            {
                eventId = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("eventName"u8))
            {
                eventName = OperationJson.ReadEnum(ref reader, WireNames.OperationTypes);
            }
            else if (reader.AttributeNameEquals("eventVersion"u8))
            {
                eventVersion = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("eventSource"u8))
            {
                eventSource = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("awsRegion"u8))
            {
                region = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("dynamodb"u8))
            {
                dynamoDb = ReadStreamRecord(ref reader);
            }
            else if (reader.AttributeNameEquals("userIdentity"u8))
            {
                userIdentity = ReadRecordIdentity(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new ChangeRecord
        {
            EventId = eventId,
            EventName = eventName,
            EventVersion = eventVersion,
            EventSource = eventSource,
            Region = region,
            DynamoDb = dynamoDb,
            UserIdentity = userIdentity,
        };
    }

    private static StreamRecord ReadStreamRecord(ref DynamoDbReader reader)
    {
        DateTimeOffset? approximateCreationDateTime = null;
        IReadOnlyDictionary<string, AttributeValue>? keys = null;
        IReadOnlyDictionary<string, AttributeValue>? newImage = null;
        IReadOnlyDictionary<string, AttributeValue>? oldImage = null;
        string? sequenceNumber = null;
        long? sizeBytes = null;
        var streamViewType = StreamViewType.Unspecified;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ApproximateCreationDateTime"u8))
            {
                approximateCreationDateTime = OperationJson.ReadEpochSeconds(ref reader);
            }
            else if (reader.AttributeNameEquals("Keys"u8))
            {
                keys = AttributeValueConverter.ReadItem(ref reader);
            }
            else if (reader.AttributeNameEquals("NewImage"u8))
            {
                newImage = AttributeValueConverter.ReadItem(ref reader);
            }
            else if (reader.AttributeNameEquals("OldImage"u8))
            {
                oldImage = AttributeValueConverter.ReadItem(ref reader);
            }
            else if (reader.AttributeNameEquals("SequenceNumber"u8))
            {
                sequenceNumber = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("SizeBytes"u8))
            {
                sizeBytes = OperationJson.ReadInt64(ref reader);
            }
            else if (reader.AttributeNameEquals("StreamViewType"u8))
            {
                streamViewType = OperationJson.ReadEnum(ref reader, WireNames.StreamViewTypes);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new StreamRecord
        {
            ApproximateCreationDateTime = approximateCreationDateTime,
            Keys = keys,
            NewImage = newImage,
            OldImage = oldImage,
            SequenceNumber = sequenceNumber,
            SizeBytes = sizeBytes,
            StreamViewType = streamViewType,
        };
    }

    private static RecordIdentity ReadRecordIdentity(ref DynamoDbReader reader)
    {
        string? principalId = null;
        string? type = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("PrincipalId"u8))
            {
                principalId = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("Type"u8))
            {
                type = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new RecordIdentity { PrincipalId = principalId, Type = type };
    }
}
