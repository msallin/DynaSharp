using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class UpdateTableOperationTests
{
    private static readonly KeySchemaElement[] ByStatusKey = [new("status", KeyType.Hash), new("createdAt", KeyType.Range)];

    private static readonly Dictionary<string, UpdateTableRequest> BotocoreRequests = new()
    {
        ["OnlyTheTableName"] = Request(),
        ["BillingModePayPerRequest"] = Request() with { BillingMode = BillingMode.PayPerRequest },
        ["BillingModeProvisionedWithIndexCapacity"] = Request() with
        {
            BillingMode = BillingMode.Provisioned,
            ProvisionedThroughput = new() { ReadCapacityUnits = 5, WriteCapacityUnits = 6 },
            GlobalSecondaryIndexUpdates = [new() { Update = new() { IndexName = "ByStatus", ProvisionedThroughput = new() { ReadCapacityUnits = 3, WriteCapacityUnits = 2 } } }],
        },
        ["ProvisionedThroughputOnly"] = Request() with { ProvisionedThroughput = new() { ReadCapacityUnits = 10, WriteCapacityUnits = 20 } },
        ["LargeProvisionedThroughput"] = Request() with { ProvisionedThroughput = new() { ReadCapacityUnits = long.MaxValue, WriteCapacityUnits = 1 } },
        ["StreamEnabled"] = Request() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages } },
        ["StreamDisabled"] = Request() with { StreamSpecification = new() { StreamEnabled = false } },
        ["GlobalSecondaryIndexCreateProvisioned"] = Request() with
        {
            AttributeDefinitions = [new("status", ScalarAttributeType.String), new("createdAt", ScalarAttributeType.Number)],
            GlobalSecondaryIndexUpdates =
            [
                new()
                {
                    Create = new()
                    {
                        IndexName = "ByStatus",
                        KeySchema = ByStatusKey,
                        Projection = new() { ProjectionType = ProjectionType.Include, NonKeyAttributes = ["total", "customer"] },
                        ProvisionedThroughput = new() { ReadCapacityUnits = 3, WriteCapacityUnits = 2 },
                    },
                },
            ],
        },
        ["GlobalSecondaryIndexCreateOnDemand"] = Request() with
        {
            AttributeDefinitions = [new("status", ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates = [new() { Create = Index(ProjectionType.KeysOnly) }],
        },
        ["GlobalSecondaryIndexCreateAllProjection"] = Request() with
        {
            AttributeDefinitions = [new("status", ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates = [new() { Create = Index(ProjectionType.All) }],
        },
        ["GlobalSecondaryIndexUpdateCapacity"] = Request() with
        {
            GlobalSecondaryIndexUpdates = [new() { Update = new() { IndexName = "ByStatus", ProvisionedThroughput = new() { ReadCapacityUnits = 1, WriteCapacityUnits = 1 } } }],
        },
        ["GlobalSecondaryIndexDelete"] = Request() with { GlobalSecondaryIndexUpdates = [new() { Delete = new() { IndexName = "ByStatus" } }] },
        ["GlobalSecondaryIndexUpdatesOfEveryKind"] = Request() with
        {
            AttributeDefinitions = [new("customer", ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates =
            [
                new() { Delete = new() { IndexName = "ByStatus" } },
                new() { Update = new() { IndexName = "ByCreatedAt", ProvisionedThroughput = new() { ReadCapacityUnits = 4, WriteCapacityUnits = 4 } } },
                new() { Create = new() { IndexName = "ByCustomer", KeySchema = [new("customer", KeyType.Hash)], Projection = new() { ProjectionType = ProjectionType.All } } },
            ],
        },
        ["DeletionProtectionOn"] = Request() with { DeletionProtectionEnabled = true },
        ["DeletionProtectionOff"] = Request() with { DeletionProtectionEnabled = false },
        ["TableClassStandard"] = Request() with { TableClass = TableClass.Standard },
        ["TableClassStandardInfrequentAccess"] = Request() with { TableClass = TableClass.StandardInfrequentAccess },
        ["TableArnAsName"] = Request(OperationVectors.TableArn) with { DeletionProtectionEnabled = true },
        ["AllOptions"] = Request() with
        {
            AttributeDefinitions = [new("status", ScalarAttributeType.String), new("createdAt", ScalarAttributeType.Number)],
            BillingMode = BillingMode.Provisioned,
            ProvisionedThroughput = new() { ReadCapacityUnits = 10, WriteCapacityUnits = 20 },
            GlobalSecondaryIndexUpdates =
            [
                new()
                {
                    Create = new()
                    {
                        IndexName = "ByStatus",
                        KeySchema = ByStatusKey,
                        Projection = new() { ProjectionType = ProjectionType.All },
                        ProvisionedThroughput = new() { ReadCapacityUnits = 3, WriteCapacityUnits = 2 },
                    },
                },
            ],
            StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.KeysOnly },
            TableClass = TableClass.StandardInfrequentAccess,
            DeletionProtectionEnabled = true,
        },
        ["SpecialCharacters"] = Request(OperationVectors.SpecialText) with
        {
            AttributeDefinitions = [new(OperationVectors.SpecialText, ScalarAttributeType.String)],
            GlobalSecondaryIndexUpdates =
            [
                new()
                {
                    Create = new()
                    {
                        IndexName = OperationVectors.SpecialText,
                        KeySchema = [new(OperationVectors.SpecialText, KeyType.Hash)],
                        Projection = new() { ProjectionType = ProjectionType.All },
                    },
                },
            ],
        },
        ["StreamViewNewImage"] = Request() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewImage } },
        ["StreamViewOldImage"] = Request() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.OldImage } },
        ["StreamViewNewAndOldImages"] = Request() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages } },
        ["StreamViewKeysOnly"] = Request() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.KeysOnly } },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("UpdateTable");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("UpdateTable");

    [Test]
    public async Task Name_Always_IsUpdateTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = UpdateTableOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("UpdateTable");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("UpdateTable", "OnlyTheTableName");

        // Act
        var target = WireProtocol.TargetPrefix + UpdateTableOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = Request("Orders");

        // Act
        var tableName = UpdateTableOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Orders");
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
        var vector = OperationVectors.Request("UpdateTable", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_OnlyTheTableName_SendsNothingElse()
    {
        // Arrange
        var request = Request("Orders");

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Orders"}""");
    }

    [Test]
    public async Task WriteRequest_EmptyLists_AreOmitted()
    {
        // Arrange
        var request = Request("Orders") with { AttributeDefinitions = [], GlobalSecondaryIndexUpdates = [] };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Orders"}""");
    }

    [Test]
    public async Task WriteRequest_DeletionProtectionOff_IsSent()
    {
        // Arrange
        var request = Request("Orders") with { DeletionProtectionEnabled = false };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Orders","DeletionProtectionEnabled":false}""");
    }

    [Test]
    public async Task WriteRequest_UpdateWithNoMember_WritesAnEmptyObject()
    {
        // Arrange (DynamoDB rejects the update; the library sends what is set)
        var request = Request("Orders") with { GlobalSecondaryIndexUpdates = [new()] };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Orders","GlobalSecondaryIndexUpdates":[{}]}""");
    }

    [Test]
    public async Task WriteRequest_UndefinedBillingMode_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { BillingMode = (BillingMode)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("BillingMode", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_UndefinedTableClass_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { TableClass = (TableClass)99 };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("TableClass", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_AttributeTypeNotSet_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with { AttributeDefinitions = [new("status", ScalarAttributeType.Unknown)] };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("ScalarAttributeType", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_NewIndexWithKeyTypeNotSet_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = Request() with
        {
            GlobalSecondaryIndexUpdates = [new() { Create = new() { IndexName = "ByStatus", KeySchema = [new("status", KeyType.Unknown)], Projection = new() { ProjectionType = ProjectionType.All } } }],
        };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("KeyType", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("UpdateTable", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithTableDefaults(vector.Parsed), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_NoTableDescription_ThrowsJsonException()
    {
        // Arrange
        const string json = "{}";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).ThrowsExactly<JsonException>().WithMessage("The UpdateTable response has no TableDescription.", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_OnDemandTable_ReportsZeroCapacityAndTheClass()
    {
        // Arrange
        var vector = OperationVectors.Response("UpdateTable", "OnDemandReportsZeroCapacity");

        // Act
        var table = Read(vector.Wire).TableDescription;

        // Assert
        await Assert.That((table.ProvisionedThroughput, table.TableClass)).IsEqualTo((new ProvisionedThroughputDescription { ReadCapacityUnits = 0, WriteCapacityUnits = 0 }, TableClass.StandardInfrequentAccess));
    }

    [Test]
    public async Task ReadResponse_IndexWithEmptyCapacity_ReportsZeroUnits()
    {
        // Arrange
        var vector = OperationVectors.Response("UpdateTable", "ProvisionedCapacityAndTableClass");

        // Act
        var index = Read(vector.Wire).TableDescription.GlobalSecondaryIndexes[1];

        // Assert
        await Assert.That((index.IndexStatus, index.ProvisionedThroughput)).IsEqualTo((IndexStatus.Deleting, new ProvisionedThroughputDescription()));
    }

    [Test]
    public async Task ReadResponse_NoCapacityNoClass_ReportsNullAndUnspecified()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Orders"}}""";

        // Act
        var table = Read(json).TableDescription;

        // Assert
        await Assert.That((table.ProvisionedThroughput, table.TableClass)).IsEqualTo(((ProvisionedThroughputDescription?)null, TableClass.Unspecified));
    }

    [Test]
    public async Task ReadResponse_TableClassThisLibraryDoesNotKnow_IsUnspecified()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Orders","TableClassSummary":{"TableClass":"SOMETHING_NEW"}}}""";

        // Act
        var table = Read(json).TableDescription;

        // Assert
        await Assert.That(table.TableClass).IsEqualTo(TableClass.Unspecified);
    }

    [Test]
    public async Task SetActivityTags_NoProvisionedThroughput_SetsNoTag()
    {
        // Arrange
        var request = Request() with { BillingMode = BillingMode.PayPerRequest };
        using var activity = new Activity("test");

        // Act
        UpdateTableOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task SetActivityTags_ProvisionedThroughput_SetsTheCapacityTags()
    {
        // Arrange
        var request = Request() with { ProvisionedThroughput = new() { ReadCapacityUnits = 7, WriteCapacityUnits = 3 } };
        using var activity = new Activity("test");

        // Act
        UpdateTableOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.provisioned_read_capacity=7,aws.dynamodb.provisioned_write_capacity=3");
    }

    private static GlobalSecondaryIndex Index(ProjectionType projection) => new()
    {
        IndexName = "ByStatus",
        KeySchema = [new("status", KeyType.Hash)],
        Projection = new() { ProjectionType = projection },
    };

    private static UpdateTableRequest Request(string tableName = "Orders") => new() { TableName = tableName };

    private static string Write(UpdateTableRequest request) => TestJson.Write(writer => UpdateTableOperation.WriteRequest(writer, request));

    private static UpdateTableResponse Read(string json) => TestJson.Read(json, static (ref reader) => UpdateTableOperation.ReadResponse(ref reader));
}
