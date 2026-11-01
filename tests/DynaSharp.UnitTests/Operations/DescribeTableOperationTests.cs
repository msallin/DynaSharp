using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class DescribeTableOperationTests
{
    // The response of the DescribeTable example in the Amazon DynamoDB API Reference (Music table).
    private const string MusicTable = """
        {
          "Table": {
            "AttributeDefinitions": [
              { "AttributeName": "Artist", "AttributeType": "S" },
              { "AttributeName": "SongTitle", "AttributeType": "S" }
            ],
            "TableName": "Music",
            "KeySchema": [
              { "AttributeName": "Artist", "KeyType": "HASH" },
              { "AttributeName": "SongTitle", "KeyType": "RANGE" }
            ],
            "TableStatus": "ACTIVE",
            "CreationDateTime": 1.421866952062E9,
            "ProvisionedThroughput": { "NumberOfDecreasesToday": 0, "ReadCapacityUnits": 1, "WriteCapacityUnits": 1 },
            "TableSizeBytes": 0,
            "ItemCount": 0,
            "TableArn": "arn:aws:dynamodb:us-west-2:123456789012:table/Music",
            "TableId": "d4a0fa45-7c8e-4c5e-b3f4-2e9a8d8b3b3c"
          }
        }
        """;

    public static IEnumerable<(string Wire, TableStatus Status)> TableStatuses()
    {
        yield return ("CREATING", TableStatus.Creating);
        yield return ("UPDATING", TableStatus.Updating);
        yield return ("DELETING", TableStatus.Deleting);
        yield return ("ACTIVE", TableStatus.Active);
        yield return ("INACCESSIBLE_ENCRYPTION_CREDENTIALS", TableStatus.InaccessibleEncryptionCredentials);
        yield return ("ARCHIVING", TableStatus.Archiving);
        yield return ("ARCHIVED", TableStatus.Archived);
        yield return ("REPLICATION_NOT_AUTHORIZED", TableStatus.ReplicationNotAuthorized);
        yield return ("REPLICATING", TableStatus.Unknown);
    }

    [Test]
    public async Task Name_Always_IsDescribeTable()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = DescribeTableOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("DescribeTable");
    }

    [Test]
    public async Task GetTableName_Request_ReturnsTheTableName()
    {
        // Arrange
        var request = new DescribeTableRequest { TableName = "Music" };

        // Act
        var tableName = DescribeTableOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsEqualTo("Music");
    }

    [Test]
    public async Task WriteRequest_Table_WritesTheTableName()
    {
        // Arrange
        var request = new DescribeTableRequest { TableName = "Music" };

        // Act
        var json = TestJson.Write(writer => DescribeTableOperation.WriteRequest(writer, request));

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Music"}""");
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheTableName()
    {
        // Arrange (the sample above)

        // Act
        var table = Read(MusicTable).Table!;

        // Assert
        await Assert.That(table.TableName).IsEqualTo("Music");
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheArn()
    {
        // Arrange (the sample above)

        // Act
        var table = Read(MusicTable).Table!;

        // Assert
        await Assert.That(table.TableArn).IsEqualTo("arn:aws:dynamodb:us-west-2:123456789012:table/Music");
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheStatus()
    {
        // Arrange (the sample above)

        // Act
        var table = Read(MusicTable).Table!;

        // Assert
        await Assert.That(table.TableStatus).IsEqualTo(TableStatus.Active);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheKeySchema()
    {
        // Arrange (the sample above)

        // Act
        var table = Read(MusicTable).Table!;

        // Assert
        await Assert.That(table.KeySchema).IsEquivalentTo([new KeySchemaElement("Artist", KeyType.Hash), new KeySchemaElement("SongTitle", KeyType.Range)]);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheAttributeDefinitions()
    {
        // Arrange (the sample above)

        // Act
        var table = Read(MusicTable).Table!;

        // Assert
        await Assert.That(table.AttributeDefinitions).IsEquivalentTo([new AttributeDefinition("Artist", ScalarAttributeType.String), new AttributeDefinition("SongTitle", ScalarAttributeType.String)]);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheCreationTime()
    {
        // Arrange (the sample above)

        // Act
        var table = Read(MusicTable).Table!;

        // Assert
        await Assert.That(table.CreationDateTime).IsEqualTo(new DateTimeOffset(2015, 1, 21, 19, 2, 32, 62, TimeSpan.Zero));
    }

    [Test]
    public async Task ReadResponse_ItemCountAndSize_ReturnsThem()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","ItemCount":1234567890123,"TableSizeBytes":98765432101}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That((table.ItemCount, table.TableSizeBytes)).IsEqualTo((1_234_567_890_123L, 98_765_432_101L));
    }

    [Test]
    [MethodDataSource(nameof(TableStatuses))]
    public async Task ReadResponse_TableStatus_MapsToTheEnum((string Wire, TableStatus Status) status)
    {
        // Arrange
        var json = $$$"""{"Table":{"TableName":"Music","TableStatus":"{{{status.Wire}}}"}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.TableStatus).IsEqualTo(status.Status);
    }

    [Test]
    public async Task ReadResponse_UnknownPropertyBesideTable_IsSkipped()
    {
        // Arrange
        const string json = """{"Extra":[1,{"a":2}],"Table":{"TableName":"Music"}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.TableName).IsEqualTo("Music");
    }

    [Test]
    public async Task ReadResponse_NullTableStatus_StatusIsUnknown()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","TableStatus":null}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.TableStatus).IsEqualTo(TableStatus.Unknown);
    }

    [Test]
    public async Task ReadResponse_NoTableStatus_StatusIsUnknown()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music"}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.TableStatus).IsEqualTo(TableStatus.Unknown);
    }

    [Test]
    public async Task ReadResponse_UnknownKeyType_MapsToUnknown()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","KeySchema":[{"AttributeName":"a","KeyType":"SOMETHING"}]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.KeySchema).IsEquivalentTo([new KeySchemaElement("a", KeyType.Unknown)]);
    }

    [Test]
    [Arguments("S", ScalarAttributeType.String)]
    [Arguments("N", ScalarAttributeType.Number)]
    [Arguments("B", ScalarAttributeType.Binary)]
    [Arguments("BOOL", ScalarAttributeType.Unknown)]
    public async Task ReadResponse_AttributeType_MapsToTheEnum(string wire, ScalarAttributeType expected)
    {
        // Arrange
        var json = $$$"""{"Table":{"TableName":"Music","AttributeDefinitions":[{"AttributeName":"a","AttributeType":"{{{wire}}}"}]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.AttributeDefinitions.Single().AttributeType).IsEqualTo(expected);
    }

    [Test]
    public async Task ReadResponse_UnknownPropertiesInElements_AreSkipped()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","KeySchema":[{"Extra":[1,2],"AttributeName":"a","KeyType":"HASH"}],"AttributeDefinitions":[{"Extra":{"x":1},"AttributeName":"a","AttributeType":"S"}]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That((table.KeySchema.Count, table.AttributeDefinitions.Count)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task ReadResponse_EmptyArrays_ReturnsEmptyLists()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","KeySchema":[],"AttributeDefinitions":[]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.KeySchema.Count + table.AttributeDefinitions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReadResponse_NoTable_HasNone()
    {
        // Arrange
        const string json = "{}";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Table).IsNull();
    }

    [Test]
    public async Task ReadResponse_NullAttributeNameInTheKeySchema_ThrowsJsonException()
    {
        // Arrange (the service model requires this member, as it does not the name of a table)
        const string json = """{"Table":{"KeySchema":[{"AttributeName":null}]}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<System.Text.Json.JsonException>().WithMessageContaining("found null", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_NullTableName_HasNoName()
    {
        // Arrange
        const string json = """{"Table":{"TableName":null}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.Table!.TableName).IsNull();
    }

    [Test]
    public async Task ReadResponse_KeySchemaThatIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","KeySchema":5}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_KeySchemaElementThatIsNotAnObject_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","KeySchema":[5]}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("the start of an item", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_KeySchemaElementWithoutAttributeName_HasAnEmptyName()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","KeySchema":[{"KeyType":"HASH"}]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.KeySchema).IsEquivalentTo([new KeySchemaElement(string.Empty, KeyType.Hash)]);
    }

    [Test]
    public async Task ReadResponse_AttributeDefinitionWithoutAttributeName_HasAnEmptyName()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","AttributeDefinitions":[{"AttributeType":"S"}]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.AttributeDefinitions).IsEquivalentTo([new AttributeDefinition(string.Empty, ScalarAttributeType.String)]);
    }

    [Test]
    public async Task ReadResponse_LocalSecondaryIndexWithoutKeySchema_HasAnEmptyKeySchema()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","LocalSecondaryIndexes":[{"IndexName":"byTitle"}]}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.LocalSecondaryIndexes.Single().KeySchema).IsEmpty();
    }

    [Test]
    [Arguments("-1E300")]
    [Arguments("1E300")]
    public async Task ReadResponse_CreationTimeOutsideTheDateTimeRange_ThrowsJsonException(string seconds)
    {
        // Arrange
        var json = $$$"""{"Table":{"TableName":"Music","CreationDateTime":{{{seconds}}}}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("outside the range", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_CreationTimeAtTheEpoch_ReturnsTheEpoch()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Music","CreationDateTime":0}}""";

        // Act
        var table = Read(json).Table!;

        // Assert
        await Assert.That(table.CreationDateTime).IsEqualTo(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public async Task ReadResponse_TableWithoutName_HasNoName()
    {
        // Arrange
        const string json = """{"Table":{"TableStatus":"ACTIVE"}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.Table!.TableName, response.Table.TableStatus)).IsEqualTo(((string?)null, TableStatus.Active));
    }

    [Test]
    public async Task ReadResponse_KeySchema_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Users","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}]}}""";

        // Act
        var keySchema = Read(json).Table!.KeySchema;

        // Assert
        await Assert.That(keySchema is IList<KeySchemaElement> { IsReadOnly: true }).IsTrue();
    }

    [Test]
    public async Task ReadResponse_AttributeDefinitions_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"Table":{"TableName":"Users","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}]}}""";

        // Act
        var definitions = Read(json).Table!.AttributeDefinitions;

        // Assert
        await Assert.That(definitions is IList<AttributeDefinition> { IsReadOnly: true }).IsTrue();
    }

    private static DescribeTableResponse Read(string json) => TestJson.Read(json, static (ref reader) => DescribeTableOperation.ReadResponse(ref reader));
}
