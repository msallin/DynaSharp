using System.Buffers;
using System.Collections.ObjectModel;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// Shared pieces of request and response JSON for the operations: optional request fields, enum names and the
/// <c>ConsumedCapacity</c> structure.
/// </summary>
/// <remarks>
/// Response envelopes are plain JSON objects. They are walked with the item methods of <see cref="DynamoDbReader"/>
/// (<see cref="DynamoDbReader.ReadStartItem"/>, <see cref="DynamoDbReader.TryReadAttributeName"/>) and the readers here, which
/// read the value of the property the reader is positioned on. Properties the library does not know are skipped.
/// </remarks>
internal static class OperationJson
{
    private static readonly double MinEpochSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly double MaxEpochSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    // Requests: DynamoDB rejects empty maps, so they are omitted.
    public static void WriteStringMap(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, IReadOnlyDictionary<string, string>? map)
    {
        if (map is not { Count: > 0 })
        {
            return;
        }

        json.WriteStartObject(propertyName);
        foreach (var (name, value) in map)
        {
            json.WriteString(name, value);
        }

        json.WriteEndObject();
    }

    public static void WriteAttributeValueMap(DynamoDbWriter writer, ReadOnlySpan<byte> propertyName, IReadOnlyDictionary<string, AttributeValue>? map)
    {
        if (map is { Count: > 0 })
        {
            WriteItem(writer, propertyName, map);
        }
    }

    public static void WriteItem(DynamoDbWriter writer, ReadOnlySpan<byte> propertyName, IReadOnlyDictionary<string, AttributeValue> item)
    {
        writer.Json.WritePropertyName(propertyName);
        AttributeValueConverter.WriteItem(writer, item);
    }

    // An item that is set is always written, even empty: an empty ExclusiveStartKey must fail in DynamoDB, not silently restart.
    public static void WriteItemIfSet(DynamoDbWriter writer, ReadOnlySpan<byte> propertyName, IReadOnlyDictionary<string, AttributeValue>? item)
    {
        if (item is not null)
        {
            WriteItem(writer, propertyName, item);
        }
    }

    public static void WriteStringIfSet(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, string? value)
    {
        if (value is not null)
        {
            json.WriteString(propertyName, value);
        }
    }

    public static void WriteInt32IfSet(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, int? value)
    {
        if (value is { } number)
        {
            json.WriteNumber(propertyName, number);
        }
    }

    public static void WriteBooleanIfSet(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, bool? value)
    {
        if (value is { } flag)
        {
            json.WriteBoolean(propertyName, flag);
        }
    }

    // For the flags whose default the service assumes when they are missing, so false is not sent.
    public static void WriteIfTrue(Utf8JsonWriter json, ReadOnlySpan<byte> propertyName, bool value)
    {
        if (value)
        {
            json.WriteBoolean(propertyName, true);
        }
    }

    public static void WriteReturnValues(Utf8JsonWriter json, ReturnValue value) =>
        WireNames.ReturnValues.WriteIfSet(json, "ReturnValues"u8, value);

    public static void WriteReturnValuesOnConditionCheckFailure(Utf8JsonWriter json, ReturnValuesOnConditionCheckFailure value) =>
        WireNames.ReturnValuesOnConditionCheckFailures.WriteIfSet(json, "ReturnValuesOnConditionCheckFailure"u8, value);

    public static void WriteReturnConsumedCapacity(Utf8JsonWriter json, ReturnConsumedCapacity value) =>
        WireNames.ReturnConsumedCapacities.WriteIfSet(json, "ReturnConsumedCapacity"u8, value);

    public static void WriteReturnItemCollectionMetrics(Utf8JsonWriter json, ReturnItemCollectionMetrics value) =>
        WireNames.ReturnItemCollectionMetricsValues.WriteIfSet(json, "ReturnItemCollectionMetrics"u8, value);

    public static void WriteSelect(Utf8JsonWriter json, Select value) =>
        WireNames.Selects.WriteIfSet(json, "Select"u8, value);

    // Responses.
    public static void SkipValue(ref DynamoDbReader reader)
    {
        reader.Advance();
        reader.Json.Skip();
    }

    public static string? ReadString(ref DynamoDbReader reader)
    {
        reader.Advance();
        return reader.Json.GetString();
    }

    public static string ReadRequiredString(ref DynamoDbReader reader) =>
        ReadString(ref reader) ?? throw new JsonException("Expected a JSON string but found null.");

    // A JSON number beyond the range of a double reads as infinity, which no consumer of these numbers can handle: the writer of the
    // capacity attribute rejects it, and that must not depend on whether a listener is attached.
    public static double ReadDouble(ref DynamoDbReader reader)
    {
        reader.Advance();
        var number = reader.Json.GetDouble();
        return double.IsFinite(number) ? number : throw new JsonException("The number is outside the range of a double.");
    }

    public static long ReadInt64(ref DynamoDbReader reader)
    {
        reader.Advance();
        return reader.Json.GetInt64();
    }

    public static int ReadInt32(ref DynamoDbReader reader)
    {
        reader.Advance();
        return reader.Json.GetInt32();
    }

