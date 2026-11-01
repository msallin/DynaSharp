using DynaSharp.Mapping;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Mapping;

/// <summary>Runs a mapped type through its generated code, on DynamoDB JSON as text.</summary>
internal static class EntityJson
{
    /// <summary>Gives the item that <paramref name="value"/> is stored as.</summary>
    public static string Write<T>(T value)
        where T : IDynamoDbDocument<T> =>
        TestJson.Write(writer =>
        {
            writer.WriteStartItem();
            T.WriteAttributes(writer, value);
            writer.WriteEndItem();
        });

    /// <summary>Gives the key that <paramref name="value"/> is addressed by.</summary>
    public static string WriteKey<T>(T value)
        where T : IDynamoDbEntity<T> =>
        TestJson.Write(writer =>
        {
            writer.WriteStartItem();
            T.WriteKey(writer, value);
            writer.WriteEndItem();
        });

    /// <summary>Reads an item.</summary>
    public static T Read<T>(string json)
        where T : IDynamoDbDocument<T> =>
        TestJson.Read(json, static (ref reader) =>
        {
            reader.ReadStartItem();
            return T.ReadAttributes(ref reader);
        });

    /// <summary>Reads an item of a recorded <c>GetItem</c> response.</summary>
    public static T ReadRecorded<T>(string name)
        where T : IDynamoDbDocument<T> =>
        Read<T>(AttributeJson(RealAwsResponses.Get(name).Wire));

    private static string AttributeJson(string wire)
    {
        using var document = System.Text.Json.JsonDocument.Parse(wire);
        return document.RootElement.GetProperty("Item").GetRawText();
    }
}
