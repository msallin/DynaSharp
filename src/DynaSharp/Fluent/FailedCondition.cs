using System.Text;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Finds the key of a command whose condition failed, for <see cref="ConditionalCheckFailedException.Key"/>. DynamoDB does not send it and a command keeps no copy of it, so that a write
/// that gets through pays nothing: this reads the body as it was sent, on the failure path, which costs the same parse that <see cref="RejectedRequest"/> costs a rejection.
/// </summary>
internal static class FailedCondition
{
    /// <summary>Attaches the key of a body to the exception: the <c>Key</c> of an update or a delete, or the key attributes in the <c>Item</c> of a put. A body with neither leaves it as it is.</summary>
    /// <param name="failure">The failure.</param>
    /// <param name="body">The request body, which this library wrote.</param>
    /// <param name="keyNames">The key attributes of the table.</param>
    public static void Describe(ConditionalCheckFailedException failure, ArraySegment<byte> body, string[] keyNames)
    {
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = WireJson.ReaderOptions.MaxDepth });
        var root = document.RootElement;
        if (root.TryGetProperty("Key"u8, out var key))
        {
            var reader = ReaderOf(key);
            failure.Describe(AttributeValueConverter.ReadItem(ref reader));
        }
        else if (root.TryGetProperty("Item"u8, out var item) && KeyOf(item, keyNames) is { Count: > 0 } found)
        {
            failure.Describe(found);
        }
    }

    // {"pk":{"S":"USER#alice"},"sk":{"S":"PROFILE"},"name":{"S":"Alice"}} with the names pk and sk gives the first two.
    private static Item KeyOf(JsonElement item, string[] keyNames)
    {
        var key = new Item(keyNames.Length);
        foreach (var name in keyNames)
        {
            if (item.TryGetProperty(name, out var value))
            {
                var reader = ReaderOf(value);
                key[name] = reader.ReadAttribute<AttributeValue, AttributeValueConverter>();
            }
        }

        return key;
    }

    // A second parse of a few bytes: a JsonElement has no way to a converter, which reads from a reader.
    private static DynamoDbReader ReaderOf(JsonElement value) => new(Encoding.UTF8.GetBytes(value.GetRawText()));
}
