using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class DescribeTimeToLiveOperationTests
{
    private static readonly Dictionary<string, DescribeTimeToLiveRequest> BotocoreRequests = new()
    {
        ["TableName"] = new() { TableName = "TTLExample" },
        ["TableArn"] = new() { TableName = "arn:aws:dynamodb:us-west-2:123456789012:table/TTLExample" },
        ["SpecialCharactersInTableName"] = new() { TableName = OperationVectors.SpecialText },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("DescribeTimeToLive");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("DescribeTimeToLive");

    public static IEnumerable<(string Wire, TimeToLiveStatus Status)> Statuses()
    {
        yield return ("ENABLING", TimeToLiveStatus.Enabling);
        yield return ("DISABLING", TimeToLiveStatus.Disabling);
        yield return ("ENABLED", TimeToLiveStatus.Enabled);
        yield return ("DISABLED", TimeToLiveStatus.Disabled);
        yield return ("SOMETHING_NEW", TimeToLiveStatus.Unknown);
    }

    [Test]
    public async Task Name_Always_IsDescribeTimeToLive()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = DescribeTimeToLiveOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("DescribeTimeToLive");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("DescribeTimeToLive", "TableName");

        // Act
        var target = WireProtocol.TargetPrefix + DescribeTimeToLiveOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new DescribeTimeToLiveRequest { TableName = "TTLExample" };

        // Act
        var tableName = DescribeTimeToLiveOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("DescribeTimeToLive", name);
        var request = BotocoreRequests[name];

        // Act
        var json = TestJson.Write(writer => DescribeTimeToLiveOperation.WriteRequest(writer, request));

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeTimeToLive", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Parsed, ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_NoDescription_ReturnsNull()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.TimeToLiveDescription).IsNull();
    }

    [Test]
    public async Task ReadResponse_NeverEnabled_HasNoAttributeName()
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeTimeToLive", "NeverEnabled");

        // Act
        var description = Read(vector.Wire).TimeToLiveDescription;

        // Assert
        await Assert.That((description!.TimeToLiveStatus, description.AttributeName)).IsEqualTo((TimeToLiveStatus.Disabled, (string?)null));
    }

    [Test]
    [MethodDataSource(nameof(Statuses))]
    public async Task ReadResponse_Status_MapsToTheEnum(string wire, TimeToLiveStatus expected)
    {
        // Arrange
        var json = $$$"""{"TimeToLiveDescription":{"TimeToLiveStatus":"{{{wire}}}"}}""";

        // Act
        var description = Read(json).TimeToLiveDescription;

        // Assert
        await Assert.That(description!.TimeToLiveStatus).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadResponse_DescriptionThatIsNotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"TimeToLiveDescription":[]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>();
    }

    private static DescribeTimeToLiveResponse Read(string json) => TestJson.Read(json, static (ref reader) => DescribeTimeToLiveOperation.ReadResponse(ref reader));
}
