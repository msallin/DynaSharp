using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>Runs a converter the way generated code does: through its type parameter, and through <c>ReadAttribute</c> for reads.</summary>
internal static class ConverterRun
{
    public static string Write<T, TConverter>(T value)
        where TConverter : IDynamoDbConverter<T>
        => TestJson.Write(writer => TConverter.Write(writer, value));

    public static T Read<T, TConverter>(string json)
        where TConverter : IDynamoDbConverter<T>
        => TestJson.Read(json, static (ref reader) => reader.ReadAttribute<T, TConverter>());

    /// <summary>Returns the members of the set in <paramref name="json"/> as sorted text, since DynamoDB sets have no order.</summary>
    /// <param name="json">The set value, for example <c>{"SS":["a","b"]}</c>.</param>
    /// <param name="descriptor">The descriptor of the set: <c>SS</c>, <c>NS</c> or <c>BS</c>.</param>
    /// <returns>The members, ordinally sorted.</returns>
    public static string[] Members(string json, string descriptor)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty(descriptor).EnumerateArray().Select(static member => member.GetString()!).Order(StringComparer.Ordinal)];
    }

    /// <summary>Returns the raw JSON of one attribute of an item of a recorded response, for example <c>{"S":"x"}</c>.</summary>
    /// <param name="wire">The response body, <c>{"Item":{...}}</c>.</param>
    /// <param name="attribute">The attribute name.</param>
    /// <returns>The attribute value as JSON text.</returns>
    public static string Attribute(string wire, string attribute)
    {
        using var document = System.Text.Json.JsonDocument.Parse(wire);
        return document.RootElement.GetProperty("Item").GetProperty(attribute).GetRawText();
    }
}
