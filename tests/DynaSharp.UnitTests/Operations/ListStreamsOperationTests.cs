using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Streams;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class ListStreamsOperationTests
{
    private const string StreamArn = "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-20T20:51:10.252";

    private static readonly Dictionary<string, ListStreamsRequest> BotocoreRequests = new()
    {
        ["RetrieveAllStreamArnsFromApiReference"] = new(),
        ["OneTable"] = new() { TableName = "Forum" },
        ["AllOptions"] = new() { TableName = "Forum", Limit = 25, ExclusiveStartStreamArn = StreamArn },
        ["LimitIntMax"] = new() { Limit = int.MaxValue },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("ListStreams");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("ListStreams");

    [Test]
    public async Task Name_Always_IsListStreams()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = ListStreamsOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("ListStreams");
    }

    [Test]
    public async Task GetTableName_TableGiven_ReturnsIt()
    {
        // Arrange
        var request = new ListStreamsRequest { TableName = "Forum" };

        // Act
        var tableName = ListStreamsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Forum");
    }

    [Test]
    public async Task GetTableName_NoTable_ReturnsNull()
    {
        // Arrange
        var request = new ListStreamsRequest();

        // Act
        var tableName = ListStreamsOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("ListStreams", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_NothingSet_WritesAnEmptyObject()
    {
        // Arrange
        var request = new ListStreamsRequest();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    [Arguments(int.MinValue)]
    [Arguments(0)]
    [Arguments(100)]
    public async Task WriteRequest_Limit_IsPassedThrough(int limit)
    {
        // Arrange
        var request = new ListStreamsRequest { Limit = limit };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"Limit":{{limit}}}""");
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("ListStreams", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithEmptyListIfAbsent(vector.Parsed, "Streams"), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_LastPage_HasNoLastEvaluatedStreamArn()
    {
        // Arrange
        var vector = OperationVectors.Response("ListStreams", "RetrieveAllStreamArnsFromApiReference");

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That((response.Streams.Count, response.LastEvaluatedStreamArn)).IsEqualTo((3, (string?)null));
    }

    [Test]
    public async Task ReadResponse_StreamWithoutArn_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Streams":[{"TableName":"Forum"}]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("A stream has no StreamArn.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_StreamsThatIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Streams":{}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_Streams_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"Streams":[{"StreamArn":"a"}]}""";

        // Act
        var streams = Read(json).Streams;

        // Assert
        await Assert.That(streams is IList<StreamSummary> { IsReadOnly: true }).IsTrue();
    }

    [Test]
    public async Task SetActivityTags_NoLimit_SetsNoTag()
    {
        // Arrange
        var request = new ListStreamsRequest { TableName = "Forum" };
        using var activity = new Activity("test");

        // Act
        ListStreamsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task SetActivityTags_Limit_SetsTheLimitTag()
    {
        // Arrange
        var request = new ListStreamsRequest { Limit = 25 };
        using var activity = new Activity("test");

        // Act
        ListStreamsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.limit")).IsEqualTo(25);
    }

    private static string Write(ListStreamsRequest request) => TestJson.Write(writer => ListStreamsOperation.WriteRequest(writer, request));

    private static ListStreamsResponse Read(string json) => TestJson.Read(json, static (ref reader) => ListStreamsOperation.ReadResponse(ref reader));
}
