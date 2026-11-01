using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Streams;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class GetShardIteratorOperationTests
{
    private const string StreamArn = "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-20T20:51:10.252";
    private const string ShardId = "shardId-00000001414576573621-f55eea83";

    private static readonly Dictionary<string, GetShardIteratorRequest> BotocoreRequests = new()
    {
        ["TrimHorizonFromApiReference"] = new() { StreamArn = StreamArn, ShardId = "00000001414576573621-f55eea83", ShardIteratorType = ShardIteratorType.TrimHorizon },
        ["Latest"] = Request(ShardIteratorType.Latest),
        ["AtSequenceNumber"] = Request(ShardIteratorType.AtSequenceNumber) with { SequenceNumber = "300000000000000499659" },
        ["AfterSequenceNumber"] = Request(ShardIteratorType.AfterSequenceNumber) with { SequenceNumber = "300000000000000499659" },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("GetShardIterator");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("GetShardIterator");

    public static IEnumerable<(ShardIteratorType Value, string Wire)> IteratorTypes()
    {
        yield return (ShardIteratorType.TrimHorizon, "TRIM_HORIZON");
        yield return (ShardIteratorType.Latest, "LATEST");
        yield return (ShardIteratorType.AtSequenceNumber, "AT_SEQUENCE_NUMBER");
        yield return (ShardIteratorType.AfterSequenceNumber, "AFTER_SEQUENCE_NUMBER");
    }

    [Test]
    public async Task Name_Always_IsGetShardIterator()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = GetShardIteratorOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("GetShardIterator");
    }

    [Test]
    public async Task GetTableName_Request_ReturnsNull()
    {
        // Arrange
        var request = Request(ShardIteratorType.Latest);

        // Act
        var tableName = GetShardIteratorOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task BotocoreRequests_Always_CoverEveryVector()
    {
        // Arrange (the vectors come from the generator, the requests from this class)

        // Act
        var missing = RequestVectorNames().Except(BotocoreRequests.Keys).Concat(BotocoreRequests.Keys.Except(RequestVectorNames()));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(RequestVectorNames))]
    public async Task WriteRequest_BotocoreVector_WritesTheJsonThatBotocoreWrites(string name)
    {
        // Arrange
        var vector = OperationVectors.Request("GetShardIterator", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    [MethodDataSource(nameof(IteratorTypes))]
    public async Task WriteRequest_IteratorType_WritesTheWireName((ShardIteratorType Value, string Wire) type)
    {
        // Arrange
        var request = Request(type.Value);

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($"\"ShardIteratorType\":\"{type.Wire}\"");
    }

    [Test]
    public async Task WriteRequest_NoSequenceNumber_OmitsIt()
    {
        // Arrange
        var request = Request(ShardIteratorType.TrimHorizon);

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("SequenceNumber");
    }

    [Test]
    [Arguments(ShardIteratorType.Unknown)]
    [Arguments((ShardIteratorType)99)]
    public async Task WriteRequest_IteratorTypeThatCannotBeSent_ThrowsArgumentOutOfRangeException(ShardIteratorType type)
    {
        // Arrange
        var request = Request(type);

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("ShardIteratorType", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("GetShardIterator", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Parsed, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_NoIterator_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Unknown":1}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The GetShardIterator response has no ShardIterator.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_NullIterator_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"ShardIterator":null}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("found null", StringComparison.Ordinal);
    }

    private static GetShardIteratorRequest Request(ShardIteratorType type) => new() { StreamArn = StreamArn, ShardId = ShardId, ShardIteratorType = type };

    private static string Write(GetShardIteratorRequest request) => TestJson.Write(writer => GetShardIteratorOperation.WriteRequest(writer, request));

    private static GetShardIteratorResponse Read(string json) => TestJson.Read(json, static (ref reader) => GetShardIteratorOperation.ReadResponse(ref reader));
}
