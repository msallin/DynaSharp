using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Finds the key of a command whose condition failed, for <see cref="ConditionalCheckFailedException.Key"/>. DynamoDB does not send it and a command keeps no copy of it, so that a write
/// that gets through pays nothing: this reads the body as it was sent, on the failure path. In a create-if-absent a failed condition is routine, and a body can be an item of 400 KB, so the
/// read goes forward and ends at the key, and what lies behind it is not looked at.
/// </summary>
internal static class FailedCondition
{
    /// <summary>Attaches the key of a body to the exception: the <c>Key</c> of an update or a delete, or the key attributes in the <c>Item</c> of a put. A body with neither leaves it as it is.</summary>
    /// <param name="failure">The failure.</param>
    /// <param name="body">The request body, which this library wrote.</param>
    /// <param name="keyNames">The key attributes of the table.</param>
    public static void Describe(ConditionalCheckFailedException failure, ArraySegment<byte> body, string[] keyNames)
    {
        var reader = new DynamoDbReader(body);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Key"u8))
            {
                failure.Describe(AttributeValueConverter.ReadItem(ref reader));
                return;
            }

            if (reader.AttributeNameEquals("Item"u8))
            {
                if (KeyOf(ref reader, keyNames) is { Count: > 0 } found)
                {
                    failure.Describe(found);
                }

                return;
            }

            OperationJson.SkipValue(ref reader);
        }
    }

    // {"pk":{"S":"USER#alice"},"sk":{"S":"PROFILE"},"name":{"S":"Alice"}} with the names pk and sk gives the first two, and name is never reached. A name twice is DynamoDB's to reject, so a
    // second pk behind the whole key goes unseen.
    private static Item KeyOf(ref DynamoDbReader reader, string[] keyNames)
    {
        var key = new Item(keyNames.Length);
        reader.ReadStartItem();
        while (key.Count < keyNames.Length && reader.TryReadAttributeName())
        {
            if (NameOf(ref reader, keyNames) is { } name)
            {
                key[name] = reader.ReadAttribute<AttributeValue, AttributeValueConverter>();
            }
            else
            {
                reader.SkipAttributeValue();
            }
        }

        return key;
    }

    private static string? NameOf(ref DynamoDbReader reader, string[] keyNames)
    {
        foreach (var name in keyNames)
        {
            if (reader.Json.ValueTextEquals(name))
            {
                return name;
            }
        }

        return null;
    }
}
