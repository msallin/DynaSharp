using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>Reads a value from a <see cref="DynamoDbReader"/>, which as a ref struct cannot be a generic argument by reference.</summary>
internal delegate T ReadFunc<out T>(ref DynamoDbReader reader);

/// <summary>Runs writers and readers over the wire settings, with JSON as text for readable assertions.</summary>
internal static class TestJson
{
    public static string Write(Action<DynamoDbWriter> write)
    {
        using var buffer = new PooledByteBuffer();
        using (var json = WireJson.CreateWriter(buffer))
        {
            write(new DynamoDbWriter(json));
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static T Read<T>(string json, ReadFunc<T> read)
    {
        var reader = new DynamoDbReader(Encoding.UTF8.GetBytes(json));
        return read(ref reader);
    }

    public static string WriteValue(AttributeValue value) => Write(writer => AttributeValueConverter.Write(writer, value));

    public static AttributeValue ReadValue(string json) => Read(json, static (ref reader) =>
    {
        var value = AttributeValueConverter.Read(ref reader, reader.ReadStartAttribute());
        reader.ReadEndAttribute();
        return value;
    });

    public static string WriteItem(IReadOnlyDictionary<string, AttributeValue> item) =>
        Write(writer => AttributeValueConverter.WriteItem(writer, item));

    public static Item ReadItem(string json) =>
        Read(json, static (ref reader) => AttributeValueConverter.ReadItem(ref reader));
}