    public static bool ReadBoolean(ref DynamoDbReader reader)
    {
        reader.Advance();
        return reader.Json.GetBoolean();
    }

    // A name this library does not know, and a JSON null, read as the zero member that every enum read from the wire has
    // (Unknown, or Unspecified for an enum that is optional in requests), so they do not fail the response.
    public static TEnum ReadEnum<TEnum>(ref DynamoDbReader reader, EnumWireNames<TEnum> names)
        where TEnum : struct, Enum =>
        ReadString(ref reader) is { } text && names.TryGetValue(text, out var value) ? value : default;

    // DynamoDB sends timestamps as epoch seconds with a fractional part, for example 1.761901254813E9.
    public static DateTimeOffset ReadEpochSeconds(ref DynamoDbReader reader)
    {
        var seconds = ReadDouble(ref reader);
        if (seconds < MinEpochSeconds || seconds > MaxEpochSeconds)
        {
            throw new JsonException($"The timestamp {seconds} is outside the range of DateTimeOffset.");
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000));
    }

    public static ReadOnlyCollection<string> ReadStringArray(ref DynamoDbReader reader)
    {
        var strings = new List<string>();
        ReadStartArray(ref reader);
        while (TryReadElement(ref reader))
        {
            strings.Add(reader.Json.GetString() ?? throw new JsonException("Expected a JSON string but found null."));
        }

        return strings.AsReadOnly();
    }

    public static void ReadStartArray(ref DynamoDbReader reader)
    {
        reader.Advance();
        ref var json = ref reader.Json;
        if (json.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a JSON array but found {json.TokenType} at byte {json.TokenStartIndex}.");
        }
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> ReadItems(ref DynamoDbReader reader) =>
        ReadItems(ref reader, ArrayPool<Item>.Shared, BuilderPools.Shared);

    // Reads an array of items, as in a Query or Scan response. The reader advances over the one buffer, so no copy of it is made;
    // the pools are parameters so that tests can verify that everything rented is returned.
    public static IReadOnlyList<IReadOnlyDictionary<string, AttributeValue>> ReadItems(ref DynamoDbReader reader, ArrayPool<Item> pool, BuilderPools pools) =>
        Array.AsReadOnly(ReadItemArray(ref reader, pool, pools));

    public static Item[] ReadItemArray(ref DynamoDbReader reader, ArrayPool<Item> pool, BuilderPools pools)
    {
        ReadStartArray(ref reader);
        var items = new ArrayBuilder<Item>(pool);
        try
        {
            // The property order of the response is not known, so the number of items is not known either: the builder grows.
            // The items of a page tend to have the same attributes, so each one starts from the shape of the one before.
            ItemShape? shape = null;
            while (reader.TryReadStartItem())
            {
                var item = AttributeValueConverter.ReadAttributes(ref reader, pools, shape);
                shape = item.Shape;
                items.Add(item);
            }

            return items.ToArray();
        }
        finally
        {
            items.Dispose();
        }
    }

    // DynamoDB leaves LastEvaluatedKey out on the last page. An empty one is treated the same, so "no key" is the only way to the end.
    public static IReadOnlyDictionary<string, AttributeValue>? ReadLastEvaluatedKey(ref DynamoDbReader reader)
    {
        var key = AttributeValueConverter.ReadItem(ref reader);
        return key.Count > 0 ? key : null;
    }

    // {"#n":"name","#m":"other"}
    public static Dictionary<string, string> ReadStringMap(ref DynamoDbReader reader)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var name = reader.GetAttributeName();
            map[name] = ReadRequiredString(ref reader);
        }

        return map;
    }

    // "ConsumedCapacity":[{"TableName":"Forum","CapacityUnits":3},{"TableName":"Thread","CapacityUnits":1}] as in BatchGetItem, BatchWriteItem
    // and the transactions, where the single object of the other operations becomes a list with one entry per table.
    public static ReadOnlyCollection<ConsumedCapacity> ReadConsumedCapacityList(ref DynamoDbReader reader)
    {
        var capacities = new List<ConsumedCapacity>();
        ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            capacities.Add(ReadConsumedCapacityMembers(ref reader));
        }

        return capacities.AsReadOnly();
    }

    // "ItemCollectionMetrics":{"Forum":[{"ItemCollectionKey":{...},"SizeEstimateRangeGB":[0.0,1.0]}]}: a list per table, in BatchWriteItem
    // and TransactWriteItems.
    public static Dictionary<string, IReadOnlyList<ItemCollectionMetrics>> ReadItemCollectionMetricsByTable(ref DynamoDbReader reader)
    {
        var byTable = new Dictionary<string, IReadOnlyList<ItemCollectionMetrics>>(StringComparer.Ordinal);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var table = reader.GetAttributeName();
            var metrics = new List<ItemCollectionMetrics>();
            ReadStartArray(ref reader);
            while (reader.TryReadStartItem())
            {
                metrics.Add(ReadItemCollectionMetricsMembers(ref reader));
            }

            byTable[table] = metrics.AsReadOnly();
        }

        return byTable;
    }

    public static ItemCollectionMetrics ReadItemCollectionMetrics(ref DynamoDbReader reader)
    {
        reader.ReadStartItem();
        return ReadItemCollectionMetricsMembers(ref reader);
    }

    public static ConsumedCapacity ReadConsumedCapacity(ref DynamoDbReader reader)
    {
        reader.ReadStartItem();
        return ReadConsumedCapacityMembers(ref reader);
    }

    // The members of an ItemCollectionMetrics whose opening brace the reader has consumed.
    private static ItemCollectionMetrics ReadItemCollectionMetricsMembers(ref DynamoDbReader reader)
    {
        IReadOnlyDictionary<string, AttributeValue>? itemCollectionKey = null;
        IReadOnlyList<double>? sizeEstimateRange = null;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("ItemCollectionKey"u8))
            {
                itemCollectionKey = AttributeValueConverter.ReadItem(ref reader);
            }
            else if (reader.AttributeNameEquals("SizeEstimateRangeGB"u8))
            {
                sizeEstimateRange = ReadDoubleArray(ref reader);
            }
            else
            {
                SkipValue(ref reader);
            }
        }

        return new ItemCollectionMetrics { ItemCollectionKey = itemCollectionKey, SizeEstimateRangeGB = sizeEstimateRange };
    }

    // The members of a ConsumedCapacity whose opening brace the reader has consumed.
    private static ConsumedCapacity ReadConsumedCapacityMembers(ref DynamoDbReader reader)
    {
        string? tableName = null;
        double? capacityUnits = null;
        double? readCapacityUnits = null;
        double? writeCapacityUnits = null;
        CapacityDetail? table = null;
        Dictionary<string, CapacityDetail>? localSecondaryIndexes = null;
        Dictionary<string, CapacityDetail>? globalSecondaryIndexes = null;
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("TableName"u8))
            {
                tableName = ReadString(ref reader);
            }
            else if (reader.AttributeNameEquals("CapacityUnits"u8))
            {
                capacityUnits = ReadDouble(ref reader);
            }
            else if (reader.AttributeNameEquals("ReadCapacityUnits"u8))
            {
                readCapacityUnits = ReadDouble(ref reader);
            }
            else if (reader.AttributeNameEquals("WriteCapacityUnits"u8))
            {
                writeCapacityUnits = ReadDouble(ref reader);
            }
            else if (reader.AttributeNameEquals("Table"u8))
            {
                table = ReadCapacityDetail(ref reader);
            }
            else if (reader.AttributeNameEquals("LocalSecondaryIndexes"u8))
            {
                localSecondaryIndexes = ReadIndexCapacities(ref reader);
            }
            else if (reader.AttributeNameEquals("GlobalSecondaryIndexes"u8))
            {
                globalSecondaryIndexes = ReadIndexCapacities(ref reader);
            }
            else
            {
                SkipValue(ref reader);
            }
        }

        return new ConsumedCapacity
        {
            TableName = tableName,
            CapacityUnits = capacityUnits,
            ReadCapacityUnits = readCapacityUnits,
            WriteCapacityUnits = writeCapacityUnits,
            Table = table,
            LocalSecondaryIndexes = localSecondaryIndexes,
            GlobalSecondaryIndexes = globalSecondaryIndexes,
        };
    }

    // The two bounds of SizeEstimateRangeGB.
    private static ReadOnlyCollection<double> ReadDoubleArray(ref DynamoDbReader reader)
    {
        var numbers = new List<double>(2);
        ReadStartArray(ref reader);
        while (TryReadElement(ref reader))
        {
            numbers.Add(reader.Json.GetDouble());
        }

        return numbers.AsReadOnly();
    }

    // Moves to the next element of an array of plain values; false at the end of the array, which is consumed.
    private static bool TryReadElement(ref DynamoDbReader reader)
    {
        reader.Advance();
        return reader.Json.TokenType != JsonTokenType.EndArray;
    }

    private static CapacityDetail ReadCapacityDetail(ref DynamoDbReader reader)
    {
        double? capacityUnits = null;
        double? readCapacityUnits = null;
        double? writeCapacityUnits = null;
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("CapacityUnits"u8))
            {
                capacityUnits = ReadDouble(ref reader);
            }
            else if (reader.AttributeNameEquals("ReadCapacityUnits"u8))
            {
                readCapacityUnits = ReadDouble(ref reader);
            }
            else if (reader.AttributeNameEquals("WriteCapacityUnits"u8))
            {
                writeCapacityUnits = ReadDouble(ref reader);
            }
            else
            {
                SkipValue(ref reader);
            }
        }

        return new CapacityDetail { CapacityUnits = capacityUnits, ReadCapacityUnits = readCapacityUnits, WriteCapacityUnits = writeCapacityUnits };
    }

    private static Dictionary<string, CapacityDetail> ReadIndexCapacities(ref DynamoDbReader reader)
    {
        var indexes = new Dictionary<string, CapacityDetail>(StringComparer.Ordinal);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            var indexName = reader.GetAttributeName();
            indexes[indexName] = ReadCapacityDetail(ref reader);
        }

        return indexes;
    }
}
