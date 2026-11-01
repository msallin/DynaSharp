using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>DeleteTable</c>.</summary>
internal readonly struct DeleteTableOperation : IOperation<DeleteTableRequest, DeleteTableResponse>
{
    public static string Name => "DeleteTable";

    public static string GetTableName(DeleteTableRequest request) => request.TableName;

    public static void WriteRequest(DynamoDbWriter writer, DeleteTableRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteString("TableName"u8, request.TableName);
        json.WriteEndObject();
    }

    public static DeleteTableResponse ReadResponse(ref DynamoDbReader reader) =>
        new() { TableDescription = TableJson.ReadTableDescriptionEnvelope(ref reader, Name) };
}
