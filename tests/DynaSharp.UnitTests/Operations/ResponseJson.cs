using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

/// <summary>
/// Writes a response of this library as JSON in the shape of botocore's parsed response, so that it can be compared with what
/// botocore made of the same body. Only members that are set are written; <c>Count</c> and <c>ScannedCount</c>, the list of
/// items and the collections of the batch and transaction responses are always written, because this library reports an absent
/// count as 0 and an absent collection as empty. <see cref="WithDefaults"/> adds the same to what botocore parsed.
/// </summary>
internal static partial class ResponseJson
{
    public static string Describe(UpdateItemResponse response) =>
        Build(json => WriteWriteResult(json, response.Attributes, response.ConsumedCapacity, response.ItemCollectionMetrics));

    public static string Describe(DeleteItemResponse response) =>
        Build(json => WriteWriteResult(json, response.Attributes, response.ConsumedCapacity, response.ItemCollectionMetrics));

    public static string Describe(PutItemResponse response) =>
        Build(json => WriteWriteResult(json, response.Attributes, response.ConsumedCapacity, response.ItemCollectionMetrics));

    public static string Describe(QueryResponse response) =>
        Build(json => WritePage(json, response.Items, response.Count, response.ScannedCount, response.LastEvaluatedKey, response.ConsumedCapacity));

    public static string Describe(ScanResponse response) =>
        Build(json => WritePage(json, response.Items, response.Count, response.ScannedCount, response.LastEvaluatedKey, response.ConsumedCapacity));

