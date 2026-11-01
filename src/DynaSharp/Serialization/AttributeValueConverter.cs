using System.Text.Json;

namespace DynaSharp.Serialization;

/// <summary>
/// Converts <see cref="AttributeValue"/> to and from any DynamoDB attribute value: the converter for a property that holds a value of
/// unknown or varying type, and for the values of the untyped API.
/// </summary>
/// <remarks>
/// Every attribute type is supported, and nested lists and maps are read to any depth DynamoDB allows. Sets read from the wire must
/// have at least one element, which DynamoDB guarantees; an empty set is a <see cref="System.Text.Json.JsonException"/>. A map that
/// repeats an attribute name is rejected the same way. A default <see cref="AttributeValue"/> has no type and cannot be written.
/// </remarks>
public readonly struct AttributeValueConverter : IDynamoDbConverter<AttributeValue>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="value"/> is a default <see cref="AttributeValue"/>, which has no type.</exception>
    public static void Write(DynamoDbWriter writer, AttributeValue value)
    {
        switch (value.Type)
        {
            case AttributeType.String:
                writer.WriteString((string)value.Value!);
                break;
            case AttributeType.Number:
                writer.WriteNumber((string)value.Value!);
                break;
            case AttributeType.Binary:
                writer.WriteBinary((byte[])value.Value!);
                break;
            case AttributeType.Boolean:
                writer.WriteBool((bool)value.Value!);
                break;
            case AttributeType.Null:
                writer.WriteNull();
                break;
            case AttributeType.List:
                writer.WriteStartList();
                foreach (var element in (AttributeValue[])value.Value!)
                {
                    Write(writer, element);
                }

                writer.WriteEndList();
                break;
            case AttributeType.Map:
                writer.WriteStartMap();
                ((Item)value.Value!).WriteAttributes(writer);
                writer.WriteEndMap();
                break;
            case AttributeType.StringSet:
                writer.WriteStartStringSet();
                foreach (var element in (string[])value.Value!)
                {
                    writer.WriteStringSetElement(element);
                }

                writer.WriteEndSet();
                break;
            case AttributeType.NumberSet:
                writer.WriteStartNumberSet();
                foreach (var element in (string[])value.Value!)
                {
                    writer.WriteNumberSetElement(element);
                }

                writer.WriteEndSet();
                break;
            case AttributeType.BinarySet:
                writer.WriteStartBinarySet();
                foreach (var element in (ReadOnlyMemory<byte>[])value.Value!)
                {
                    writer.WriteBinarySetElement(element.Span);
                }

                writer.WriteEndSet();
                break;
            default:
                throw new ArgumentException("A default AttributeValue has no type and cannot be sent to DynamoDB.", nameof(value));
        }
    }

    /// <inheritdoc/>
    /// <exception cref="System.Text.Json.JsonException">The value is malformed, for example an empty set or a map that repeats an attribute name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a DynamoDB attribute type, such as <see cref="AttributeType.None"/>.</exception>
    public static AttributeValue Read(ref DynamoDbReader reader, AttributeType type) => Read(ref reader, type, BuilderPools.Shared);

    internal static AttributeValue Read(ref DynamoDbReader reader, AttributeType type, BuilderPools pools) => type switch
    {
        AttributeType.String => AttributeValue.FromString(reader.GetString()),
        AttributeType.Number => AttributeValue.WrapNumber(reader.GetNumberText()),
        AttributeType.Binary => AttributeValue.WrapBinary(reader.GetBinary()),
        AttributeType.Boolean => AttributeValue.FromBool(reader.GetBool()),
        AttributeType.Null => AttributeValue.Null,
        AttributeType.List => AttributeValue.WrapList(ReadList(ref reader, pools)),
        AttributeType.Map => AttributeValue.WrapMap(ReadAttributes(ref reader, pools)),
        AttributeType.StringSet => AttributeValue.WrapStringSet(ReadStringSet(ref reader, pools)),
        AttributeType.NumberSet => AttributeValue.WrapNumberSet(ReadStringSet(ref reader, pools)),
        AttributeType.BinarySet => AttributeValue.WrapBinarySet(ReadBinarySet(ref reader, pools)),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a DynamoDB attribute type."),
    };

    /// <summary>Writes an item: a JSON object of attribute names and values, as used for <c>Item</c> or <c>Key</c>.</summary>
    /// <param name="writer">The writer, positioned where a value is expected.</param>
    /// <param name="item">The attributes.</param>
    internal static void WriteItem(DynamoDbWriter writer, IReadOnlyDictionary<string, AttributeValue> item)
    {
        writer.WriteStartItem();
        if (item is Item flat)
        {
            flat.WriteAttributes(writer);
        }
        else if (item is Dictionary<string, AttributeValue> dictionary)
        {
            // Enumerating the concrete type avoids boxing the enumerator.
            foreach (var (name, value) in dictionary)
            {
                writer.WriteAttributeName(name);
                Write(writer, value);
            }
        }
        else
        {
            foreach (var (name, value) in item)
            {
                writer.WriteAttributeName(name);
                Write(writer, value);
            }
        }

        writer.WriteEndItem();
    }

    /// <summary>Reads an item: a JSON object of attribute names and values, such as <c>Item</c> in a response.</summary>
    /// <param name="reader">The reader, positioned before the item (for example on the property name <c>"Item"</c>).</param>
    /// <returns>The attributes.</returns>
    internal static Item ReadItem(ref DynamoDbReader reader) => ReadItem(ref reader, BuilderPools.Shared);

    /// <summary>Reads an item with the given pools and tables.</summary>
    /// <param name="reader">The reader, positioned before the item.</param>
    /// <param name="pools">The pools and tables to read with.</param>
    /// <returns>The attributes.</returns>
    internal static Item ReadItem(ref DynamoDbReader reader, BuilderPools pools)
    {
        reader.ReadStartItem();
        return ReadAttributes(ref reader, pools);
    }

    /// <summary>
    /// Reads the attributes of an item or map whose opening <c>{</c> the reader has consumed, up to and including the closing
    /// <c>}</c>. Use it for the items of an array, after <see cref="DynamoDbReader.TryReadStartItem"/> returned
    /// <see langword="true"/>, so that no second reader is needed.
    /// </summary>
    /// <param name="reader">The reader, positioned after the <c>{</c>.</param>
    /// <returns>The attributes.</returns>
    internal static Item ReadAttributes(ref DynamoDbReader reader) => ReadAttributes(ref reader, BuilderPools.Shared);

    /// <summary>
    /// Reads the attributes of an item or map. When the shape is known, this allocates the values and the item and nothing else; while
    /// the names follow <paramref name="previous"/> they are not even looked up. Duplicates are rejected the first time a shape is seen.
    /// </summary>
    /// <param name="reader">The reader, positioned after the <c>{</c>.</param>
    /// <param name="pools">The pools and tables to read with.</param>
    /// <param name="previous">The shape of the previous item of a page, which the next one most likely has as well.</param>
    /// <returns>The attributes.</returns>
    internal static Item ReadAttributes(ref DynamoDbReader reader, BuilderPools pools, ItemShape? previous = null)
    {
        var expected = previous ?? ItemShape.Empty;
        var expectedNames = expected.Names;
        var nameBuffer = default(InlineBuffer<string>);
        var valueBuffer = default(InlineBuffer<AttributeValue>);
        var names = new ArrayBuilder<string>(pools.Strings, nameBuffer);
        var values = new ArrayBuilder<AttributeValue>(pools.Values, valueBuffer);
        try
        {
            var matched = 0;
            while (reader.TryReadAttributeName())
            {
                string name;
                if (matched == names.Count && matched < expectedNames.Length && expected.IsNameAt(ref reader.Json, matched))
                {
                    name = expectedNames[matched++];
                }
                else
                {
                    name = pools.Names.GetName(ref reader.Json);
                }

                var value = Read(ref reader, reader.ReadStartAttribute(), pools);
                reader.ReadEndAttribute();
                names.Add(name);
                values.Add(value);
            }

            return matched == names.Count && matched == expectedNames.Length
                ? new Item(expected, values.ToArray())
                : pools.Shapes.CreateItem(names.WrittenSpan, values.WrittenSpan, expected);
        }
        finally
        {
            names.Dispose();
            values.Dispose();
        }
    }

    private static AttributeValue[] ReadList(ref DynamoDbReader reader, BuilderPools pools)
    {
        var elements = new ArrayBuilder<AttributeValue>(pools.Values);
        try
        {
            while (reader.TryReadStartAttribute(out var type))
            {
                elements.Add(Read(ref reader, type, pools));
                reader.ReadEndAttribute();
            }

            return elements.ToArray();
        }
        finally
        {
            elements.Dispose();
        }
    }

    private static string[] ReadStringSet(ref DynamoDbReader reader, BuilderPools pools)
    {
        var elements = new ArrayBuilder<string>(pools.Strings);
        try
        {
            while (reader.TryReadSetElement())
            {
                elements.Add(reader.GetString());
            }

            ThrowIfEmpty(elements.Count);
            return elements.ToArray();
        }
        finally
        {
            elements.Dispose();
        }
    }

    private static ReadOnlyMemory<byte>[] ReadBinarySet(ref DynamoDbReader reader, BuilderPools pools)
    {
        var elements = new ArrayBuilder<ReadOnlyMemory<byte>>(pools.Binaries);
        try
        {
            while (reader.TryReadSetElement())
            {
                elements.Add(reader.GetBinary());
            }

            ThrowIfEmpty(elements.Count);
            return elements.ToArray();
        }
        finally
        {
            elements.Dispose();
        }
    }

    // DynamoDB never sends empty sets; AttributeValue relies on sets having elements.
    private static void ThrowIfEmpty(int count)
    {
        if (count == 0)
        {
            throw new JsonException("A DynamoDB set must contain at least one element.");
        }
    }
}
