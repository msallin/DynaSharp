namespace DynaSharp.UnitTests.Testing.Fake;

/// <summary>Tables for the tests of writes, batches and transactions.</summary>
internal static class FakeWrites
{
    /// <summary>A table keyed by <c>pk</c> and <c>sk</c>, strings, with a local index <c>ByDate</c> on <c>date</c> (KEYS_ONLY) and a global index <c>ByAuthor</c> on <c>author</c> (ALL), on demand.</summary>
    public const string Forum = """
        {"TableName":"Forum","BillingMode":"PAY_PER_REQUEST",
         "AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"S"},{"AttributeName":"date","AttributeType":"S"},{"AttributeName":"author","AttributeType":"S"}],
         "KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],
         "LocalSecondaryIndexes":[{"IndexName":"ByDate","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"date","KeyType":"RANGE"}],"Projection":{"ProjectionType":"KEYS_ONLY"}}],
         "GlobalSecondaryIndexes":[{"IndexName":"ByAuthor","KeySchema":[{"AttributeName":"author","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}]}
        """;

    /// <summary>A string of the given length, for an item of a known size.</summary>
    public static string Text(int length) => new('x', length);
}
