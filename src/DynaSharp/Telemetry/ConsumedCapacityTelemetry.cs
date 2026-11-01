using System.Buffers;
using System.Text;
using System.Text.Json;

namespace DynaSharp.Telemetry;

/// <summary>
/// How consumed capacity reaches telemetry: as the span attribute <c>aws.dynamodb.consumed_capacity</c> and as the
/// <c>dynasharp.client.consumed_capacity</c> counter. Only capacity units and table and index names are involved; the
/// <c>ItemCollectionMetrics</c> of a response contain the partition key value and are never reported.
/// </summary>
internal static class ConsumedCapacityTelemetry
{
    /// <summary>Wraps the capacity of a single-capacity response as the list the operation hook returns.</summary>
    /// <param name="consumedCapacity">The capacity of the response, or <see langword="null"/> if none was requested.</param>
    /// <returns>A list with the capacity, or <see langword="null"/> if there is none.</returns>
    public static IReadOnlyList<ConsumedCapacity>? ToList(ConsumedCapacity? consumedCapacity) =>
        consumedCapacity is null ? null : [consumedCapacity];

    /// <summary>
    /// Formats the capacities as the attribute value the conventions prescribe: one JSON text per <c>ConsumedCapacity</c> item,
    /// in the shape of the DynamoDB response (members that were not reported are left out).
    /// </summary>
    /// <param name="capacities">The capacities of a response.</param>
    /// <returns>One JSON document per capacity.</returns>
    public static string[] ToAttributeValue(IReadOnlyList<ConsumedCapacity> capacities)
    {
        var documents = new string[capacities.Count];
        var buffer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < documents.Length; i++)
        {
            buffer.ResetWrittenCount();
            using (var json = new Utf8JsonWriter(buffer))
            {
                WriteConsumedCapacity(json, capacities[i]);
            }

            documents[i] = Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        return documents;
    }

    private static void WriteConsumedCapacity(Utf8JsonWriter json, ConsumedCapacity capacity)
    {
        json.WriteStartObject();
        WriteUnits(json, capacity.CapacityUnits, capacity.ReadCapacityUnits, capacity.WriteCapacityUnits);
        if (capacity.TableName is { } tableName)
        {
            json.WriteString("TableName"u8, tableName);
        }

        if (capacity.Table is { } table)
        {
            WriteCapacityDetail(json, "Table", table);
        }

        WriteIndexes(json, "LocalSecondaryIndexes", capacity.LocalSecondaryIndexes);
        WriteIndexes(json, "GlobalSecondaryIndexes", capacity.GlobalSecondaryIndexes);
        json.WriteEndObject();
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
            WriteCapacityDetail(json, indexName, capacity);
        }

        json.WriteEndObject();
    }

    private static void WriteCapacityDetail(Utf8JsonWriter json, string name, CapacityDetail capacity)
    {
        json.WriteStartObject(name);
        WriteUnits(json, capacity.CapacityUnits, capacity.ReadCapacityUnits, capacity.WriteCapacityUnits);
        json.WriteEndObject();
    }

    // The numbers come from a parsed response, so they are finite; a writer would reject NaN and infinity.
    private static void WriteUnits(Utf8JsonWriter json, double? capacityUnits, double? readCapacityUnits, double? writeCapacityUnits)
    {
        if (capacityUnits is { } total)
        {
            json.WriteNumber("CapacityUnits"u8, total);
        }

        if (readCapacityUnits is { } read)
        {
            json.WriteNumber("ReadCapacityUnits"u8, read);
        }

        if (writeCapacityUnits is { } write)
        {
            json.WriteNumber("WriteCapacityUnits"u8, write);
        }
    }
}
