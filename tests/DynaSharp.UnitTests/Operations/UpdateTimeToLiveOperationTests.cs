using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class UpdateTimeToLiveOperationTests
{
    private static readonly Dictionary<string, UpdateTimeToLiveRequest> BotocoreRequests = new()
    {
        ["Enable"] = Request(true, "ttl"),
        ["Disable"] = Request(false, "ttl"),
        ["SpecialCharactersInAttributeName"] = Request(true, OperationVectors.SpecialText),
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("UpdateTimeToLive");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("UpdateTimeToLive");

    [Test]
    public async Task Name_Always_IsUpdateTimeToLive()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = UpdateTimeToLiveOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("UpdateTimeToLive");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("UpdateTimeToLive", "Enable");

        // Act
        var target = WireProtocol.TargetPrefix + UpdateTimeToLiveOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = Request(true, "ttl");

        // Act
        var tableName = UpdateTimeToLiveOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("TTLExample");
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
        var vector = OperationVectors.Request("UpdateTimeToLive", name);
        var request = BotocoreRequests[name];

        // Act
        var json = TestJson.Write(writer => UpdateTimeToLiveOperation.WriteRequest(writer, request));

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_Disable_WritesEnabledFalse()
    {
        // Arrange
        var request = Request(false, "ttl");

        // Act
        var json = TestJson.Write(writer => UpdateTimeToLiveOperation.WriteRequest(writer, request));

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"TTLExample","TimeToLiveSpecification":{"Enabled":false,"AttributeName":"ttl"}}""");
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("UpdateTimeToLive", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Parsed, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_NoSpecification_ReturnsNull()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.TimeToLiveSpecification).IsNull();
    }

    [Test]
    public async Task ReadResponse_SpecificationWithoutEnabled_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"TimeToLiveSpecification":{"AttributeName":"ttl"}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The TimeToLiveSpecification has no Enabled.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_SpecificationWithoutAttributeName_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"TimeToLiveSpecification":{"Enabled":true}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The TimeToLiveSpecification has no AttributeName.", StringComparison.Ordinal);
    }

    private static UpdateTimeToLiveRequest Request(bool enabled, string attributeName) => new()
    {
        TableName = "TTLExample",
        TimeToLiveSpecification = new() { Enabled = enabled, AttributeName = attributeName },
    };

    private static UpdateTimeToLiveResponse Read(string json) => TestJson.Read(json, static (ref reader) => UpdateTimeToLiveOperation.ReadResponse(ref reader));
}
