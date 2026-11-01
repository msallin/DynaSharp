using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class CreateTableOperationTests
{
    private static readonly KeySchemaElement[] OrdersKey = [new("pk", KeyType.Hash), new("sk", KeyType.Range)];

    private static readonly AttributeDefinition[] OrdersAttributes =
    [
        new("pk", ScalarAttributeType.String),
        new("sk", ScalarAttributeType.String),
        new("status", ScalarAttributeType.String),
        new("createdAt", ScalarAttributeType.Number),
    ];

    private static readonly Dictionary<string, CreateTableRequest> BotocoreRequests = new()
    {
        ["Minimal"] = Request(),
        ["CreateATableFromApiReference"] = new()
        {
            TableName = "Thread",
            AttributeDefinitions =
            [
                new("ForumName", ScalarAttributeType.String),
                new("Subject", ScalarAttributeType.String),
                new("LastPostDateTime", ScalarAttributeType.String),
            ],
            KeySchema = [new("ForumName", KeyType.Hash), new("Subject", KeyType.Range)],
            LocalSecondaryIndexes =
            [
                new()
                {
                    IndexName = "LastPostIndex",
                    KeySchema = [new("ForumName", KeyType.Hash), new("LastPostDateTime", KeyType.Range)],
                    Projection = new() { ProjectionType = ProjectionType.KeysOnly },
                },
            ],
            BillingMode = BillingMode.PayPerRequest,
            Tags = [new() { Key = "Owner", Value = "BlueTeam" }],
        },
        ["AllOptions"] = new()
        {
            TableName = "Orders",
            AttributeDefinitions = OrdersAttributes,
            KeySchema = OrdersKey,
            LocalSecondaryIndexes =
            [
                new()
                {
                    IndexName = "ByCreatedAt",
                    KeySchema = [new("pk", KeyType.Hash), new("createdAt", KeyType.Range)],
                    Projection = new() { ProjectionType = ProjectionType.Include, NonKeyAttributes = ["total", "customer"] },
                },
            ],
            GlobalSecondaryIndexes =
            [
                new()
                {
                    IndexName = "ByStatus",
                    KeySchema = [new("status", KeyType.Hash), new("createdAt", KeyType.Range)],
                    Projection = new() { ProjectionType = ProjectionType.All },
                    ProvisionedThroughput = new() { ReadCapacityUnits = 3, WriteCapacityUnits = 2 },
                },
            ],
            BillingMode = BillingMode.Provisioned,
            ProvisionedThroughput = new() { ReadCapacityUnits = 10, WriteCapacityUnits = 20 },
            StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages },
            SSESpecification = new()
            {
                Enabled = true,
                SSEType = SSEType.Kms,
                KMSMasterKeyId = "arn:aws:kms:eu-central-2:123456789012:key/1234abcd-12ab-34cd-56ef-1234567890ab",
            },
            Tags = [new() { Key = "Owner", Value = "BlueTeam" }, new() { Key = "Env", Value = string.Empty }],
            TableClass = TableClass.StandardInfrequentAccess,
            DeletionProtectionEnabled = true,
        },
        ["OnDemandWithGlobalSecondaryIndexes"] = new()
        {
            TableName = "Orders",
            AttributeDefinitions = OrdersAttributes,
            KeySchema = OrdersKey,
            GlobalSecondaryIndexes =
            [
                new() { IndexName = "ByStatus", KeySchema = [new("status", KeyType.Hash)], Projection = new() { ProjectionType = ProjectionType.KeysOnly } },
                new() { IndexName = "ByCreatedAt", KeySchema = [new("createdAt", KeyType.Hash)], Projection = new() { ProjectionType = ProjectionType.All } },
            ],
            BillingMode = BillingMode.PayPerRequest,
        },
        ["KeyTypesAndLargeCapacity"] = new()
        {
            TableName = "Blobs",
            AttributeDefinitions = [new("pk", ScalarAttributeType.Binary), new("sk", ScalarAttributeType.Number)],
            KeySchema = [new("pk", KeyType.Hash), new("sk", KeyType.Range)],
            ProvisionedThroughput = new() { ReadCapacityUnits = long.MaxValue, WriteCapacityUnits = 1 },
        },
        ["StreamDisabled"] = OnDemand() with { StreamSpecification = new() { StreamEnabled = false } },
        ["SseEnabledWithoutKey"] = OnDemand() with { SSESpecification = new() { Enabled = true } },
        ["SseAes256"] = OnDemand() with { SSESpecification = new() { SSEType = SSEType.Aes256 } },
        ["TableClassStandard"] = OnDemand() with { TableClass = TableClass.Standard },
        ["SpecialCharactersInTags"] = OnDemand() with { Tags = [new() { Key = OperationVectors.SpecialText, Value = OperationVectors.SpecialText }] },
        ["StreamViewNewImage"] = OnDemand() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewImage } },
        ["StreamViewOldImage"] = OnDemand() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.OldImage } },
        ["StreamViewNewAndOldImages"] = OnDemand() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewAndOldImages } },
        ["StreamViewKeysOnly"] = OnDemand() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.KeysOnly } },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("CreateTable");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("CreateTable");

    public static IEnumerable<(BillingMode Value, string Wire)> BillingModes()
    {
        yield return (BillingMode.Provisioned, "PROVISIONED");
        yield return (BillingMode.PayPerRequest, "PAY_PER_REQUEST");
    }

    public static IEnumerable<(TableClass Value, string Wire)> TableClasses()
    {
        yield return (TableClass.Standard, "STANDARD");
        yield return (TableClass.StandardInfrequentAccess, "STANDARD_INFREQUENT_ACCESS");
    }

    public static IEnumerable<(SSEType Value, string Wire)> SseTypes()
    {
        yield return (SSEType.Aes256, "AES256");
        yield return (SSEType.Kms, "KMS");
    }

    public static IEnumerable<(StreamViewType Value, string Wire)> StreamViewTypes()
    {
        yield return (StreamViewType.NewImage, "NEW_IMAGE");
        yield return (StreamViewType.OldImage, "OLD_IMAGE");
        yield return (StreamViewType.NewAndOldImages, "NEW_AND_OLD_IMAGES");
        yield return (StreamViewType.KeysOnly, "KEYS_ONLY");
    }

    public static IEnumerable<(ProjectionType Value, string Wire)> ProjectionTypes()
    {
        yield return (ProjectionType.All, "ALL");
        yield return (ProjectionType.KeysOnly, "KEYS_ONLY");
        yield return (ProjectionType.Include, "INCLUDE");
    }

    public static IEnumerable<(IndexStatus Value, string Wire)> IndexStatuses()
    {
        yield return (IndexStatus.Creating, "CREATING");
        yield return (IndexStatus.Updating, "UPDATING");
        yield return (IndexStatus.Deleting, "DELETING");
        yield return (IndexStatus.Active, "ACTIVE");
        yield return (IndexStatus.Unknown, "BACKFILLING");
    }

    [Test]
    public async Task Name_Always_IsCreateTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = CreateTableOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("CreateTable");
    }

    [Test]
    public async Task Name_Always_MatchesTheTargetBotocoreSends()
    {
        // Arrange
        var vector = OperationVectors.Request("CreateTable", "Minimal");

        // Act
        var target = WireProtocol.TargetPrefix + CreateTableOperation.Name;

        // Assert
        await Assert.That(target).IsEqualTo(vector.Target);
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = Request();

        // Act
        var tableName = CreateTableOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Users");
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
        var vector = OperationVectors.Request("CreateTable", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_RequiredMembersOnly_WritesNothingOptional()
    {
        // Arrange
        var request = Request() with { ProvisionedThroughput = null };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"TableName":"Users","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}]}""");
    }

    [Test]
    public async Task WriteRequest_EmptyOptionalLists_OmitsThem()
    {
        // Arrange
        var request = Request() with
        {
            ProvisionedThroughput = null,
            LocalSecondaryIndexes = [],
            GlobalSecondaryIndexes = [],
            Tags = [],
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("Indexes").And.DoesNotContain("Tags");
    }

    [Test]
    public async Task WriteRequest_IncludeWithEmptyNonKeyAttributes_OmitsTheList()
    {
        // Arrange
        var request = Request() with
        {
            LocalSecondaryIndexes =
            [
                new()
                {
                    IndexName = "ByDate",
                    KeySchema = [new("pk", KeyType.Hash), new("date", KeyType.Range)],
                    Projection = new() { ProjectionType = ProjectionType.Include, NonKeyAttributes = [] },
                },
            ],
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("NonKeyAttributes");
    }

    [Test]
    public async Task WriteRequest_DeletionProtectionNotSet_OmitsIt()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("DeletionProtectionEnabled");
    }

    [Test]
    public async Task WriteRequest_ServerSideEncryptionWithoutOptions_WritesAnEmptyObject()
    {
        // Arrange
        var request = OnDemand() with { SSESpecification = new() };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains("\"SSESpecification\":{}");
    }

    [Test]
    public async Task WriteRequest_StreamEnabledWithoutViewType_WritesOnlyTheFlag()
    {
        // Arrange
        var request = OnDemand() with { StreamSpecification = new() { StreamEnabled = true } };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains("\"StreamSpecification\":{\"StreamEnabled\":true}");
    }

    [Test]
    public async Task WriteRequest_BillingModeAndTableClassNotSet_OmitsThem()
    {
        // Arrange
        var request = Request();

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).DoesNotContain("BillingMode").And.DoesNotContain("TableClass");
    }

    [Test]
    [MethodDataSource(nameof(BillingModes))]
    public async Task WriteRequest_BillingMode_WritesTheWireName((BillingMode Value, string Wire) mode)
    {
        // Arrange
        var request = Request() with { BillingMode = mode.Value };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($"\"BillingMode\":\"{mode.Wire}\"");
    }

    [Test]
    [MethodDataSource(nameof(TableClasses))]
    public async Task WriteRequest_TableClass_WritesTheWireName((TableClass Value, string Wire) tableClass)
    {
        // Arrange
        var request = Request() with { TableClass = tableClass.Value };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($"\"TableClass\":\"{tableClass.Wire}\"");
    }

    [Test]
    [MethodDataSource(nameof(SseTypes))]
    public async Task WriteRequest_SseType_WritesTheWireName((SSEType Value, string Wire) type)
    {
        // Arrange
        var request = OnDemand() with { SSESpecification = new() { SSEType = type.Value } };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($"\"SSEType\":\"{type.Wire}\"");
    }

    [Test]
    [MethodDataSource(nameof(StreamViewTypes))]
    public async Task WriteRequest_StreamViewType_WritesTheWireName((StreamViewType Value, string Wire) type)
    {
        // Arrange
        var request = OnDemand() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = type.Value } };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($"\"StreamViewType\":\"{type.Wire}\"");
    }

    [Test]
    [MethodDataSource(nameof(ProjectionTypes))]
    public async Task WriteRequest_ProjectionType_WritesTheWireName((ProjectionType Value, string Wire) type)
    {
        // Arrange
        var request = Request() with
        {
            GlobalSecondaryIndexes = [new() { IndexName = "ByStatus", KeySchema = [new("status", KeyType.Hash)], Projection = new() { ProjectionType = type.Value } }],
        };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).Contains($"\"Projection\":{{\"ProjectionType\":\"{type.Wire}\"}}");
    }

    [Test]
    [Arguments(ScalarAttributeType.Unknown)]
    [Arguments((ScalarAttributeType)99)]
    public async Task WriteRequest_AttributeTypeThatCannotBeSent_ThrowsArgumentOutOfRangeException(ScalarAttributeType type)
    {
        // Arrange
        var request = Request() with { AttributeDefinitions = [new("pk", type)] };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("ScalarAttributeType", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(KeyType.Unknown)]
    [Arguments((KeyType)99)]
    public async Task WriteRequest_KeyTypeThatCannotBeSent_ThrowsArgumentOutOfRangeException(KeyType type)
    {
        // Arrange
        var request = Request() with { KeySchema = [new("pk", type)] };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("KeyType", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(ProjectionType.Unknown)]
    [Arguments((ProjectionType)99)]
    public async Task WriteRequest_ProjectionTypeThatCannotBeSent_ThrowsArgumentOutOfRangeException(ProjectionType type)
    {
        // Arrange
        var request = Request() with
        {
            LocalSecondaryIndexes = [new() { IndexName = "ByDate", KeySchema = [new("pk", KeyType.Hash)], Projection = new() { ProjectionType = type } }],
        };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("ProjectionType", StringComparison.Ordinal);
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
    public async Task WriteRequest_UndefinedStreamViewType_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = OnDemand() with { StreamSpecification = new() { StreamEnabled = true, StreamViewType = (StreamViewType)99 } };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("StreamViewType", StringComparison.Ordinal);
    }

    [Test]
    public async Task WriteRequest_UndefinedSseType_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var request = OnDemand() with { SSESpecification = new() { SSEType = (SSEType)99 } };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("SSEType", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("CreateTable", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithTableDefaults(vector.Parsed), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheLocalSecondaryIndex()
    {
        // Arrange
        var vector = OperationVectors.Response("CreateTable", "CreateATableFromApiReference");

        // Act
        var table = Read(vector.Wire).TableDescription!;

        // Assert
        await Assert.That(table.LocalSecondaryIndexes.Single().IndexName).IsEqualTo("LastPostIndex");
    }

    [Test]
    public async Task ReadResponse_NoTableDescription_HasNone()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.TableDescription).IsNull();
    }

    [Test]
    public async Task ReadResponse_LocalSecondaryIndexWithoutName_HasNoName()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","LocalSecondaryIndexes":[{"KeySchema":[]}]}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.TableDescription!.LocalSecondaryIndexes.Single().IndexName).IsNull();
    }

    [Test]
    public async Task ReadResponse_GlobalSecondaryIndexWithoutName_HasNoName()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexStatus":"ACTIVE"}]}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.TableDescription!.GlobalSecondaryIndexes.Single().IndexName).IsNull();
    }

    [Test]
    [MethodDataSource(nameof(IndexStatuses))]
    public async Task ReadResponse_IndexStatus_MapsToTheEnum((IndexStatus Value, string Wire) status)
    {
        // Arrange
        var json = $$$"""{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexName":"ByStatus","IndexStatus":"{{{status.Wire}}}"}]}}""";

        // Act
        var index = Read(json).TableDescription!.GlobalSecondaryIndexes.Single();

        // Assert
        await Assert.That(index.IndexStatus).IsEqualTo(status.Value);
    }

    [Test]
    [MethodDataSource(nameof(ProjectionTypes))]
    public async Task ReadResponse_ProjectionType_MapsToTheEnum((ProjectionType Value, string Wire) type)
    {
        // Arrange
        var json = $$$"""{"TableDescription":{"TableName":"Users","LocalSecondaryIndexes":[{"IndexName":"ByDate","Projection":{"ProjectionType":"{{{type.Wire}}}"}}]}}""";

        // Act
        var index = Read(json).TableDescription!.LocalSecondaryIndexes.Single();

        // Assert
        await Assert.That(index.Projection?.ProjectionType).IsEqualTo(type.Value);
    }

    [Test]
    public async Task ReadResponse_UnknownProjectionType_MapsToUnknown()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","LocalSecondaryIndexes":[{"IndexName":"ByDate","Projection":{"ProjectionType":"VECTOR"}}]}}""";

        // Act
        var index = Read(json).TableDescription!.LocalSecondaryIndexes.Single();

        // Assert
        await Assert.That(index.Projection?.ProjectionType).IsEqualTo(ProjectionType.Unknown);
    }

    [Test]
    public async Task ReadResponse_NonKeyAttributes_AreReturnedInOrder()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexName":"ByStatus","Projection":{"ProjectionType":"INCLUDE","NonKeyAttributes":["b","a"],"Extra":1}}]}}""";

        // Act
        var projection = Read(json).TableDescription!.GlobalSecondaryIndexes.Single().Projection;

        // Assert
        await Assert.That(projection?.NonKeyAttributes).IsEquivalentTo(["b", "a"]);
    }

    [Test]
    public async Task ReadResponse_NullNonKeyAttribute_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexName":"ByStatus","Projection":{"NonKeyAttributes":[null]}}]}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("found null", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_NonKeyAttributesThatIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexName":"ByStatus","Projection":{"NonKeyAttributes":"a"}}]}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_BackfillingIndex_ReportsIt()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexName":"ByStatus","Backfilling":true,"Unknown":{"a":[1]}}]}}""";

        // Act
        var index = Read(json).TableDescription!.GlobalSecondaryIndexes.Single();

        // Assert
        await Assert.That(index.Backfilling).IsTrue();
    }

    [Test]
    public async Task ReadResponse_UnknownPropertiesInLocalSecondaryIndex_AreSkipped()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","LocalSecondaryIndexes":[{"IndexSizeBytes":5,"IndexName":"ByDate","Unknown":{"a":[1]}}]}}""";

        // Act
        var index = Read(json).TableDescription!.LocalSecondaryIndexes.Single();

        // Assert
        await Assert.That(index.IndexName).IsEqualTo("ByDate");
    }

    [Test]
    public async Task ReadResponse_UnknownPropertiesInProjection_AreSkipped()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","LocalSecondaryIndexes":[{"IndexName":"ByDate","Projection":{"Extra":{"a":1},"ProjectionType":"ALL"}}]}}""";

        // Act
        var index = Read(json).TableDescription!.LocalSecondaryIndexes.Single();

        // Assert
        await Assert.That(index.Projection?.ProjectionType).IsEqualTo(ProjectionType.All);
    }

    [Test]
    public async Task ReadResponse_IndexWithoutProjectionAndKeySchema_HasNoProjectionAndAnEmptyKeySchema()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","GlobalSecondaryIndexes":[{"IndexName":"ByStatus"}]}}""";

        // Act
        var index = Read(json).TableDescription!.GlobalSecondaryIndexes.Single();

        // Assert
        await Assert.That((index.Projection, index.KeySchema.Count)).IsEqualTo(((IndexProjection?)null, 0));
    }

    [Test]
    [Arguments("PROVISIONED", BillingMode.Provisioned)]
    [Arguments("PAY_PER_REQUEST", BillingMode.PayPerRequest)]
    [Arguments("SOMETHING_ELSE", BillingMode.Unspecified)]
    public async Task ReadResponse_BillingMode_MapsToTheEnum(string wire, BillingMode expected)
    {
        // Arrange
        var json = $$$$"""{"TableDescription":{"TableName":"Users","BillingModeSummary":{"BillingMode":"{{{{wire}}}}","Extra":1}}}""";

        // Act
        var summary = Read(json).TableDescription!.BillingModeSummary;

        // Assert
        await Assert.That(summary?.BillingMode).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadResponse_BillingModeSummaryWithDate_ReturnsTheDate()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","BillingModeSummary":{"LastUpdateToPayPerRequestDateTime":1.421866952062E9}}}""";

        // Act
        var summary = Read(json).TableDescription!.BillingModeSummary;

        // Assert
        await Assert.That(summary?.LastUpdateToPayPerRequestDateTime).IsEqualTo(new DateTimeOffset(2015, 1, 21, 19, 2, 32, 62, TimeSpan.Zero));
    }

    [Test]
    public async Task ReadResponse_BillingModeSummaryWithoutMembers_HasAnUnknownModeAndNoDate()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","BillingModeSummary":{}}}""";

        // Act
        var summary = Read(json).TableDescription!.BillingModeSummary;

        // Assert
        await Assert.That((summary?.BillingMode, summary?.LastUpdateToPayPerRequestDateTime)).IsEqualTo(((BillingMode?)BillingMode.Unspecified, (DateTimeOffset?)null));
    }

    [Test]
    [MethodDataSource(nameof(StreamViewTypes))]
    public async Task ReadResponse_StreamViewType_MapsToTheEnum((StreamViewType Value, string Wire) type)
    {
        // Arrange
        var json = $$$$"""{"TableDescription":{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"{{{{type.Wire}}}}","Extra":1}}}""";

        // Act
        var specification = Read(json).TableDescription!.StreamSpecification;

        // Assert
        await Assert.That((specification?.StreamEnabled, specification?.StreamViewType)).IsEqualTo(((bool?)true, (StreamViewType?)type.Value));
    }

    [Test]
    public async Task ReadResponse_UnknownStreamViewType_MapsToUnknown()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","StreamSpecification":{"StreamEnabled":false,"StreamViewType":"SOMETHING_NEW"}}}""";

        // Act
        var specification = Read(json).TableDescription!.StreamSpecification;

        // Assert
        await Assert.That(specification?.StreamViewType).IsEqualTo(StreamViewType.Unspecified);
    }

    [Test]
    public async Task ReadResponse_StreamSpecificationWithoutMembers_IsDisabledWithoutViewType()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","StreamSpecification":{}}}""";

        // Act
        var specification = Read(json).TableDescription!.StreamSpecification;

        // Assert
        await Assert.That((specification?.StreamEnabled, specification?.StreamViewType)).IsEqualTo(((bool?)false, (StreamViewType?)StreamViewType.Unspecified));
    }

    [Test]
    public async Task ReadResponse_LatestStreamAndDeletionProtection_AreReturned()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","LatestStreamArn":"arn:stream","LatestStreamLabel":"label","DeletionProtectionEnabled":true}}""";

        // Act
        var table = Read(json).TableDescription!;

        // Assert
        await Assert.That((table.LatestStreamArn, table.LatestStreamLabel, table.DeletionProtectionEnabled)).IsEqualTo(("arn:stream", "label", true));
    }

    [Test]
    public async Task ReadResponse_OnlyTheName_HasTheDefaultsOfTheNewMembers()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users"}}""";

        // Act
        var table = Read(json).TableDescription!;

        // Assert
        await Assert.That((table.BillingModeSummary, table.StreamSpecification, table.LatestStreamArn, table.LatestStreamLabel, table.DeletionProtectionEnabled, table.LocalSecondaryIndexes.Count, table.GlobalSecondaryIndexes.Count))
            .IsEqualTo(((BillingModeSummary?)null, (StreamSpecification?)null, (string?)null, (string?)null, false, 0, 0));
    }

    [Test]
    public async Task ReadResponse_Indexes_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"TableDescription":{"TableName":"Users","LocalSecondaryIndexes":[{"IndexName":"a"}],"GlobalSecondaryIndexes":[{"IndexName":"b"}]}}""";

        // Act
        var table = Read(json).TableDescription!;

        // Assert
        await Assert.That(table.LocalSecondaryIndexes is IList<LocalSecondaryIndexDescription> { IsReadOnly: true } && table.GlobalSecondaryIndexes is IList<GlobalSecondaryIndexDescription> { IsReadOnly: true }).IsTrue();
    }

    [Test]
    public async Task SetActivityTags_NoProvisionedThroughput_SetsNoTag()
    {
        // Arrange
        var request = OnDemand();
        using var activity = new Activity("test");

        // Act
        CreateTableOperation.SetActivityTags(activity, request);

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
        CreateTableOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.provisioned_read_capacity=7,aws.dynamodb.provisioned_write_capacity=3");
    }

    private static CreateTableRequest Request() => new()
    {
        TableName = "Users",
        AttributeDefinitions = [new("pk", ScalarAttributeType.String)],
        KeySchema = [new("pk", KeyType.Hash)],
        ProvisionedThroughput = new() { ReadCapacityUnits = 5, WriteCapacityUnits = 5 },
    };

    private static CreateTableRequest OnDemand() => Request() with { ProvisionedThroughput = null, BillingMode = BillingMode.PayPerRequest };

    private static string Write(CreateTableRequest request) => TestJson.Write(writer => CreateTableOperation.WriteRequest(writer, request));

    private static CreateTableResponse Read(string json) => TestJson.Read(json, static (ref reader) => CreateTableOperation.ReadResponse(ref reader));
}
