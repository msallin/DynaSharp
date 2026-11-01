using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>UpdateTimeToLive</c>.</summary>
internal readonly struct UpdateTimeToLiveOperation : IOperation<UpdateTimeToLiveRequest, UpdateTimeToLiveResponse>
{
    public static string Name => "UpdateTimeToLive";

    public static string GetTableName(UpdateTimeToLiveRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, UpdateTimeToLiveRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        json.WriteStartObject("TimeToLiveSpecification"u8);
        json.WriteBoolean("Enabled"u8, request.TimeToLiveSpecification.Enabled);
        json.WriteString("AttributeName"u8, request.TimeToLiveSpecification.AttributeName);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    public static UpdateTimeToLiveResponse ReadResponse(ref DynamoDbReader reader)
    {
        TimeToLiveSpecification? specification = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TimeToLiveSpecification"u8))
            {
                specification = ReadSpecification(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new UpdateTimeToLiveResponse { TimeToLiveSpecification = specification };
    }

    private static TimeToLiveSpecification ReadSpecification(ref DynamoDbReader reader)
    {
        bool? enabled = null;
        string? attributeName = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Enabled"u8))
            {
                enabled = OperationJson.ReadBoolean(ref reader);
            }
            else if (reader.AttributeNameEquals("AttributeName"u8))
            {
                attributeName = OperationJson.ReadRequiredString(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new TimeToLiveSpecification
        {
            Enabled = enabled ?? throw new JsonException("The TimeToLiveSpecification has no Enabled."),
            AttributeName = attributeName ?? throw new JsonException("The TimeToLiveSpecification has no AttributeName."),
        };
    }
}
