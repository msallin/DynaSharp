using System.Net;
using DynaSharp.Requests;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// The bodies follow the examples of the API reference for CreateTable, DescribeTable, UpdateTable, DeleteTable and ListTables, and the messages are those DynamoDB sends,
// as far as the Developer Guide and the errors the repository's emulator tests quote show them.
public sealed class TableOperationsTests
{
    private const string Invalid = "One or more parameter values were invalid: ";

    private const string Thread = """
        {"TableName":"Thread",
         "AttributeDefinitions":[{"AttributeName":"ForumName","AttributeType":"S"},{"AttributeName":"Subject","AttributeType":"S"},{"AttributeName":"LastPostDateTime","AttributeType":"S"},
                                 {"AttributeName":"Author","AttributeType":"S"}],
         "KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"},{"AttributeName":"Subject","KeyType":"RANGE"}],
         "LocalSecondaryIndexes":[{"IndexName":"LastPostIndex","KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"},{"AttributeName":"LastPostDateTime","KeyType":"RANGE"}],
                                   "Projection":{"ProjectionType":"KEYS_ONLY"}}],
         "GlobalSecondaryIndexes":[{"IndexName":"ByAuthor","KeySchema":[{"AttributeName":"Author","KeyType":"HASH"}],"Projection":{"ProjectionType":"INCLUDE","NonKeyAttributes":["Subject"]},
                                    "ProvisionedThroughput":{"ReadCapacityUnits":2,"WriteCapacityUnits":3}}],
         "ProvisionedThroughput":{"ReadCapacityUnits":5,"WriteCapacityUnits":5},
         "TableClass":"STANDARD","DeletionProtectionEnabled":false}
        """;

    private const string ThreadDescription = """
        {"AttributeDefinitions":[{"AttributeName":"ForumName","AttributeType":"S"},{"AttributeName":"Subject","AttributeType":"S"},{"AttributeName":"LastPostDateTime","AttributeType":"S"},
                                 {"AttributeName":"Author","AttributeType":"S"}],
         "TableName":"Thread",
         "KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"},{"AttributeName":"Subject","KeyType":"RANGE"}],
         "TableStatus":"ACTIVE","CreationDateTime":1767225600,
         "ProvisionedThroughput":{"NumberOfDecreasesToday":0,"ReadCapacityUnits":5,"WriteCapacityUnits":5},
         "TableSizeBytes":0,"ItemCount":0,"TableArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Thread",
         "BillingModeSummary":{"BillingMode":"PROVISIONED"},
         "LocalSecondaryIndexes":[{"IndexName":"LastPostIndex","KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"},{"AttributeName":"LastPostDateTime","KeyType":"RANGE"}],
                                   "Projection":{"ProjectionType":"KEYS_ONLY"},"IndexSizeBytes":0,"ItemCount":0,
                                   "IndexArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Thread/index/LastPostIndex"}],
         "GlobalSecondaryIndexes":[{"IndexName":"ByAuthor","KeySchema":[{"AttributeName":"Author","KeyType":"HASH"}],"Projection":{"ProjectionType":"INCLUDE","NonKeyAttributes":["Subject"]},
                                    "IndexStatus":"ACTIVE","ProvisionedThroughput":{"NumberOfDecreasesToday":0,"ReadCapacityUnits":2,"WriteCapacityUnits":3},
                                    "IndexSizeBytes":0,"ItemCount":0,"IndexArn":"arn:aws:dynamodb:us-east-1:123456789012:table/Thread/index/ByAuthor"}],
         "TableClassSummary":{"TableClass":"STANDARD"},
         "DeletionProtectionEnabled":false}
        """;

    [Test]
    public async Task HandleCreateTable_TableWithIndexes_AnswersItActive()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", Thread);

