using System.Text.Json;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

/// <summary>The table the tests of <c>Query</c> and <c>Scan</c> read, with hand-written bodies, and helpers that follow pages.</summary>
internal static class ReadTables
{
    /// <summary>
    /// A table of orders: string partition key <c>pk</c>, number sort key <c>sk</c>, a global index <c>ByCustomer</c> on the string <c>customer</c> of keys only, a local index
    /// <c>ByStatus</c> on the string <c>status</c> of keys only, and one <c>ByTotal</c> on the number <c>total</c> that includes <c>v</c>.
    /// </summary>
    public const string Shop = """
        {"TableName":"Shop","BillingMode":"PAY_PER_REQUEST",
         "AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"N"},{"AttributeName":"customer","AttributeType":"S"},
                                 {"AttributeName":"status","AttributeType":"S"},{"AttributeName":"total","AttributeType":"N"}],
         "KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],
         "LocalSecondaryIndexes":[{"IndexName":"ByStatus","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"status","KeyType":"RANGE"}],"Projection":{"ProjectionType":"KEYS_ONLY"}}],
         "GlobalSecondaryIndexes":[
           {"IndexName":"ByCustomer","KeySchema":[{"AttributeName":"customer","KeyType":"HASH"}],"Projection":{"ProjectionType":"KEYS_ONLY"}},
           {"IndexName":"ByTotal","KeySchema":[{"AttributeName":"customer","KeyType":"HASH"},{"AttributeName":"total","KeyType":"RANGE"}],"Projection":{"ProjectionType":"INCLUDE","NonKeyAttributes":["v"]}}]}
        """;

    /// <summary>Makes a fake with the table <see cref="Shop"/> and, per partition, the items with sort keys 1 to the count: <c>status</c> <c>s&lt;sk&gt;</c>, <c>customer</c> <c>k</c>, <c>total</c> 10 times the sort key, <c>v</c> the sort key.</summary>
    public static FakeDynamoDb ShopWith(params (string Partition, int Count)[] partitions)
    {
        var fake = FakeCalls.With(Shop);
        foreach (var (partition, count) in partitions)
        {
            for (var sk = 1; sk <= count; sk++)
            {
                FakeCalls.Ok(
                    fake,
                    "PutItem",
                    $$$$"""{"TableName":"Shop","Item":{"pk":{"S":"{{{{partition}}}}"},"sk":{"N":"{{{{sk}}}}"},"status":{"S":"s{{{{sk}}}}"},"customer":{"S":"k"},"total":{"N":"{{{{sk * 10}}}}"},"v":{"N":"{{{{sk}}}}"}}}""");
            }
        }

        return fake;
    }

    /// <summary>Gives <c>pk/sk</c> of every item of an answer, in its order.</summary>
    public static IReadOnlyList<string> Keys(JsonElement body) =>
        [.. body.GetProperty("Items").EnumerateArray().Select(static item => $"{item.GetProperty("pk").GetProperty("S").GetString()}/{item.GetProperty("sk").GetProperty("N").GetString()}")];

    /// <summary>Follows <c>LastEvaluatedKey</c> to the end and gives <c>pk/sk</c> of every item read.</summary>
    /// <param name="fake">The fake.</param>
    /// <param name="operation"><c>Query</c> or <c>Scan</c>.</param>
    /// <param name="request">The request without its closing brace, so that the start key can be added.</param>
    public static List<string> ReadAll(FakeDynamoDb fake, string operation, string request)
    {
        var keys = new List<string>();
        string? start = null;
        do
        {
            var body = FakeCalls.Ok(fake, operation, request + (start is null ? "}" : $$""","ExclusiveStartKey":{{start}}}"""));
            keys.AddRange(Keys(body));
            start = body.TryGetProperty("LastEvaluatedKey", out var key) ? key.GetRawText() : null;
        }
        while (start is not null);

        return keys;
    }
}
