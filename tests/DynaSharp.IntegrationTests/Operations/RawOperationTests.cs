using System.Text;
using System.Text.Json;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>
/// SendRawAsync against the emulators: operations the library has no request type for, a parameter that a request type does not
/// carry, and the errors of a raw call.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class RawOperationTests(Backend backend)
{
    [Test]
    public async Task SendRawAsync_DescribeLimits_ReturnsTheLimitsOfTheAccount()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.DescribeLimits);
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var response = await sandbox.Client.SendRawAsync("DescribeLimits", "{}"u8.ToArray());

        // Assert
        using var document = JsonDocument.Parse(response);
        await Assert.That(document.RootElement.GetProperty("TableMaxReadCapacityUnits").GetInt64()).IsGreaterThan(0);
    }

    [Test]
    public async Task SendRawAsync_ExecuteStatement_ReadsAnItemWithPartiQl()
    {
        // Arrange (PartiQL has no request type in the library, and both emulators serve it)
        Gaps.SkipIfMissing(backend, Gap.PartiQl);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("partiql-1", "found") });
        var request = "{\"Statement\":\"SELECT * FROM \\\"" + table + "\\\" WHERE pk = 'partiql-1'\"}";

        // Act
        var response = await sandbox.Client.SendRawAsync("ExecuteStatement", Encoding.UTF8.GetBytes(request));

        // Assert
        using var document = JsonDocument.Parse(response);
        await Assert.That(document.RootElement.GetProperty("Items")[0].GetProperty("v").GetProperty("S").GetString()).IsEqualTo("found");
    }

    [Test]
    public async Task SendRawAsync_ListTables_ListsTheTableTheRequestApiCreated()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.SendRawAsync("ListTables", """{"Limit":100}"""u8.ToArray());

        // Assert
        using var document = JsonDocument.Parse(response);
        var names = document.RootElement.GetProperty("TableNames").EnumerateArray().Select(static name => name.GetString());
        await Assert.That(names).Contains(table);
    }

    [Test]
    public async Task SendRawAsync_PutItem_WritesAnItemTheRequestApiReads()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = "{\"TableName\":\"" + table + "\",\"Item\":{\"pk\":{\"S\":\"raw-1\"},\"nested\":{\"M\":{\"n\":{\"N\":\"1.50\"},\"flag\":{\"BOOL\":true}}}}}";

        // Act
        await sandbox.Client.SendRawAsync("PutItem", Encoding.UTF8.GetBytes(json));

        // Assert
        var item = (await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("raw-1"), ConsistentRead = true })).Item!;
        await Assert.That(item["nested"].AsMap()["flag"].AsBool()).IsTrue();
    }

    [Test]
    public async Task SendRawAsync_GetItem_ReturnsTheItemAsDynamoDbSendsIt()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("raw-2", "x") });

        // Act
        var response = await sandbox.Client.SendRawAsync("GetItem", Encoding.UTF8.GetBytes($$$$"""{"TableName":"{{{{table}}}}","Key":{"pk":{"S":"raw-2"}},"ConsistentRead":true}"""));

        // Assert
        using var document = JsonDocument.Parse(response);
        await Assert.That(document.RootElement.GetProperty("Item").GetProperty("pk").GetProperty("S").GetString()).IsEqualTo("raw-2");
    }

    [Test]
    public async Task SendRawAsync_MissingTable_ThrowsResourceNotFoundExceptionNamingTheOperation()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var json = Encoding.UTF8.GetBytes($$$$"""{"TableName":"{{{{Tables.UniqueName()}}}}","Key":{"pk":{"S":"a"}}}""");

        // Act
        var exception = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.SendRawAsync("GetItem", json));

        // Assert
        await Assert.That(exception.OperationName).IsEqualTo("GetItem");
    }

    [Test]
    public async Task SendRawAsync_RequestWithoutItsKey_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var json = Encoding.UTF8.GetBytes($$$$"""{"TableName":"{{{{table}}}}"}""");

        // Act
        Func<Task> act = () => sandbox.Client.SendRawAsync("GetItem", json);

        // Assert
        await Assert.That(act).Throws<DynamoDbException>();
    }

    [Test]
    public async Task SendRawAsync_UnknownOperation_ThrowsDynamoDbException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        Func<Task> act = () => sandbox.Client.SendRawAsync("DescribeNothingAtAll", "{}"u8.ToArray());

        // Assert
        await Assert.That(act).Throws<DynamoDbException>();
    }

    [Test]
    public async Task SendRawAsync_StreamsListStreams_ReturnsTheStreamsOfTheAccount()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.Streams);
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var response = await sandbox.Streams.SendRawAsync("ListStreams", "{}"u8.ToArray());

        // Assert
        using var document = JsonDocument.Parse(response);
        await Assert.That(document.RootElement.GetProperty("Streams").ValueKind).IsEqualTo(JsonValueKind.Array);
    }
}
