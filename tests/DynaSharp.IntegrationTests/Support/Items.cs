using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Support;

/// <summary>Builders and shortcuts that keep the tests about the operation under test, not about request plumbing.</summary>
internal static class Items
{
    /// <summary>Builds the key <c>{ pk }</c>.</summary>
    /// <param name="pk">The partition key.</param>
    /// <returns>The key.</returns>
    public static Dictionary<string, AttributeValue> Key(string pk) => new() { ["pk"] = pk };

    /// <summary>Builds the key <c>{ pk, sk }</c> with a number sort key.</summary>
    /// <param name="pk">The partition key.</param>
    /// <param name="sk">The sort key.</param>
    /// <returns>The key.</returns>
    public static Dictionary<string, AttributeValue> Key(string pk, int sk) => new() { ["pk"] = pk, ["sk"] = AttributeValue.FromNumber(sk) };

    /// <summary>Builds the item <c>{ pk, v }</c>.</summary>
    /// <param name="pk">The partition key.</param>
    /// <param name="value">The value of the attribute <c>v</c>.</param>
    /// <returns>The item.</returns>
    public static Dictionary<string, AttributeValue> Item(string pk, AttributeValue value) => new() { ["pk"] = pk, ["v"] = value };

    /// <summary>Wraps a map of expression attribute values for the request records, which take read-only dictionaries.</summary>
    /// <param name="values">Pairs of placeholder (with the colon) and value.</param>
    /// <returns>The dictionary.</returns>
    public static Dictionary<string, AttributeValue> Values(params (string Placeholder, AttributeValue Value)[] values) =>
        values.ToDictionary(static pair => pair.Placeholder, static pair => pair.Value);

    /// <summary>Writes an item <c>{ pk, v = value }</c> and reads it back with a strongly consistent read.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The table, with a string partition key <c>pk</c>.</param>
    /// <param name="value">The value to store as <c>v</c>.</param>
    /// <returns>The value as DynamoDB returns it.</returns>
    public static async Task<AttributeValue> RoundTripAsync(DynamoDbClient client, string table, AttributeValue value)
    {
        await client.PutItemAsync(new PutItemRequest { TableName = table, Item = Item("round-trip", value) });
        var response = await client.GetItemAsync(new GetItemRequest { TableName = table, Key = Key("round-trip"), ConsistentRead = true });
        return response.Item!["v"];
    }

    /// <summary>Puts items <c>{ pk, sk = 1..count, v = sk, gsi_pk = "g", gsi_sk = "s" + sk }</c> into a table with a number sort key.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The table.</param>
    /// <param name="pk">The partition key all items share.</param>
    /// <param name="count">How many items.</param>
    /// <returns>A task that completes when all items are written.</returns>
    public static async Task PutSequenceAsync(DynamoDbClient client, string table, string pk, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            var item = Key(pk, i);
            item["v"] = AttributeValue.FromNumber(i);
            item["gsi_pk"] = "g";
            item["gsi_sk"] = $"s{i}";
            await client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });
        }
    }

    /// <summary>Runs <paramref name="act"/> and returns the exception it throws.</summary>
    /// <typeparam name="TException">The exact type of the expected exception.</typeparam>
    /// <param name="act">The call that must fail.</param>
    /// <returns>The exception, for the assertions.</returns>
    /// <exception cref="InvalidOperationException">The call did not throw, or threw another type.</exception>
    public static async Task<TException> FailureOfAsync<TException>(Func<Task> act)
        where TException : Exception
    {
        try
        {
            await act();
        }
        catch (Exception exception) when (exception is TException expected && exception.GetType() == typeof(TException))
        {
            return expected;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but the call succeeded.");
    }
}
