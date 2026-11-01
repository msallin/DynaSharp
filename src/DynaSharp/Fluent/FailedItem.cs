using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// Finds the key of an item that a type could not read, for <see cref="DynamoDbMappingException.Key"/>. A read goes straight from JSON to the type, so nothing about the item is at hand when it
/// fails, and JSON has no order of attributes, so the key can stand behind the attribute that failed. The caller keeps a copy of its reader for that, one per page, because a reader is a struct:
/// this walks the copy on the failure path, and a read that does not fail has paid for the copy alone.
/// </summary>
internal static class FailedItem
{
    /// <summary>Reads the key of one item of an array.</summary>
    /// <param name="items">A copy of the reader, positioned after the opening bracket of the array.</param>
    /// <param name="position">The position of the item in the array.</param>
    /// <param name="table">What was read, which says what the key of an item is.</param>
    /// <returns>The key attributes, or <see langword="null"/> if the item has none of them.</returns>
    public static Item? KeyOf(DynamoDbReader items, int position, TableModel table)
    {
        for (var i = 0; i < position; i++)
        {
            items.TryReadStartItem();
            items.Json.Skip();
        }

        items.TryReadStartItem();
        var key = new Item(table.ItemKeyNames.Length);
        while (items.TryReadAttributeName())
        {
            var name = items.GetAttributeName();
            if (Array.IndexOf(table.ItemKeyNames, name) >= 0)
            {
                key[name] = items.ReadAttribute<AttributeValue, AttributeValueConverter>();
            }
            else
            {
                items.SkipAttributeValue();
            }
        }

        return key.Count > 0 ? key : null;
    }
}
