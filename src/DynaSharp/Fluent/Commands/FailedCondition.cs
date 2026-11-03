using System.Text;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Finds the key of a command whose condition failed, for <see cref="ConditionalCheckFailedException.Key"/>. DynamoDB does not send it and a command keeps no copy of it, so that a write
/// that gets through pays nothing: this reads the body as it was sent, on the failure path. In a create-if-absent a failed condition is routine, and a body can be an item of 400 KB, so the
/// read goes forward and ends at the key, and what lies behind it is not looked at. <see cref="Explain"/> names the condition in the message from what the write rules noted on the state, so it reads
/// nothing at all.
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

    /// <summary>
    /// Gives the exception what the line of its message is made of, which says which condition did not hold. It is names and kinds that the write rules noted, and the line is made only when the message is read,
    /// see <see cref="Line"/>. A write without a part keeps the message as it is.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <param name="operation">The operation, <c>PutItem</c>, <c>UpdateItem</c> or <c>DeleteItem</c>, which are the ones with a condition.</param>
    /// <param name="table">The table of the write.</param>
    /// <param name="parts">The parts of the condition.</param>
    public static void Explain(ConditionalCheckFailedException failure, string operation, TableModel table, ConditionParts parts)
    {
        if (parts != ConditionParts.None)
        {
            failure.Explain(operation, table.Name, table.Writes?.VersionAttributeName, parts);
        }
    }

    /// <summary>
    /// Makes the line that says which condition did not hold: "The condition of this Update on Users did not hold: the version check on Revision." It names the version by its attribute and the rest by kind,
    /// and never a value. DynamoDB does not say which part failed, so with several the line names them all.
    /// </summary>
    /// <param name="operation">The operation, whose name ends in <c>Item</c>.</param>
    /// <param name="tableName">The table.</param>
    /// <param name="versionAttributeName">The version attribute, which a part of the version has.</param>
    /// <param name="parts">The parts of the condition, at least one.</param>
    /// <returns>The line.</returns>
    public static string Line(string operation, string tableName, string? versionAttributeName, ConditionParts parts)
    {
        // Version 0 and IfNotExists are one condition, and IfNotExists asked for it in so many words.
        var newVersion = (parts & (ConditionParts.NewVersion | ConditionParts.NoItem)) == ConditionParts.NewVersion;
        var named = new List<string>(4);
        if (newVersion || (parts & ConditionParts.Version) != 0)
        {
            named.Add("the version check on " + versionAttributeName);
        }

        if ((parts & ConditionParts.NoItem) != 0)
        {
            named.Add("the check that no item has this key");
        }

        if ((parts & ConditionParts.ItemExists) != 0)
        {
            named.Add("the check that an item has this key");
        }

        if ((parts & ConditionParts.Caller) != 0)
        {
            named.Add("your own condition");
        }

        // PutItem gives Put.
        var line = new StringBuilder("The condition of this ").Append(operation, 0, operation.Length - "Item".Length).Append(" on ").Append(tableName).Append(" did not hold: ");
        for (var i = 0; i < named.Count; i++)
        {
            if (i > 0)
            {
                line.Append(i == named.Count - 1 ? " or " : ", ");
            }

            line.Append(named[i]);
        }

        line.Append(named.Count > 1 ? ", and DynamoDB does not say which." : ".");
        if (newVersion)
        {
            line.Append(" The version this write expects marks a new item, so the check asks that no item has this key. To write over the stored item, use the version last read from it, or call IgnoreVersion() to write without the check.");
        }

        return line.ToString();
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