        // Assert
        await Assert.That(FakeCalls.SameJson($$"""{"TableDescription":{{ThreadDescription}}}""", Wrap("TableDescription", body))).IsTrue();
    }

    [Test]
    public async Task HandleDescribeTable_TableWithItems_CountsThemAndTheirSize()
    {
        // Arrange: ForumName "a" (9 + 1) + Subject "b" (7 + 1) + Author "c" (6 + 1) = 25 bytes; the global index holds the keys and Subject, as many; the local one has no LastPostDateTime.
        var fake = FakeCalls.With(Thread);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Thread","Item":{"ForumName":{"S":"a"},"Subject":{"S":"b"},"Author":{"S":"c"}}}""");

        // Act
        var table = FakeCalls.Ok(fake, "DescribeTable", """{"TableName":"Thread"}""").GetProperty("Table");

        // Assert
        var global = table.GetProperty("GlobalSecondaryIndexes")[0];
        var local = table.GetProperty("LocalSecondaryIndexes")[0];
        await Assert.That((table.GetProperty("ItemCount").GetInt64(), table.GetProperty("TableSizeBytes").GetInt64(), global.GetProperty("ItemCount").GetInt64(),
            global.GetProperty("IndexSizeBytes").GetInt64(), local.GetProperty("ItemCount").GetInt64())).IsEqualTo((1L, 25L, 1L, 25L, 0L));
    }

    [Test]
    public async Task HandleCreateTable_NameTaken_AnswersResourceInUse()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", FakeCalls.Users);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceInUseException", "Table already exists: Users"));
    }

    [Test]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}]}""", Invalid + "ReadCapacityUnits and WriteCapacityUnits must both be specified when BillingMode is PROVISIONED")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}""", Invalid + "Neither ReadCapacityUnits nor WriteCapacityUnits can be specified when BillingMode is PAY_PER_REQUEST")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"ProvisionedThroughput":{"ReadCapacityUnits":0,"WriteCapacityUnits":1}}""", "1 validation error detected: Value '0' at 'provisionedThroughput.readCapacityUnits' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":0}}""", "1 validation error detected: Value '0' at 'provisionedThroughput.writeCapacityUnits' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"pk","AttributeType":"N"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}""", "Cannot have two attributes with the same name")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"x","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}""", Invalid + "Number of attributes in KeySchema does not exactly match number of attributes defined in AttributeDefinitions")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"x","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}""", Invalid + "Some index key attributes are not defined in AttributeDefinitions. Keys: [pk], AttributeDefinitions: [x]")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[],"BillingMode":"PAY_PER_REQUEST"}""", "1 validation error detected: Value at 'keySchema' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"a","KeyType":"RANGE"},{"AttributeName":"b","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST"}""", "1 validation error detected: Value '[KeySchemaElement(attributeName=pk, keyType=HASH), KeySchemaElement(attributeName=a, keyType=RANGE), KeySchemaElement(attributeName=b, keyType=RANGE)]' at 'keySchema' failed to satisfy constraint: Member must have length less than or equal to 2")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST"}""", "Invalid KeySchema: The first KeySchemaElement is not a HASH key type")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"},{"AttributeName":"b","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"},{"AttributeName":"b","KeyType":"RANGE"},{"AttributeName":"pk","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}}]}""", "1 validation error detected: Value at 'keySchema' failed to satisfy constraint: Member must have length less than or equal to 2")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}""", "Invalid KeySchema: The second KeySchemaElement is not a RANGE key type")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"pk","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST"}""", "Invalid KeySchema: Some index key attribute have no definition")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","LocalSecondaryIndexes":[]}""", Invalid + "List of LocalSecondaryIndexes is empty")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","LocalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"a","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}}]}""", Invalid + "Table KeySchema does not have a range key, which is required when specifying a LocalSecondaryIndex")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST","LocalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}}]}""", Invalid + "Index KeySchema does not have the same leading hash key as table KeySchema for index: ByA. index hash key: a, table hash key: pk")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST","LocalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}]}""", Invalid + "Index KeySchema does not have the same leading hash key as table KeySchema for index: ByA. index hash key: pk, table hash key: pk")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}},{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}]}""", Invalid + "Duplicate index name: ByA")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"B","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}]}""", "1 validation error detected: Value 'B' at 'globalSecondaryIndexes.1.member.indexName' failed to satisfy constraint: Member must have length greater than or equal to 3")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"INCLUDE"}}]}""", Invalid + "ProjectionType is INCLUDE, but NonKeyAttributes is not specified")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL","NonKeyAttributes":["x"]}}]}""", Invalid + "ProjectionType is ALL, but NonKeyAttributes is specified")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{}}]}""", Invalid + "The ProjectionType of an index must be ALL, KEYS_ONLY or INCLUDE")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST","GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"},"ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}]}""", Invalid + "ProvisionedThroughput should not be specified for index: ByA when BillingMode is PAY_PER_REQUEST")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1},"GlobalSecondaryIndexes":[{"IndexName":"ByA","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}]}""", Invalid + "ProvisionedThroughput must be specified for index: ByA")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"BillingMode":"PAY_PER_REQUEST"}""", "1 validation error detected: Value null at 'keySchema' failed to satisfy constraint: Member must not be null")]
    [Arguments("""{"TableName":"T a","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}""", "1 validation error detected: Value 'T a' at 'tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    public async Task HandleCreateTable_RequestDynamoDbRejects_AnswersValidation(string request, string message)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments(6, 0, Invalid + "Number of LocalSecondaryIndexes exceeds per-table limit of 5")]
    [Arguments(0, 21, Invalid + "Number of GlobalSecondaryIndexes exceeds per-table limit of 20")]
    public async Task HandleCreateTable_TooManyIndexes_AnswersValidation(int locals, int globals, string message)
    {
        // Arrange
        var fake = FakeCalls.With();
        var localIndexes = string.Join(",", Enumerable.Range(0, locals).Select(i => $$$"""{"IndexName":"Local{{{i}}}","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"a","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"}}"""));
        var globalIndexes = string.Join(",", Enumerable.Range(0, globals).Select(i => $$$"""{"IndexName":"Global{{{i}}}","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}"""));
        var request = $$"""
            {"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"S"},{"AttributeName":"a","AttributeType":"S"}],
             "KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST"
             {{(locals > 0 ? $",\"LocalSecondaryIndexes\":[{localIndexes}]" : string.Empty)}}{{(globals > 0 ? $",\"GlobalSecondaryIndexes\":[{globalIndexes}]" : string.Empty)}}}
            """;

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ValidationException", message));
    }

    [Test]
    [Arguments(""","Tags":[{"Key":"team","Value":"shop"}]""", "the member Tags in CreateTable")]
    [Arguments(""","SSESpecification":{"Enabled":true,"Unknown":1}""", "the member Unknown in SSESpecification of CreateTable")]
    [Arguments(""","OnDemandThroughput":{"MaxReadRequestUnits":10,"Unknown":1}""", "the member Unknown in OnDemandThroughput of CreateTable")]

    [Arguments(""","StreamSpecification":{"StreamEnabled":false,"Shards":1}""", "the member Shards in StreamSpecification of CreateTable")]
    public async Task HandleCreateTable_MemberTheFakeDoesNotImplement_AnswersUnsupported(string member, string named)
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + member + "}";

        // Act
        var (status, code, message) = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That((status, code, message.Contains(named, StringComparison.Ordinal), fake.TableNames.Count)).IsEqualTo((HttpStatusCode.BadRequest, "UnsupportedByFakeException", true, 0));
    }

    [Test]
    public async Task HandleCreateTable_StreamDisabledAndProtection_CreatesTheTableWithProtection()
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","StreamSpecification":{"StreamEnabled":false},"DeletionProtectionEnabled":true}""";

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", request);

        // Assert
        await Assert.That(body.GetProperty("TableDescription").GetProperty("DeletionProtectionEnabled").GetBoolean()).IsTrue();
    }

    [Test]
    [Arguments("""{"Enabled":true}""", "arn:aws:kms:us-east-1:123456789012:key/8f2b1c4e-6d3a-4e5f-9a7b-0c1d2e3f4a5b")]
    [Arguments("""{"Enabled":true,"SSEType":"KMS"}""", "arn:aws:kms:us-east-1:123456789012:key/8f2b1c4e-6d3a-4e5f-9a7b-0c1d2e3f4a5b")]
    [Arguments("""{"Enabled":true,"SSEType":"AES256"}""", "arn:aws:kms:us-east-1:123456789012:key/8f2b1c4e-6d3a-4e5f-9a7b-0c1d2e3f4a5b")]
    [Arguments("""{"Enabled":true,"KMSMasterKeyId":"1234abcd-12ab-34cd-56ef-1234567890ab"}""", "arn:aws:kms:us-east-1:123456789012:key/1234abcd-12ab-34cd-56ef-1234567890ab")]
    [Arguments("""{"Enabled":true,"KMSMasterKeyId":"alias/orders"}""", "arn:aws:kms:us-east-1:123456789012:alias/orders")]
    [Arguments("""{"Enabled":true,"KMSMasterKeyId":"arn:aws:kms:us-west-2:111122223333:key/1234abcd"}""", "arn:aws:kms:us-west-2:111122223333:key/1234abcd")]
    public async Task HandleCreateTable_SseEnabled_DescribesTheKmsKey(string sse, string keyArn)
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","SSESpecification":""" + sse + "}";

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", request);

        // Assert
        await Assert.That(body.GetProperty("TableDescription").GetProperty("SSEDescription").GetRawText()).IsEqualTo($$"""{"Status":"ENABLED","SSEType":"KMS","KMSMasterKeyArn":"{{keyArn}}"}""");
    }

    [Test]
    [Arguments("""{"Enabled":false}""")]
    [Arguments("""{}""")]
    [Arguments("""{"Enabled":false,"SSEType":"KMS","KMSMasterKeyId":"alias/orders"}""")]
    public async Task HandleCreateTable_SseNotEnabled_DescribesNoEncryption(string sse)
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","SSESpecification":""" + sse + "}";

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", request);

        // Assert
        await Assert.That(body.GetProperty("TableDescription").TryGetProperty("SSEDescription", out _)).IsFalse();
    }

    [Test]
    [Arguments("DescribeTable", "Table")]
    [Arguments("DeleteTable", "TableDescription")]
    public async Task HandleDescribeAndDeleteTable_SseEnabled_DescribeTheKmsKey(string operation, string member)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users.TrimEnd()[..^1] + ""","SSESpecification":{"Enabled":true}}""");

        // Act
        var body = FakeCalls.Ok(fake, operation, """{"TableName":"Users"}""");

        // Assert
        await Assert.That(body.GetProperty(member).GetProperty("SSEDescription").GetProperty("Status").GetString()).IsEqualTo("ENABLED");
    }

    [Test]
    public async Task HandleUpdateTable_SseEnabled_KeepsTheEncryption()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users.TrimEnd()[..^1] + ""","SSESpecification":{"Enabled":true}}""");

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","DeletionProtectionEnabled":true}""");

        // Assert
        await Assert.That(body.GetProperty("TableDescription").GetProperty("SSEDescription").GetProperty("KMSMasterKeyArn").GetString()).IsEqualTo(FakeTable.AwsManagedKeyArn);
    }

    [Test]
    public async Task HandleDescribeTable_NoTable_AnswersResourceNotFoundNamingIt()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "DescribeTable", """{"TableName":"Users"}""");

        // Assert
        await Assert.That(answer).IsEqualTo((HttpStatusCode.BadRequest, "ResourceNotFoundException", "Requested resource not found: Table: Users not found"));
    }

    [Test]
    public async Task HandleDeleteTable_Table_AnswersItDeletingAndFreesTheName()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var body = FakeCalls.Ok(fake, "DeleteTable", """{"TableName":"Users"}""");

        // Assert
        await Assert.That((body.GetProperty("TableDescription").GetProperty("TableStatus").GetString(), fake.TableNames.Count)).IsEqualTo(("DELETING", 0));
    }

    [Test]
    public async Task HandleDeleteTable_Protected_AnswersValidationAndKeepsIt()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users.TrimEnd()[..^1] + ""","DeletionProtectionEnabled":true}""");

        // Act
        var answer = FakeCalls.Error(fake, "DeleteTable", """{"TableName":"Users"}""");

        // Assert
        await Assert.That((answer.Message, fake.TableNames.Count)).IsEqualTo(("Resource cannot be deleted as it is currently protected against deletion. Disable deletion protection first.", 1));
    }

    [Test]
    [Arguments("{}", """{"TableNames":["Alpha","Beta","Gamma"]}""")]
    [Arguments("""{"Limit":2}""", """{"TableNames":["Alpha","Beta"],"LastEvaluatedTableName":"Beta"}""")]
    [Arguments("""{"Limit":2,"ExclusiveStartTableName":"Beta"}""", """{"TableNames":["Gamma"]}""")]
    [Arguments("""{"Limit":3}""", """{"TableNames":["Alpha","Beta","Gamma"]}""")]
    [Arguments("""{"ExclusiveStartTableName":"Gamma"}""", """{"TableNames":[]}""")]
    public async Task HandleListTables_ThreeTables_PagesThemInOrdinalOrder(string request, string expected)
    {
        // Arrange
        var fake = FakeCalls.With(Named("Gamma"), Named("Alpha"), Named("Beta"));

        // Act
        var body = FakeCalls.Ok(fake, "ListTables", request);

        // Assert
        await Assert.That(FakeCalls.SameJson(expected, body)).IsTrue();
    }

    [Test]
    [Arguments(0, "greater than or equal to 1")]
    [Arguments(101, "less than or equal to 100")]
    public async Task HandleListTables_LimitOutOfRange_AnswersValidation(int limit, string constraint)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "ListTables", $$"""{"Limit":{{limit}}}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo($"1 validation error detected: Value '{limit}' at 'limit' failed to satisfy constraint: Member must have value {constraint}");
    }

    [Test]
    public async Task HandleUpdateTable_CreateIndex_BackfillsItAtOnce()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u1"},"email":{"S":"a@example.com"}}}""");
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u2"}}}""");
        const string Request = """
            {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"email","AttributeType":"S"}],
             "GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByEmail","KeySchema":[{"AttributeName":"email","KeyType":"HASH"}],"Projection":{"ProjectionType":"KEYS_ONLY"}}}]}
            """;

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", Request);

        // Assert
        var table = body.GetProperty("TableDescription");
        var index = table.GetProperty("GlobalSecondaryIndexes")[0];
        await Assert.That((index.GetProperty("IndexName").GetString(), index.GetProperty("IndexStatus").GetString(), index.GetProperty("ItemCount").GetInt64(), table.GetProperty("AttributeDefinitions").GetArrayLength()))
            .IsEqualTo(("ByEmail", "ACTIVE", 1L, 2));
    }

    [Test]
    public async Task HandleUpdateTable_CreateIndexOnTheTableKeyDeclaredInTheRequest_BackfillsIt()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"u1"},"g3":{"S":"x"}}}""");
        const string Request = """
            {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"g3","AttributeType":"S"}],
             "GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByPkAndG3","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"g3","KeyType":"RANGE"}],"Projection":{"ProjectionType":"KEYS_ONLY"}}}]}
            """;

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", Request);

        // Assert
        var index = body.GetProperty("TableDescription").GetProperty("GlobalSecondaryIndexes")[0];
        await Assert.That((index.GetProperty("IndexName").GetString(), index.GetProperty("ItemCount").GetInt64())).IsEqualTo(("ByPkAndG3", 1L));
    }

    [Test]
    public async Task HandleUpdateTable_DeleteIndex_DropsItAndItsDefinition()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Delete":{"IndexName":"ByAuthor"}}]}""");

        // Assert
        var table = body.GetProperty("TableDescription");
        await Assert.That((table.TryGetProperty("GlobalSecondaryIndexes", out _), table.GetProperty("AttributeDefinitions").GetArrayLength())).IsEqualTo((false, 3));
    }

    [Test]
    public async Task HandleUpdateTable_ThroughputOfTableAndIndex_ReportsTheNewNumbers()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        const string Request = """
            {"TableName":"Thread","ProvisionedThroughput":{"ReadCapacityUnits":10,"WriteCapacityUnits":20},
             "GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"ByAuthor","ProvisionedThroughput":{"ReadCapacityUnits":7,"WriteCapacityUnits":8}}}]}
            """;

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", Request).GetProperty("TableDescription");

        // Assert
        var tableThroughput = table.GetProperty("ProvisionedThroughput");
        var indexThroughput = table.GetProperty("GlobalSecondaryIndexes")[0].GetProperty("ProvisionedThroughput");
        await Assert.That((tableThroughput.GetProperty("ReadCapacityUnits").GetInt64(), tableThroughput.GetProperty("WriteCapacityUnits").GetInt64(),
            indexThroughput.GetProperty("ReadCapacityUnits").GetInt64(), indexThroughput.GetProperty("WriteCapacityUnits").GetInt64())).IsEqualTo((10L, 20L, 7L, 8L));
    }

    [Test]
    public async Task HandleUpdateTable_ToOnDemand_ZeroesTableAndIndexes()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Thread","BillingMode":"PAY_PER_REQUEST"}""").GetProperty("TableDescription");

        // Assert
        await Assert.That((table.GetProperty("BillingModeSummary").GetProperty("BillingMode").GetString(), table.GetProperty("ProvisionedThroughput").GetProperty("ReadCapacityUnits").GetInt64(),
            table.GetProperty("GlobalSecondaryIndexes")[0].GetProperty("ProvisionedThroughput").GetProperty("WriteCapacityUnits").GetInt64())).IsEqualTo(("PAY_PER_REQUEST", 0L, 0L));
    }

    [Test]
    public async Task HandleUpdateTable_ToProvisionedWithEveryIndex_SetsTheNumbers()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "UpdateTable", """
            {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"email","AttributeType":"S"}],
             "GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByEmail","KeySchema":[{"AttributeName":"email","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}}]}
            """);
        const string Request = """
            {"TableName":"Users","BillingMode":"PROVISIONED","ProvisionedThroughput":{"ReadCapacityUnits":4,"WriteCapacityUnits":4},
             "GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"ByEmail","ProvisionedThroughput":{"ReadCapacityUnits":2,"WriteCapacityUnits":2}}}]}
            """;

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", Request).GetProperty("TableDescription");

        // Assert
        await Assert.That((table.GetProperty("BillingModeSummary").GetProperty("BillingMode").GetString(),
            table.GetProperty("GlobalSecondaryIndexes")[0].GetProperty("ProvisionedThroughput").GetProperty("ReadCapacityUnits").GetInt64())).IsEqualTo(("PROVISIONED", 2L));
    }

    [Test]
    public async Task HandleUpdateTable_ClassAndProtection_ReportsThem()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","TableClass":"STANDARD_INFREQUENT_ACCESS","DeletionProtectionEnabled":true}""").GetProperty("TableDescription");

        // Assert
        await Assert.That((table.GetProperty("TableClassSummary").GetProperty("TableClass").GetString(), table.GetProperty("DeletionProtectionEnabled").GetBoolean()))
            .IsEqualTo(("STANDARD_INFREQUENT_ACCESS", true));
    }

    [Test]
    public async Task HandleUpdateTable_ProtectionOnly_KeepsTheClass()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Thread","DeletionProtectionEnabled":false}""").GetProperty("TableDescription");

        // Assert
        await Assert.That(table.GetProperty("TableClassSummary").GetProperty("TableClass").GetString()).IsEqualTo("STANDARD");
    }

    [Test]
    [Arguments("""{"TableName":"Thread"}""", "ValidationException", "At least one of ProvisionedThroughput, BillingMode, UpdateStreamEnabled, GlobalSecondaryIndexUpdates or SSESpecification or ReplicaUpdates is required")]
    [Arguments("""{"TableName":"Thread","ProvisionedThroughput":{"ReadCapacityUnits":5,"WriteCapacityUnits":5}}""", "ValidationException", "The provisioned throughput for the table will not change. The requested value equals the current value. Current ReadCapacityUnits provisioned for the table: 5. Requested ReadCapacityUnits: 5. " + "Current WriteCapacityUnits provisioned for the table: 5. Requested WriteCapacityUnits: 5. Refer to the Amazon DynamoDB Developer Guide for current limits and how to request higher limits.")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Delete":{"IndexName":"ByAuthor"}},{"Delete":{"IndexName":"Other"}}]}""", "LimitExceededException", "Subscriber limit exceeded: Only 1 online index can be created or deleted simultaneously per table")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Delete":{"IndexName":"Missing"}}]}""", "ResourceNotFoundException", "Requested resource not found: Index: Missing not found")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"Missing","ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}}]}""", "ResourceNotFoundException", "Requested resource not found: Index: Missing not found")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByAuthor","KeySchema":[{"AttributeName":"Author","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"},"ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}}]}""", "ValidationException", Invalid + "Attempting to create an index which already exists: ByAuthor")]
    [Arguments("""{"TableName":"Thread","AttributeDefinitions":[{"AttributeName":"Likes","AttributeType":"N"}],"GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByLikes","KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"},{"AttributeName":"Likes","KeyType":"RANGE"}],"Projection":{"ProjectionType":"ALL"},"ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}}]}""", "ValidationException", Invalid + "Some index key attributes are not defined in AttributeDefinitions. Keys: [ForumName, Likes], AttributeDefinitions: [Likes]")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByLikes","KeySchema":[{"AttributeName":"ForumName","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"},"ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}}]}""", "ValidationException", Invalid + "Some index key attributes are not defined in AttributeDefinitions. Keys: [ForumName], AttributeDefinitions: []")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{}]}""", "ValidationException", Invalid + "A GlobalSecondaryIndexUpdate must have one of Create, Update or Delete")]
    [Arguments("""{"TableName":"Thread","BillingMode":"PROVISIONED","ProvisionedThroughput":{"ReadCapacityUnits":5,"WriteCapacityUnits":6}}""", "", "")]
    [Arguments("""{"TableName":"Missing","BillingMode":"PAY_PER_REQUEST"}""", "ResourceNotFoundException", "Requested resource not found: Table: Missing not found")]
    public async Task HandleUpdateTable_Request_AnswersAsDynamoDb(string request, string code, string message)
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var (status, body) = FakeCalls.Send(fake, "UpdateTable", request);

        // Assert
        var answer = status == HttpStatusCode.OK ? (string.Empty, string.Empty) : (body.GetProperty("__type").GetString()!.Split('#')[1], body.GetProperty("message").GetString()!);
        await Assert.That(answer).IsEqualTo((code, message));
    }

    [Test]
    public async Task HandleUpdateTable_ToProvisionedWithoutTheIndex_AnswersValidationAndChangesNothing()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "UpdateTable", """
            {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"email","AttributeType":"S"}],
             "GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByEmail","KeySchema":[{"AttributeName":"email","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}}]}
            """);

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTable", """{"TableName":"Users","BillingMode":"PROVISIONED","ProvisionedThroughput":{"ReadCapacityUnits":4,"WriteCapacityUnits":4}}""");

        // Assert
        await Assert.That((answer.Message, fake.DescribeTable("Users").BillingModeSummary!.BillingMode)).IsEqualTo((Invalid + "ProvisionedThroughput must be specified for index: ByEmail", BillingMode.PayPerRequest));
    }

    [Test]
    [Arguments("""{"TableName":"Thread","SSESpecification":{"Enabled":true,"Unknown":1}}""", "the member Unknown in SSESpecification of UpdateTable")]
    [Arguments("""{"TableName":"Thread","ReplicaUpdates":[{"Delete":{"RegionName":"eu-west-1"}}]}""", "the member ReplicaUpdates in UpdateTable")]
    [Arguments("""{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"ByAuthor","WarmThroughput":{}}}]}""", "the member WarmThroughput in Update of UpdateTable")]
    public async Task HandleUpdateTable_MemberTheFakeDoesNotImplement_AnswersUnsupported(string request, string named)
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var (_, code, message) = FakeCalls.Error(fake, "UpdateTable", request);

        // Assert
        await Assert.That((code, message.Contains(named, StringComparison.Ordinal))).IsEqualTo(("UnsupportedByFakeException", true));
    }

    [Test]
    public async Task HandleUpdateTable_TwoActionsInOneUpdate_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTable", """{"TableName":"Thread","GlobalSecondaryIndexUpdates":[{"Delete":{"IndexName":"ByAuthor"},"Update":{"IndexName":"ByAuthor","ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}}]}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo(Invalid + "A GlobalSecondaryIndexUpdate must have one of Create, Update or Delete");
    }

    [Test]
    public async Task Update_IndexUpdateWithoutAnAction_ThrowsValidation()
    {
        // Arrange: the record allows what the wire reader rejects before.
        var db = new FakeDatabase(TimeProvider.System);
        TableOperations.Create(db, UsersRequest());
        var request = new UpdateTableRequest { TableName = "Users", GlobalSecondaryIndexUpdates = [new GlobalSecondaryIndexUpdate()] };

        // Act
        var act = () => TableOperations.Update(db, request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Invalid + "A GlobalSecondaryIndexUpdate must have one of Create, Update or Delete", StringComparison.Ordinal);
    }

    [Test]
    public async Task Create_RecordWithEmptyIndexListsAndUnknownType_ThrowsForTheType()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        var request = UsersRequest() with { AttributeDefinitions = [new AttributeDefinition("pk", ScalarAttributeType.Unknown)], LocalSecondaryIndexes = [], GlobalSecondaryIndexes = [] };

        // Act
        var act = () => TableOperations.Create(db, request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(Invalid + "The AttributeType of pk must be S, N or B", StringComparison.Ordinal);
    }

    [Test]
    public async Task Create_RecordWithEmptyTags_CreatesTheTable()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        var request = UsersRequest() with { Tags = [] };

        // Act
        TableOperations.Create(db, request);

        // Assert
        await Assert.That(db.Tables.ContainsKey("Users")).IsTrue();
    }

    [Test]
    [Arguments(2, "greater than or equal to 3")]
    [Arguments(256, "less than or equal to 255")]
    public async Task ThrowIfInvalidName_WrongLength_ThrowsValidation(int length, string constraint)
    {
        // Arrange
        var name = new string('a', length);

        // Act
        var act = () => TableOperations.ThrowIfInvalidName(name, "tableName");

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage($"1 validation error detected: Value '{name}' at 'tableName' failed to satisfy constraint: Member must have length {constraint}", StringComparison.Ordinal);
    }

    [Test]
    public async Task Create_ThroughTheClient_GivesTheDescriptionTheClientReads()
    {
        // Arrange
        var fake = FakeCalls.With();
        using var client = fake.CreateClient();

        // Act
        var response = await client.CreateTableAsync(UsersRequest());

        // Assert
        await Assert.That((response.TableDescription!.TableStatus, response.TableDescription.KeySchema[0].AttributeName)).IsEqualTo((TableStatus.Active, "pk"));
    }

    [Test]
    public async Task ThrowIfInvalidName_AllowedCharacters_Passes()
    {
        // Arrange
        const string Name = "Shop_2026-11.v2";

        // Act
        var act = () => TableOperations.ThrowIfInvalidName(Name, "tableName");

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Create_RecordWithEncryption_KeepsTheAwsManagedKey()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        var request = UsersRequest() with { SSESpecification = new SSESpecification { Enabled = true } };

        // Act
        var table = TableOperations.Create(db, request);

        // Assert
        await Assert.That(table.KmsMasterKeyArn).IsEqualTo(FakeTable.AwsManagedKeyArn);
    }

    [Test]
    public async Task HandleUpdateTable_ToOnDemandWhileCreatingAnIndex_ZeroesBoth()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);
        const string Request = """
            {"TableName":"Thread","BillingMode":"PAY_PER_REQUEST","AttributeDefinitions":[{"AttributeName":"Subject","AttributeType":"S"}],
             "GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"BySubject","KeySchema":[{"AttributeName":"Subject","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"}}}]}
            """;

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", Request).GetProperty("TableDescription");

        // Assert
        await Assert.That(table.GetProperty("GlobalSecondaryIndexes").EnumerateArray().Sum(index => index.GetProperty("ProvisionedThroughput").GetProperty("ReadCapacityUnits").GetInt64())).IsEqualTo(0L);
    }

    [Test]
    public async Task HandleCreateTable_WithStream_DescribesTheStreamAndItsArn()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var table = FakeCalls.Ok(fake, "CreateTable", FakeStreams.Users("NEW_AND_OLD_IMAGES")).GetProperty("TableDescription");

        // Assert
        await Assert.That((FakeCalls.SameJson("""{"StreamEnabled":true,"StreamViewType":"NEW_AND_OLD_IMAGES"}""", table.GetProperty("StreamSpecification")),
            table.GetProperty("LatestStreamLabel").GetString(), table.GetProperty("LatestStreamArn").GetString())).IsEqualTo((true, FakeStreams.Label, FakeStreams.UsersArn));
    }

    [Test]
    public async Task HandleCreateTable_StreamWithoutViewType_AnswersValidationAndCreatesNoTable()
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","StreamSpecification":{"StreamEnabled":true}}""";

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That((answer, fake.TableNames.Count)).IsEqualTo(((HttpStatusCode.BadRequest, "ValidationException", Invalid + "StreamViewType must be specified when StreamEnabled is true"), 0));
    }

    [Test]
    public async Task HandleCreateTable_DisabledStreamWithViewType_AnswersValidationAndCreatesNoTable()
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","StreamSpecification":{"StreamEnabled":false,"StreamViewType":"NEW_IMAGE"}}""";

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That((answer, fake.TableNames.Count)).IsEqualTo(((HttpStatusCode.BadRequest, "ValidationException", Invalid + "Table is being created with a stream disabled, UpdateViewType should not be specified"), 0));
    }

    [Test]
    public async Task HandleUpdateTable_EnableStream_DescribesTheNewStream()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"KEYS_ONLY"}}""").GetProperty("TableDescription");

        // Assert
        await Assert.That((table.GetProperty("StreamSpecification").GetProperty("StreamViewType").GetString(), table.GetProperty("LatestStreamArn").GetString()))
            .IsEqualTo(("KEYS_ONLY", FakeStreams.UsersArn));
    }

    [Test]
    public async Task HandleUpdateTable_DisableStream_KeepsTheArnAndDropsTheSpecification()
    {
        // Arrange
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""").GetProperty("TableDescription");

        // Assert
        await Assert.That((table.TryGetProperty("StreamSpecification", out _), table.GetProperty("LatestStreamArn").GetString())).IsEqualTo((false, FakeStreams.UsersArn));
    }

    [Test]
    public async Task HandleUpdateTable_EnableAgainAfterDisabling_GivesANewStreamWithANewLabel()
    {
        // Arrange: a new view type takes a stream of its own; the clock has not moved, so the label is a millisecond later.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":false}}""");

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_IMAGE"}}""").GetProperty("TableDescription");

        // Assert
        await Assert.That((table.GetProperty("LatestStreamLabel").GetString(), table.GetProperty("LatestStreamArn").GetString()))
            .IsEqualTo(("2026-01-01T00:00:00.001", "arn:aws:dynamodb:us-east-1:123456789012:table/Users/stream/2026-01-01T00:00:00.001"));
    }

    [Test]
    [Arguments(true, """{"StreamEnabled":true,"StreamViewType":"NEW_IMAGE"}""", "Table already has an enabled stream: TableName: Users")]
    [Arguments(false, """{"StreamEnabled":false}""", "Table has no stream to disable")]
    [Arguments(false, """{"StreamEnabled":false,"StreamViewType":"NEW_IMAGE"}""", "Invalid StreamViewType: Stream StreamEnabled was false but a StreamViewType was present.")]
    [Arguments(false, """{"StreamEnabled":true}""", Invalid + "StreamViewType must be specified when StreamEnabled is true")]
    public async Task HandleUpdateTable_StreamChangeDynamoDbRefuses_AnswersValidationAndChangesNothing(bool withStream, string specification, string message)
    {
        // Arrange
        var fake = FakeCalls.With(withStream ? FakeStreams.Users("KEYS_ONLY") : FakeCalls.Users);

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTable", $$"""{"TableName":"Users","BillingMode":"PROVISIONED","ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1},"StreamSpecification":{{specification}}}""");

        // Assert
        await Assert.That((answer, fake.DescribeTable("Users").BillingModeSummary!.BillingMode)).IsEqualTo(((HttpStatusCode.BadRequest, "ValidationException", message), BillingMode.PayPerRequest));
    }

    [Test]
    public async Task HandleDeleteTable_TableWithStream_DisablesTheStream()
    {
        // Arrange: the API reference of DeleteTable: the stream goes into the DISABLED state.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));

        // Act
        FakeCalls.Ok(fake, "DeleteTable", """{"TableName":"Users"}""");

        // Assert
        var description = FakeCalls.Ok(fake, "DescribeStream", $$"""{"StreamArn":"{{FakeStreams.UsersArn}}"}""").GetProperty("StreamDescription");
        await Assert.That(description.GetProperty("StreamStatus").GetString()).IsEqualTo("DISABLED");
    }

    [Test]
    public async Task HandleCreateTable_StreamOfATableOfTheSameNameBefore_GivesANewLabel()
    {
        // Arrange: the stream of a deleted table stays for 24 hours, and a new table of its name gets a stream of its own.
        var fake = FakeCalls.With(FakeStreams.Users("KEYS_ONLY"));
        FakeCalls.Ok(fake, "DeleteTable", """{"TableName":"Users"}""");

        // Act
        var table = FakeCalls.Ok(fake, "CreateTable", FakeStreams.Users("KEYS_ONLY")).GetProperty("TableDescription");

        // Assert
        await Assert.That(table.GetProperty("LatestStreamLabel").GetString()).IsEqualTo("2026-01-01T00:00:00.001");
    }

    [Test]
    public async Task HandleUpdateTable_CreateIndexOverItemsWhoseIndexKeyDoesNotFit_LeavesThemOut()
    {
        // Arrange: the Developer Guide, "Detecting and correcting index key violations": an item whose index key has another type, or is too large, is not written to the index.
        // DynamoDB Local 3.3.2 leaves out the empty value and backfills the other two.
        var fake = FakeCalls.With(FakeCalls.Users);
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"fits"},"g":{"S":"v"}}}""");
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"empty"},"g":{"S":""}}}""");
        FakeCalls.Ok(fake, "PutItem", """{"TableName":"Users","Item":{"pk":{"S":"number"},"g":{"N":"1"}}}""");
        FakeCalls.Ok(fake, "PutItem", $$$$"""{"TableName":"Users","Item":{"pk":{"S":"large"},"g":{"S":"{{{{FakeWrites.Text(2049)}}}}"}}}""");
        FakeCalls.Ok(
            fake,
            "UpdateTable",
            """{"TableName":"Users","AttributeDefinitions":[{"AttributeName":"g","AttributeType":"S"}],"GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByG","KeySchema":[{"AttributeName":"g","KeyType":"HASH"}],"Projection":{"ProjectionType":"KEYS_ONLY"}}}]}""");

        // Act
        var body = FakeCalls.Ok(fake, "Scan", """{"TableName":"Users","IndexName":"ByG"}""");

        // Assert
        await Assert.That(FakeCalls.SameJson("""[{"pk":{"S":"fits"},"g":{"S":"v"}}]""", body.GetProperty("Items"))).IsTrue();
    }

    [Test]
    [Arguments("""{"MaxReadRequestUnits":10,"MaxWriteRequestUnits":5}""", """{"MaxReadRequestUnits":10,"MaxWriteRequestUnits":5}""")]
    [Arguments("""{"MaxReadRequestUnits":7}""", """{"MaxReadRequestUnits":7}""")]
    [Arguments("""{"MaxReadRequestUnits":-1,"MaxWriteRequestUnits":40000}""", """{"MaxWriteRequestUnits":40000}""")]
    public async Task HandleCreateTable_OnDemandThroughput_DescribesTheMaximums(string maximums, string described)
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","OnDemandThroughput":""" + maximums + "}";

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", request);

        // Assert
        await Assert.That(body.GetProperty("TableDescription").GetProperty("OnDemandThroughput").GetRawText()).IsEqualTo(described);
    }

    [Test]
    public async Task HandleCreateTable_NoMaximumEither_DescribesNoOnDemandThroughput()
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","OnDemandThroughput":{"MaxReadRequestUnits":-1,"MaxWriteRequestUnits":-1}}""";

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", request);

        // Assert
        await Assert.That(body.GetProperty("TableDescription").TryGetProperty("OnDemandThroughput", out _)).IsFalse();
    }

    [Test]
    [Arguments("""{}""", Invalid + "OnDemandThroughput must specify MaxReadRequestUnits, MaxWriteRequestUnits, or both")]
    [Arguments("""{"MaxReadRequestUnits":0,"MaxWriteRequestUnits":5}""", Invalid + "Requested MaxReadRequestUnits for OnDemandThroughput for table is outside of valid range")]
    [Arguments("""{"MaxReadRequestUnits":5,"MaxWriteRequestUnits":-2}""", Invalid + "Requested MaxWriteRequestUnits for OnDemandThroughput for table is outside of valid range")]
    [Arguments("""{"MaxReadRequestUnits":40001}""", "Cannot increase provisioned throughput to more than 40,000 units per table")]
    [Arguments("""{"MaxWriteRequestUnits":40001}""", "Cannot increase provisioned throughput to more than 40,000 units per table")]
    public async Task HandleCreateTable_InvalidOnDemandThroughput_AnswersValidation(string maximums, string message)
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.TrimEnd()[..^1] + ""","OnDemandThroughput":""" + maximums + "}";

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That((answer.Code, answer.Message, fake.TableNames.Count)).IsEqualTo(("ValidationException", message, 0));
    }

    [Test]
    [Arguments("""{"MaxReadRequestUnits":5}""", "MaxReadRequestUnits")]
    [Arguments("""{"MaxWriteRequestUnits":5}""", "MaxWriteRequestUnits")]
    public async Task HandleCreateTable_OnDemandThroughputWithProvisionedCapacity_AnswersValidation(string maximums, string member)
    {
        // Arrange
        var fake = FakeCalls.With();
        var request = FakeCalls.Users.Replace("\"BillingMode\":\"PAY_PER_REQUEST\"", "\"ProvisionedThroughput\":{\"ReadCapacityUnits\":1,\"WriteCapacityUnits\":1},\"OnDemandThroughput\":" + maximums, StringComparison.Ordinal);

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", request);

        // Assert
        await Assert.That(answer.Message).IsEqualTo(Invalid + member + " for OnDemandThroughput cannot be specified when table BillingMode is PROVISIONED.");
    }

    [Test]
    public async Task HandleCreateTable_IndexWithOnDemandThroughput_DescribesItOnTheIndex()
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var body = FakeCalls.Ok(fake, "CreateTable", UsersWithIndex("""{"MaxReadRequestUnits":4,"MaxWriteRequestUnits":3}"""));

        // Assert
        var index = body.GetProperty("TableDescription").GetProperty("GlobalSecondaryIndexes")[0];
        await Assert.That(index.GetProperty("OnDemandThroughput").GetRawText()).IsEqualTo("""{"MaxReadRequestUnits":4,"MaxWriteRequestUnits":3}""");
    }

    [Test]
    [Arguments("""{"MaxReadRequestUnits":0}""", Invalid + "Requested MaxReadRequestUnits for OnDemandThroughput for index : ByG is outside of valid range")]
    [Arguments("""{"MaxReadRequestUnits":1,"MaxWriteRequestUnits":40001}""", "Cannot increase provisioned throughput to more than 40,000 units per global secondary index")]
    public async Task HandleCreateTable_IndexWithInvalidOnDemandThroughput_AnswersValidation(string maximums, string message)
    {
        // Arrange
        var fake = FakeCalls.With();

        // Act
        var answer = FakeCalls.Error(fake, "CreateTable", UsersWithIndex(maximums));

        // Assert
        await Assert.That(answer.Message).IsEqualTo(message);
    }

    [Test]
    [Arguments("""{"MaxReadRequestUnits":20,"MaxWriteRequestUnits":15}""", """{"MaxReadRequestUnits":20,"MaxWriteRequestUnits":15}""")]
    [Arguments("""{"MaxReadRequestUnits":30}""", """{"MaxReadRequestUnits":30,"MaxWriteRequestUnits":5}""")]
    [Arguments("""{"MaxWriteRequestUnits":9}""", """{"MaxReadRequestUnits":10,"MaxWriteRequestUnits":9}""")]
    [Arguments("""{"MaxReadRequestUnits":-1}""", """{"MaxWriteRequestUnits":5}""")]
    [Arguments("""{"MaxReadRequestUnits":-1,"MaxWriteRequestUnits":-1}""", "")]
    public async Task HandleUpdateTable_OnDemandThroughput_ChangesWhatItNames(string maximums, string described)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users.TrimEnd()[..^1] + ""","OnDemandThroughput":{"MaxReadRequestUnits":10,"MaxWriteRequestUnits":5}}""");

        // Act
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","OnDemandThroughput":""" + maximums + "}");

        // Assert
        var table = FakeCalls.Ok(fake, "DescribeTable", """{"TableName":"Users"}""").GetProperty("Table");
        await Assert.That(table.TryGetProperty("OnDemandThroughput", out var found) ? found.GetRawText() : string.Empty).IsEqualTo(described);
    }

    [Test]
    public async Task HandleUpdateTable_OnDemandThroughputOfAProvisionedTable_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTable", """{"TableName":"Thread","OnDemandThroughput":{"MaxReadRequestUnits":5,"MaxWriteRequestUnits":5}}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo(Invalid + "MaxReadRequestUnits for OnDemandThroughput cannot be specified when the table BillingMode is PROVISIONED");
    }

    [Test]
    public async Task HandleUpdateTable_ToOnDemandWithMaximums_DescribesThem()
    {
        // Arrange
        var fake = FakeCalls.With(Thread);

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Thread","BillingMode":"PAY_PER_REQUEST","OnDemandThroughput":{"MaxReadRequestUnits":5,"MaxWriteRequestUnits":6}}""");

        // Assert
        await Assert.That(body.GetProperty("TableDescription").GetProperty("OnDemandThroughput").GetRawText()).IsEqualTo("""{"MaxReadRequestUnits":5,"MaxWriteRequestUnits":6}""");
    }

    [Test]
    public async Task HandleUpdateTable_ToProvisioned_DropsTheMaximumsOfTheTableAndItsIndexes()
    {
        // Arrange
        var fake = FakeCalls.With(UsersWithIndex("""{"MaxReadRequestUnits":4}""").TrimEnd()[..^1] + ""","OnDemandThroughput":{"MaxReadRequestUnits":10}}""");
        const string Request = """
            {"TableName":"Users","BillingMode":"PROVISIONED","ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1},
             "GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"ByG","ProvisionedThroughput":{"ReadCapacityUnits":1,"WriteCapacityUnits":1}}}]}
            """;

        // Act
        var table = FakeCalls.Ok(fake, "UpdateTable", Request).GetProperty("TableDescription");

        // Assert
        await Assert.That((table.TryGetProperty("OnDemandThroughput", out _), table.GetProperty("GlobalSecondaryIndexes")[0].TryGetProperty("OnDemandThroughput", out _))).IsEqualTo((false, false));
    }

    [Test]
    public async Task HandleUpdateTable_IndexOnDemandThroughput_ChangesWhatItNames()
    {
        // Arrange
        var fake = FakeCalls.With(UsersWithIndex("""{"MaxReadRequestUnits":4,"MaxWriteRequestUnits":3}"""));

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"ByG","OnDemandThroughput":{"MaxReadRequestUnits":6}}}]}""");

        // Assert
        var index = body.GetProperty("TableDescription").GetProperty("GlobalSecondaryIndexes")[0];
        await Assert.That(index.GetProperty("OnDemandThroughput").GetRawText()).IsEqualTo("""{"MaxReadRequestUnits":6,"MaxWriteRequestUnits":3}""");
    }

    [Test]
    public async Task HandleUpdateTable_IndexUpdateWithoutThroughput_AnswersValidation()
    {
        // Arrange
        var fake = FakeCalls.With(UsersWithIndex("""{"MaxReadRequestUnits":4}"""));

        // Act
        var answer = FakeCalls.Error(fake, "UpdateTable", """{"TableName":"Users","GlobalSecondaryIndexUpdates":[{"Update":{"IndexName":"ByG"}}]}""");

        // Assert
        await Assert.That(answer.Message).IsEqualTo(Invalid + "ProvisionedThroughput or OnDemandThroughput must be specified for index: ByG");
    }

    [Test]
    public async Task HandleUpdateTable_NewIndexWithOnDemandThroughput_DescribesItOnTheIndex()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);
        const string Request = """
            {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"g","AttributeType":"S"}],
             "GlobalSecondaryIndexUpdates":[{"Create":{"IndexName":"ByG","KeySchema":[{"AttributeName":"g","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"},
                                                       "OnDemandThroughput":{"MaxWriteRequestUnits":2}}}]}
            """;

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", Request);

        // Assert
        var index = body.GetProperty("TableDescription").GetProperty("GlobalSecondaryIndexes")[0];
        await Assert.That(index.GetProperty("OnDemandThroughput").GetRawText()).IsEqualTo("""{"MaxWriteRequestUnits":2}""");
    }

    [Test]
    [Arguments("""{"Enabled":true}""", "arn:aws:kms:us-east-1:123456789012:key/8f2b1c4e-6d3a-4e5f-9a7b-0c1d2e3f4a5b")]
    [Arguments("""{"Enabled":true,"SSEType":"KMS","KMSMasterKeyId":"alias/orders"}""", "arn:aws:kms:us-east-1:123456789012:alias/orders")]
    public async Task HandleUpdateTable_SseEnabled_EncryptsWithTheKmsKey(string sse, string keyArn)
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users);

        // Act
        FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","SSESpecification":""" + sse + "}");

        // Assert
        var table = FakeCalls.Ok(fake, "DescribeTable", """{"TableName":"Users"}""").GetProperty("Table");
        await Assert.That(table.GetProperty("SSEDescription").GetRawText()).IsEqualTo($$"""{"Status":"ENABLED","SSEType":"KMS","KMSMasterKeyArn":"{{keyArn}}"}""");
    }

    [Test]
    public async Task HandleUpdateTable_SseDisabled_GoesBackToTheKeyThatDynamoDbOwns()
    {
        // Arrange
        var fake = FakeCalls.With(FakeCalls.Users.TrimEnd()[..^1] + ""","SSESpecification":{"Enabled":true,"KMSMasterKeyId":"alias/orders"}}""");

        // Act
        var body = FakeCalls.Ok(fake, "UpdateTable", """{"TableName":"Users","SSESpecification":{"Enabled":false}}""");

        // Assert
        await Assert.That(body.GetProperty("TableDescription").TryGetProperty("SSEDescription", out _)).IsFalse();
    }

    [Test]
    public async Task Update_RecordWithoutAnyChange_ThrowsValidation()
    {
        // Arrange
        var db = new FakeDatabase(TimeProvider.System);
        TableOperations.Create(db, UsersRequest());

        // Act
        var act = () => TableOperations.Update(db, new UpdateTableRequest { TableName = "Users" });

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("At least one of ProvisionedThroughput, BillingMode, UpdateStreamEnabled, GlobalSecondaryIndexUpdates or SSESpecification or ReplicaUpdates is required", StringComparison.Ordinal);
    }

    private static string UsersWithIndex(string maximums) => $$"""
        {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"g","AttributeType":"S"}],
         "KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST",
         "GlobalSecondaryIndexes":[{"IndexName":"ByG","KeySchema":[{"AttributeName":"g","KeyType":"HASH"}],"Projection":{"ProjectionType":"ALL"},"OnDemandThroughput":{{maximums}}}]}
        """;

    private static CreateTableRequest UsersRequest() => new()
    {
        TableName = "Users",
        AttributeDefinitions = [new AttributeDefinition("pk", ScalarAttributeType.String)],
        KeySchema = [new KeySchemaElement("pk", KeyType.Hash)],
        BillingMode = BillingMode.PayPerRequest,
    };

    private static string Named(string name) => FakeCalls.Users.Replace("\"Users\"", $"\"{name}\"", StringComparison.Ordinal);

    // The answer without TableId, which is new per table.
    private static System.Text.Json.JsonElement Wrap(string member, System.Text.Json.JsonElement body) =>
        System.Text.Json.JsonElement.Parse($$"""{"{{member}}":{{FakeCalls.Without(body.GetProperty(member), "TableId").GetRawText()}}}""");
}
