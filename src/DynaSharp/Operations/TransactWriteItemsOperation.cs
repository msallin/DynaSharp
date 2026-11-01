using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>DynamoDB <c>TransactWriteItems</c>.</summary>
internal readonly struct TransactWriteItemsOperation : IOperation<TransactWriteItemsRequest, TransactWriteItemsResponse>
{
    // The length of a GUID in the "D" format, which is what fits the 36 characters that DynamoDB allows for the token.
    private const int GuidLength = 36;

    public static string Name => "TransactWriteItems";

    public static string? GetTableName(TransactWriteItemsRequest request)
    {
        var scan = default(TableScan);
        Scan(ref scan, request);
        return scan.Only;
    }

    public static void WriteRequest(DynamoDbWriter writer, TransactWriteItemsRequest request)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteStartArray("TransactItems"u8);
        var items = request.TransactItems;
        for (var i = 0; i < items.Count; i++)
        {
            WriteAction(writer, items[i]);
        }

        json.WriteEndArray();
        OperationJson.WriteReturnConsumedCapacity(json, request.ReturnConsumedCapacity);
        OperationJson.WriteReturnItemCollectionMetrics(json, request.ReturnItemCollectionMetrics);
        WriteClientRequestToken(json, request.ClientRequestToken);
        json.WriteEndObject();
    }

    public static TransactWriteItemsResponse ReadResponse(ref DynamoDbReader reader)
    {
        IReadOnlyList<ConsumedCapacity> consumedCapacity = [];
        IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> itemCollectionMetrics = FrozenDictionary<string, IReadOnlyList<ItemCollectionMetrics>>.Empty;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                consumedCapacity = OperationJson.ReadConsumedCapacityList(ref reader);
            }
            else if (reader.AttributeNameEquals("ItemCollectionMetrics"u8))
            {
                itemCollectionMetrics = OperationJson.ReadItemCollectionMetricsByTable(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new TransactWriteItemsResponse { ConsumedCapacity = consumedCapacity, ItemCollectionMetrics = itemCollectionMetrics };
    }

    public static void SetActivityTags(Activity activity, TransactWriteItemsRequest request)
    {
        var scan = TableScan.Collecting();
        Scan(ref scan, request);
        scan.SetTag(activity);
    }

    public static IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TransactWriteItemsResponse response) => response.ConsumedCapacity;

    // Every member that is set counts, also when the action is invalid because several are: DynamoDB rejects it, and the tables are
    // still the ones the request names.
    private static void Scan(ref TableScan scan, TransactWriteItemsRequest request)
    {
        var items = request.TransactItems;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.ConditionCheck is { } conditionCheck)
            {
                scan.Add(conditionCheck.TableName);
            }

            if (item.Put is { } put)
            {
                scan.Add(put.TableName);
            }

            if (item.Delete is { } delete)
            {
                scan.Add(delete.TableName);
            }

            if (item.Update is { } update)
            {
                scan.Add(update.TableName);
            }
        }
    }

    // {"Put":{...}} or {"Delete":{...}} or {"Update":{...}} or {"ConditionCheck":{...}}. What is set is written; DynamoDB rejects none and several.
    private static void WriteAction(DynamoDbWriter writer, TransactWriteItem action)
    {
        var json = writer.Json;
        json.WriteStartObject();
        if (action.ConditionCheck is { } conditionCheck)
        {
            json.WriteStartObject("ConditionCheck"u8);
            json.WriteString("TableName"u8, conditionCheck.TableName);
            OperationJson.WriteItem(writer, "Key"u8, conditionCheck.Key);
            WriteCondition(writer, conditionCheck.ConditionExpression, conditionCheck.ExpressionAttributeNames, conditionCheck.ExpressionAttributeValues, conditionCheck.ReturnValuesOnConditionCheckFailure);
            json.WriteEndObject();
        }

        if (action.Put is { } put)
        {
            json.WriteStartObject("Put"u8);
            json.WriteString("TableName"u8, put.TableName);
            OperationJson.WriteItem(writer, "Item"u8, put.Item);
            WriteCondition(writer, put.ConditionExpression, put.ExpressionAttributeNames, put.ExpressionAttributeValues, put.ReturnValuesOnConditionCheckFailure);
            json.WriteEndObject();
        }

        if (action.Delete is { } delete)
        {
            json.WriteStartObject("Delete"u8);
            json.WriteString("TableName"u8, delete.TableName);
            OperationJson.WriteItem(writer, "Key"u8, delete.Key);
            WriteCondition(writer, delete.ConditionExpression, delete.ExpressionAttributeNames, delete.ExpressionAttributeValues, delete.ReturnValuesOnConditionCheckFailure);
            json.WriteEndObject();
        }

        if (action.Update is { } update)
        {
            json.WriteStartObject("Update"u8);
            json.WriteString("TableName"u8, update.TableName);
            OperationJson.WriteItem(writer, "Key"u8, update.Key);
            json.WriteString("UpdateExpression"u8, update.UpdateExpression);
            WriteCondition(writer, update.ConditionExpression, update.ExpressionAttributeNames, update.ExpressionAttributeValues, update.ReturnValuesOnConditionCheckFailure);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    // The members that all four kinds of action share.
    private static void WriteCondition(
        DynamoDbWriter writer,
        string? conditionExpression,
        IReadOnlyDictionary<string, string>? expressionAttributeNames,
        IReadOnlyDictionary<string, AttributeValue>? expressionAttributeValues,
        ReturnValuesOnConditionCheckFailure returnValues)
    {
        var json = writer.Json;
        OperationJson.WriteStringIfSet(json, "ConditionExpression"u8, conditionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, expressionAttributeNames);
        OperationJson.WriteAttributeValueMap(writer, "ExpressionAttributeValues"u8, expressionAttributeValues);
        OperationJson.WriteReturnValuesOnConditionCheckFailure(json, returnValues);
    }

    // The token is made here, in the one place that serializes the request, and the pipeline serializes once per call: every attempt
    // of the call, retries included, sends the same body and with it the same token. That is what makes a retried transaction idempotent.
    private static void WriteClientRequestToken(Utf8JsonWriter json, string? clientRequestToken)
    {
        if (clientRequestToken is not null)
        {
            json.WriteString("ClientRequestToken"u8, clientRequestToken);
            return;
        }

        Span<byte> token = stackalloc byte[GuidLength];
        _ = Guid.NewGuid().TryFormat(token, out var written);
        json.WriteString("ClientRequestToken"u8, token[..written]);
    }
}
