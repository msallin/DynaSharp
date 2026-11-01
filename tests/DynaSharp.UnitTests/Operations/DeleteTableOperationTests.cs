using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class DeleteTableOperationTests
{
    private static readonly Dictionary<string, DeleteTableRequest> BotocoreRequests = new()
    {
        ["DeleteATableFromApiReference"] = new() { TableName = "Reply" },
        ["TableArn"] = new() { TableName = "arn:aws:dynamodb:us-west-2:123456789012:table/Reply" },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("DeleteTable");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("DeleteTable");

    [Test]
    public async Task Name_Always_IsDeleteTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = DeleteTableOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("DeleteTable");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("DeleteTable", "TableArn");

        // Act
        var target = WireProtocol.TargetPrefix + DeleteTableOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new DeleteTableRequest { TableName = "Reply" };

        // Act
        var tableName = DeleteTableOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Reply");
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
        var vector = OperationVectors.Request("DeleteTable", name);
        var request = BotocoreRequests[name];

        // Act
        var json = TestJson.Write(writer => DeleteTableOperation.WriteRequest(writer, request));

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("DeleteTable", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithTableDefaults(vector.Parsed), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheDeletingTable()
    {
        // Arrange
        var vector = OperationVectors.Response("DeleteTable", "DeleteATableFromApiReference");

        // Act
        var table = Read(vector.Wire).TableDescription;

        // Assert
        await Assert.That((table.TableName, table.TableStatus)).IsEqualTo(("Reply", TableStatus.Deleting));
    }

    [Test]
    public async Task ReadResponse_NoTableDescription_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Unknown":[1]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The DeleteTable response has no TableDescription.", StringComparison.Ordinal);
    }

    private static DeleteTableResponse Read(string json) => TestJson.Read(json, static (ref reader) => DeleteTableOperation.ReadResponse(ref reader));
}
