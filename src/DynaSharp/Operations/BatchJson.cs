using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The JSON of the structures that <c>BatchGetItem</c> and <c>BatchWriteItem</c> have in their requests and, for the keys and
/// writes that were not processed, in their responses: <see cref="KeysAndAttributes"/> and <see cref="WriteRequest"/>.
/// </summary>
internal static class BatchJson
{
    // {"Keys":[{"pk":{"S":"a"}}],"ConsistentRead":true,"ProjectionExpression":"#n","ExpressionAttributeNames":{"#n":"name"}}
    public static void WriteKeysAndAttributes(DynamoDbWriter writer, KeysAndAttributes keysAndAttributes)
    {
        var json = writer.Json;
        json.WriteStartObject();
        json.WriteStartArray("Keys"u8);
        var keys = keysAndAttributes.Keys;
        for (var i = 0; i < keys.Count; i++)
        {
            AttributeValueConverter.WriteItem(writer, keys[i]);
        }

        json.WriteEndArray();
        OperationJson.WriteIfTrue(json, "ConsistentRead"u8, keysAndAttributes.ConsistentRead);
        OperationJson.WriteStringIfSet(json, "ProjectionExpression"u8, keysAndAttributes.ProjectionExpression);
        OperationJson.WriteStringMap(json, "ExpressionAttributeNames"u8, keysAndAttributes.ExpressionAttributeNames);
        json.WriteEndObject();
    }

    // {"PutRequest":{"Item":{...}}} or {"DeleteRequest":{"Key":{...}}}. What is set is written; DynamoDB rejects none and both.
    public static void WriteWriteRequest(DynamoDbWriter writer, WriteRequest write)
    {
        var json = writer.Json;
        json.WriteStartObject();
        if (write.PutRequest is { } put)
        {
            json.WriteStartObject("PutRequest"u8);
            OperationJson.WriteItem(writer, "Item"u8, put.Item);
            json.WriteEndObject();
        }

        if (write.DeleteRequest is { } delete)
        {
            json.WriteStartObject("DeleteRequest"u8);
            OperationJson.WriteItem(writer, "Key"u8, delete.Key);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    // "Responses":{"Forum":[{"Name":{"S":"a"}},{"Name":{"S":"b"}}],"Thread":[]}
    public static Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>> ReadItemsByTable(ref DynamoDbReader reader)
    {
        var byTable = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>>(StringComparer.Ordinal);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var table = reader.GetAttributeName();
            byTable[table] = OperationJson.ReadItems(ref reader);
        }

        return byTable;
    }

    // "UnprocessedKeys":{"Forum":{"Keys":[...],"ConsistentRead":true}}: the form of the request, so that it can be sent again.
    public static Dictionary<string, KeysAndAttributes> ReadKeysByTable(ref DynamoDbReader reader)
    {
        var byTable = new Dictionary<string, KeysAndAttributes>(StringComparer.Ordinal);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var table = reader.GetAttributeName();
            byTable[table] = ReadKeysAndAttributes(ref reader);
        }

        return byTable;
    }

    // "UnprocessedItems":{"Forum":[{"PutRequest":{"Item":{...}}},{"DeleteRequest":{"Key":{...}}}]}
    public static Dictionary<string, IReadOnlyList<WriteRequest>> ReadWritesByTable(ref DynamoDbReader reader)
    {
        var byTable = new Dictionary<string, IReadOnlyList<WriteRequest>>(StringComparer.Ordinal);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var table = reader.GetAttributeName();
            var writes = new List<WriteRequest>();
            OperationJson.ReadStartArray(ref reader);
            while (reader.TryReadStartItem())
            {
                writes.Add(ReadWriteRequest(ref reader));
            }

            byTable[table] = writes.AsReadOnly();
        }

        return byTable;
    }

    private static KeysAndAttributes ReadKeysAndAttributes(ref DynamoDbReader reader)
    {
        IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>>? keys = null;
        var consistentRead = false;
        string? projectionExpression = null;
        IReadOnlyDictionary<string, string>? expressionAttributeNames = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Keys"u8))
            {
                keys = OperationJson.ReadItems(ref reader);
            }
            else if (reader.AttributeNameEquals("ConsistentRead"u8))
            {
                consistentRead = OperationJson.ReadBoolean(ref reader);
            }
            else if (reader.AttributeNameEquals("ProjectionExpression"u8))
            {
                projectionExpression = OperationJson.ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("ExpressionAttributeNames"u8))
            {
                expressionAttributeNames = OperationJson.ReadStringMap(ref reader);
            }
            else
            {
                // Including AttributesToGet, which the library never sends.
                OperationJson.SkipValue(ref reader);
            }
        }

        return new KeysAndAttributes
        {
            Keys = keys ?? throw new JsonException("The keys and attributes of a table have no Keys."),
            ConsistentRead = consistentRead,
            ProjectionExpression = projectionExpression,
            ExpressionAttributeNames = expressionAttributeNames,
        };
    }

    // The reader has consumed the opening brace of the write.
    private static WriteRequest ReadWriteRequest(ref DynamoDbReader reader)
    {
        PutRequest? put = null;
        DeleteRequest? delete = null;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("PutRequest"u8))
            {
                put = ReadPutRequest(ref reader);
            }
            else if (reader.AttributeNameEquals("DeleteRequest"u8))
            {
                delete = ReadDeleteRequest(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new WriteRequest { PutRequest = put, DeleteRequest = delete };
    }

    // {"Item":{...}}
    private static PutRequest ReadPutRequest(ref DynamoDbReader reader)
    {
        Item? item = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Item"u8))
            {
                item = AttributeValueConverter.ReadItem(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new PutRequest { Item = item ?? throw new JsonException("A PutRequest has no Item.") };
    }

    // {"Key":{...}}
    private static DeleteRequest ReadDeleteRequest(ref DynamoDbReader reader)
    {
        Item? key = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Key"u8))
            {
                key = AttributeValueConverter.ReadItem(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new DeleteRequest { Key = key ?? throw new JsonException("A DeleteRequest has no Key.") };
    }
}
