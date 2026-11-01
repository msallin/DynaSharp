using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class ListTablesOperationTests
{
    private static readonly Dictionary<string, ListTablesRequest> BotocoreRequests = new()
    {
        ["NothingSet"] = new(),
        ["Limit"] = new() { Limit = 3 },
        ["AllOptions"] = new() { ExclusiveStartTableName = "Forum", Limit = 100 },
        ["LimitOne"] = new() { Limit = 1 },
        ["ExclusiveStartTableNameOnly"] = new() { ExclusiveStartTableName = "Orders" },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("ListTables");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("ListTables");

    [Test]
    public async Task Name_Always_IsListTables()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = ListTablesOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("ListTables");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("ListTables", "NothingSet");

        // Act
        var target = WireProtocol.TargetPrefix + ListTablesOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_AnyRequest_ReturnsNull()
    {
        // Arrange
        var request = new ListTablesRequest { ExclusiveStartTableName = "Forum" };

        // Act
        var tableName = ListTablesOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("ListTables", name);
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
        var request = new ListTablesRequest();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("{}");
    }

    [Test]
    [Arguments(int.MinValue)]
    [Arguments(0)]
    [Arguments(100)]
    [Arguments(int.MaxValue)]
    public async Task WriteRequest_Limit_IsPassedThrough(int limit)
    {
        // Arrange
        var request = new ListTablesRequest { Limit = limit };

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
        var vector = OperationVectors.Response("ListTables", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithEmptyListIfAbsent(vector.Parsed, "TableNames"), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_LastPage_HasNoLastEvaluatedTableName()
    {
        // Arrange
        var vector = OperationVectors.Response("ListTables", "LastPage");

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That((response.TableNames.Count, response.LastEvaluatedTableName)).IsEqualTo((2, (string?)null));
    }

    [Test]
    public async Task ReadResponse_NothingAtAll_ReturnsAnEmptyList()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.TableNames).IsEmpty();
    }

    [Test]
    public async Task ReadResponse_TableNamesThatIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"TableNames":{}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_TableNames_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"TableNames":["a-table"]}""";

        // Act
        var names = Read(json).TableNames;

        // Assert
        await Assert.That(names is IList<string> { IsReadOnly: true }).IsTrue();
    }

    [Test]
    public async Task SetActivityTags_NothingSet_SetsNoTag()
    {
        // Arrange
        var request = new ListTablesRequest();
        using var activity = new Activity("test");

        // Act
        ListTablesOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task SetActivityTags_LimitAndStartTable_SetsBothTags()
    {
        // Arrange
        var request = new ListTablesRequest { ExclusiveStartTableName = "Forum", Limit = 25 };
        using var activity = new Activity("test");

        // Act
        ListTablesOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.exclusive_start_table=Forum,aws.dynamodb.limit=25");
    }

    [Test]
    public async Task SetResponseTags_Page_SetsTheTableCount()
    {
        // Arrange
        var response = new ListTablesResponse { TableNames = ["a-table", "b-table", "c-table"] };
        using var activity = new Activity("test");

        // Act
        ListTablesOperation.SetResponseTags(activity, response);

        // Assert
        await Assert.That(activity.GetTagItem("aws.dynamodb.table_count")).IsEqualTo(3);
    }

    private static string Write(ListTablesRequest request) => TestJson.Write(writer => ListTablesOperation.WriteRequest(writer, request));

    private static ListTablesResponse Read(string json) => TestJson.Read(json, static (ref reader) => ListTablesOperation.ReadResponse(ref reader));
}
