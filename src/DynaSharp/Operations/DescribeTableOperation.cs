using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>DescribeTable</c>.</summary>
internal readonly struct DescribeTableOperation : IOperation<DescribeTableRequest, DescribeTableResponse>
{
    public static string Name => "DescribeTable";

    public static string GetTableName(DescribeTableRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, DescribeTableRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        json.WriteEndObject();
    }

    public static DescribeTableResponse ReadResponse(ref DynamoDbReader reader)
    {
        TableDescription? table = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Table"u8))
            {
                table = TableJson.ReadTable(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new DescribeTableResponse { Table = table };
    }
}
