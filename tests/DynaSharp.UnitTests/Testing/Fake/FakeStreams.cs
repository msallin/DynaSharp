using System.Text.Json;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

/// <summary>Tables with streams and readers of their records, for the tests of the streams of the fake, with the names a fake on the clock of <see cref="FakeCalls.With"/> gives them.</summary>
internal static class FakeStreams
{
    /// <summary>The label of the first stream a table gets at 2026-01-01T00:00:00Z.</summary>
    public const string Label = "2026-01-01T00:00:00.000";

    /// <summary>The ARN of the first stream of <c>Users</c>.</summary>
    public const string UsersArn = "arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/" + Label;

    /// <summary>The shard of the first stream of a fake opened at 2026-01-01T00:00:00Z.</summary>
    public const string FirstShard = "shardId-00000001767225600000-00000001";

    /// <summary>The starting sequence number of the first shard of a fake; its first record has the next one.</summary>
    public const string FirstSequenceNumber = "100000000000000000000";

    /// <summary>The body that creates <see cref="FakeCalls.Users"/> with a stream of the given view type.</summary>
    public static string Users(string viewType) => FakeCalls.Users.TrimEnd()[..^1] + $$$""","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"{{{viewType}}}"}}""";

    /// <summary>Makes a fake on the given clock with <c>Users</c> and its stream.</summary>
    public static FakeDynamoDb UsersOn(TimeProvider clock, string viewType = "NEW_AND_OLD_IMAGES")
    {
        var fake = new FakeDynamoDb(clock);
        FakeCalls.Ok(fake, "CreateTable", Users(viewType));
        return fake;
    }

    /// <summary>Puts an item of <c>Users</c> with the key and a number <c>v</c>.</summary>
    public static void Put(FakeDynamoDb fake, string key, int v) =>
        FakeCalls.Ok(fake, "PutItem", $$$$"""{"TableName":"Users","Item":{"pk":{"S":"{{{{key}}}}"},"v":{"N":"{{{{v}}}}"}}}""");

    /// <summary>Deletes an item of <c>Users</c>.</summary>
    public static void Delete(FakeDynamoDb fake, string key) =>
        FakeCalls.Ok(fake, "DeleteItem", $$$$"""{"TableName":"Users","Key":{"pk":{"S":"{{{{key}}}}"}}}""");

    /// <summary>Gets an iterator of the first shard of a stream.</summary>
    public static string Iterator(FakeDynamoDb fake, string type, string? sequenceNumber = null, string arn = UsersArn) =>
        FakeCalls.Ok(fake, "GetShardIterator", $$"""{"StreamArn":"{{arn}}","ShardId":"{{FirstShard}}","ShardIteratorType":"{{type}}"{{(sequenceNumber is null ? string.Empty : $",\"SequenceNumber\":\"{sequenceNumber}\"")}}}""")
            .GetProperty("ShardIterator").GetString()!;

    /// <summary>Reads one page from an iterator.</summary>
    public static JsonElement Page(FakeDynamoDb fake, string iterator, int? limit = null) =>
        FakeCalls.Ok(fake, "GetRecords", $$"""{"ShardIterator":"{{iterator}}"{{(limit is null ? string.Empty : $",\"Limit\":{limit}")}}}""");

    /// <summary>Reads the records of the shard of <c>Users</c> from its trim horizon, one page.</summary>
    public static IReadOnlyList<JsonElement> Records(FakeDynamoDb fake, string arn = UsersArn) =>
        [.. Page(fake, Iterator(fake, "TRIM_HORIZON", arn: arn)).GetProperty("Records").EnumerateArray()];

    /// <summary>Gives the event names of records, in order.</summary>
    public static IReadOnlyList<string> Names(IEnumerable<JsonElement> records) => [.. records.Select(static record => record.GetProperty("eventName").GetString()!)];
}
