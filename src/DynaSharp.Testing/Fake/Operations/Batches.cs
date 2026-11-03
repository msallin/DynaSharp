using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>What the batches and the transactions read alike: the length of their lists, the one action of an element, and keys that come twice.</summary>
internal static class Batches
{
    /// <summary>Checks the length of a list of a request.</summary>
    /// <param name="count">The length.</param>
    /// <param name="member">The member, as DynamoDB names it in the message: <c>transactItems</c>.</param>
    /// <param name="max">The largest length DynamoDB takes.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> for an empty list or a longer one.</exception>
    public static void ThrowIfLength(int count, string member, int max)
    {
        if (count < 1)
        {
            throw FakeError.Validation($"1 validation error detected: Value at '{member}' failed to satisfy constraint: Member must have length greater than or equal to 1");
        }

        if (count > max)
        {
            throw FakeError.Validation($"1 validation error detected: Value at '{member}' failed to satisfy constraint: Member must have length less than or equal to {max}");
        }
    }

    /// <summary>Gets the one action of an element, such as the <c>Put</c> of <c>{"Put":{...}}</c>.</summary>
    /// <param name="value">The element.</param>
    /// <param name="where">What it is, for the message.</param>
    /// <param name="message">The message of DynamoDB for an element with none or more than one.</param>
    /// <param name="actions">The actions it may hold.</param>
    /// <returns>The name of the action and its object.</returns>
    /// <exception cref="FakeError">A <c>ValidationException</c> for none or more than one, an <c>UnsupportedByFakeException</c> for another member.</exception>
    public static (string Name, JsonElement Value) OneOf(JsonElement value, string where, string message, params ReadOnlySpan<string> actions)
    {
        RequestJson.Members(value, where, actions);
        var present = RequestJson.ReadObject(value, where);
        return present.Count == 1 ? (present[0].Name, present[0].Value) : throw FakeError.Validation(message);
    }

    /// <summary>Adds the key of an item to those of the request so far.</summary>
    /// <param name="seen">The keys so far, with their tables.</param>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <param name="message">The message of DynamoDB for a key that comes twice.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static void ThrowIfSeen(HashSet<(string Table, ItemKey Key)> seen, FakeTable table, ItemKey key, string message)
    {
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(table);
        if (!seen.Add((table.Name, key)))
        {
            throw FakeError.Validation(message);
        }
    }
}