    public static string Describe(BatchGetItemResponse response) =>
        Build(json =>
        {
            json.WriteStartObject("Responses");
            foreach (var (table, items) in response.Responses)
            {
                json.WriteStartArray(table);
                WriteItems(json, items);
                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteStartObject("UnprocessedKeys");
            foreach (var (table, keysAndAttributes) in response.UnprocessedKeys)
            {
                json.WriteStartObject(table);
                json.WriteStartArray("Keys");
                WriteItems(json, keysAndAttributes.Keys);
                json.WriteEndArray();
                if (keysAndAttributes.ConsistentRead)
                {
                    json.WriteBoolean("ConsistentRead", true);
                }

                WriteString(json, "ProjectionExpression", keysAndAttributes.ProjectionExpression);
                if (keysAndAttributes.ExpressionAttributeNames is { } names)
                {
                    json.WriteStartObject("ExpressionAttributeNames");
                    foreach (var (placeholder, name) in names)
                    {
                        json.WriteString(placeholder, name);
                    }

                    json.WriteEndObject();
                }

                json.WriteEndObject();
            }

            json.WriteEndObject();
            WriteConsumedCapacityList(json, response.ConsumedCapacity);
        });

    public static string Describe(BatchWriteItemResponse response) =>
        Build(json =>
        {
            json.WriteStartObject("UnprocessedItems");
            foreach (var (table, writes) in response.UnprocessedItems)
            {
                json.WriteStartArray(table);
                foreach (var write in writes)
                {
                    json.WriteStartObject();
                    if (write.PutRequest is { } put)
                    {
                        json.WriteStartObject("PutRequest");
                        json.WritePropertyName("Item");
                        json.WriteRawValue(TestJson.WriteItem(put.Item));
                        json.WriteEndObject();
                    }

                    if (write.DeleteRequest is { } delete)
                    {
                        json.WriteStartObject("DeleteRequest");
                        json.WritePropertyName("Key");
                        json.WriteRawValue(TestJson.WriteItem(delete.Key));
                        json.WriteEndObject();
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
            WriteMetricsByTable(json, response.ItemCollectionMetrics);
            WriteConsumedCapacityList(json, response.ConsumedCapacity);
        });

    public static string Describe(TransactGetItemsResponse response) =>
        Build(json =>
        {
            json.WriteStartArray("Responses");
            foreach (var itemResponse in response.Responses)
            {
                json.WriteStartObject();
                WriteItemIfPresent(json, "Item", itemResponse.Item);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            WriteConsumedCapacityList(json, response.ConsumedCapacity);
        });

    public static string Describe(TransactWriteItemsResponse response) =>
        Build(json =>
        {
            WriteConsumedCapacityList(json, response.ConsumedCapacity);
            WriteMetricsByTable(json, response.ItemCollectionMetrics);
        });

    /// <summary>
    /// The cancellation reasons of a <see cref="TransactionCanceledException"/> in the shape of botocore's parsed modeled error. The message
    /// is left out: the modeled shape only reads <c>Message</c>, where DynamoDB sometimes sends <c>message</c>.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The reasons, always written.</returns>
    public static string Describe(TransactionCanceledException exception) =>
        Build(json =>
        {
            json.WriteStartArray("CancellationReasons");
            foreach (var reason in exception.CancellationReasons)
            {
                json.WriteStartObject();
                WriteString(json, "Code", reason.Code);
                WriteString(json, "Message", reason.Message);
                WriteItemIfPresent(json, "Item", reason.Item);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });

    /// <summary>Adds the members that this library reports with a default and botocore leaves out: an absent list of items.</summary>
    /// <param name="botocoreJson">What botocore parsed.</param>
    /// <returns>The same with <c>Items</c> set to an empty array if absent.</returns>
    public static string WithEmptyItemsIfAbsent(string botocoreJson) => WithDefaults(botocoreJson, ("Items", new JsonArray()));

    /// <summary>Adds the members that this library reports with a default and botocore leaves out.</summary>
    /// <param name="botocoreJson">What botocore parsed.</param>
    /// <param name="defaults">The members and the empty values to add where the object has none.</param>
    /// <returns>The same with the defaults set where absent.</returns>
    public static string WithDefaults(string botocoreJson, params (string Name, JsonNode Empty)[] defaults)
    {
        var response = JsonNode.Parse(botocoreJson)!.AsObject();
        foreach (var (name, empty) in defaults)
        {
            response.TryAdd(name, empty);
        }

        return response.ToJsonString();
    }

    internal static string Build(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static void WriteItemIfPresent(Utf8JsonWriter json, string name, IReadOnlyDictionary<string, AttributeValue>? item)
    {
        if (item is not null)
        {
            json.WritePropertyName(name);
            json.WriteRawValue(TestJson.WriteItem(item));
        }
    }

    private static void WriteWriteResult(Utf8JsonWriter json, IReadOnlyDictionary<string, AttributeValue>? attributes, ConsumedCapacity? consumedCapacity, ItemCollectionMetrics? metrics)
    {
        WriteItemIfPresent(json, "Attributes", attributes);
        WriteConsumedCapacityIfPresent(json, consumedCapacity);
        if (metrics is null)
        {
            return;
        }

        json.WriteStartObject("ItemCollectionMetrics");
        WriteMetricsMembers(json, metrics);
        json.WriteEndObject();
    }

    private static void WritePage(Utf8JsonWriter json, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> items, int count, int scannedCount, IReadOnlyDictionary<string, AttributeValue>? lastEvaluatedKey, ConsumedCapacity? consumedCapacity)
    {
        json.WriteStartArray("Items");
        WriteItems(json, items);
        json.WriteEndArray();
        json.WriteNumber("Count", count);
        json.WriteNumber("ScannedCount", scannedCount);
        WriteItemIfPresent(json, "LastEvaluatedKey", lastEvaluatedKey);
        WriteConsumedCapacityIfPresent(json, consumedCapacity);
    }

    private static void WriteItems(Utf8JsonWriter json, IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> items)
    {
        foreach (var item in items)
        {
            json.WriteRawValue(TestJson.WriteItem(item));
        }
    }

    private static void WriteMetricsMembers(Utf8JsonWriter json, ItemCollectionMetrics metrics)
    {
        WriteItemIfPresent(json, "ItemCollectionKey", metrics.ItemCollectionKey);
        if (metrics.SizeEstimateRangeGB is { } range)
        {
            json.WriteStartArray("SizeEstimateRangeGB");
            foreach (var bound in range)
            {
                json.WriteNumberValue(bound);
            }

            json.WriteEndArray();
        }
    }

    private static void WriteMetricsByTable(Utf8JsonWriter json, IReadOnlyDictionary<string, IReadOnlyList<ItemCollectionMetrics>> metricsByTable)
    {
        json.WriteStartObject("ItemCollectionMetrics");
        foreach (var (table, metrics) in metricsByTable)
        {
            json.WriteStartArray(table);
            foreach (var entry in metrics)
            {
                json.WriteStartObject();
                WriteMetricsMembers(json, entry);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteConsumedCapacityList(Utf8JsonWriter json, IReadOnlyList<ConsumedCapacity> capacities)
    {
        json.WriteStartArray("ConsumedCapacity");
        foreach (var capacity in capacities)
        {
            json.WriteStartObject();
            WriteConsumedCapacityMembers(json, capacity);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteConsumedCapacityIfPresent(Utf8JsonWriter json, ConsumedCapacity? capacity)
    {
        if (capacity is null)
        {
            return;
        }

        json.WriteStartObject("ConsumedCapacity");
        WriteConsumedCapacityMembers(json, capacity);
        json.WriteEndObject();
    }

    private static void WriteConsumedCapacityMembers(Utf8JsonWriter json, ConsumedCapacity capacity)
    {
        WriteString(json, "TableName", capacity.TableName);
        WriteNumber(json, "CapacityUnits", capacity.CapacityUnits);
        WriteNumber(json, "ReadCapacityUnits", capacity.ReadCapacityUnits);
        WriteNumber(json, "WriteCapacityUnits", capacity.WriteCapacityUnits);
        WriteCapacity(json, "Table", capacity.Table);
        WriteIndexes(json, "LocalSecondaryIndexes", capacity.LocalSecondaryIndexes);
        WriteIndexes(json, "GlobalSecondaryIndexes", capacity.GlobalSecondaryIndexes);
    }

    private static void WriteIndexes(Utf8JsonWriter json, string name, IReadOnlyDictionary<string, CapacityDetail>? indexes)
    {
        if (indexes is null)
        {
            return;
        }

        json.WriteStartObject(name);
        foreach (var (indexName, capacity) in indexes)
        {
            WriteCapacity(json, indexName, capacity);
        }

        json.WriteEndObject();
    }

    private static void WriteCapacity(Utf8JsonWriter json, string name, CapacityDetail? capacity)
    {
        if (capacity is null)
        {
            return;
        }

        json.WriteStartObject(name);
        WriteNumber(json, "CapacityUnits", capacity.CapacityUnits);
        WriteNumber(json, "ReadCapacityUnits", capacity.ReadCapacityUnits);
        WriteNumber(json, "WriteCapacityUnits", capacity.WriteCapacityUnits);
        json.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }

    private static void WriteNumber(Utf8JsonWriter json, string name, double? value)
    {
        if (value is { } number)
        {
            json.WriteNumber(name, number);
        }
    }
}
