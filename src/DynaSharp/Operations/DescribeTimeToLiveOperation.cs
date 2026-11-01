using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>DescribeTimeToLive</c>.</summary>
internal readonly struct DescribeTimeToLiveOperation : IOperation<DescribeTimeToLiveRequest, DescribeTimeToLiveResponse>
{
    public static string Name => "DescribeTimeToLive";

    public static string GetTableName(DescribeTimeToLiveRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, DescribeTimeToLiveRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        json.WriteEndObject();
    }

    public static DescribeTimeToLiveResponse ReadResponse(ref DynamoDbReader reader)
    {
        TimeToLiveDescription? description = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TimeToLiveDescription"u8))
            {
                description = ReadDescription(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new DescribeTimeToLiveResponse { TimeToLiveDescription = description };
    }

    private static TimeToLiveDescription ReadDescription(ref DynamoDbReader reader)
    {
        var status = TimeToLiveStatus.Unknown;
        string? attributeName = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TimeToLiveStatus"u8))
            {
                status = OperationJson.ReadEnum(ref reader, WireNames.TimeToLiveStatuses);
            }
            else if (reader.AttributeNameEquals("AttributeName"u8))
            {
                attributeName = OperationJson.ReadString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new TimeToLiveDescription { TimeToLiveStatus = status, AttributeName = attributeName };
    }
}
