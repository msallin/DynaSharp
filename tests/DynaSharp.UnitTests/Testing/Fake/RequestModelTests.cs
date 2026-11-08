using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

// What DynamoDB checks of the model of a request before it reads the request any further, and which of the failures it reports together. The wording and the order are those the Parity Suite
// recorded of DynamoDB (tier3/validation-ordering and tier3/error-messages).
public sealed class RequestModelTests
{
    private const string RcCapacity = "Value 'BOGUS' at 'returnConsumedCapacity' failed to satisfy constraint: Member must satisfy enum value set: [INDEXES, TOTAL, NONE]";
    private const string RcMetrics = "Value 'BOGUS2' at 'returnItemCollectionMetrics' failed to satisfy constraint: Member must satisfy enum value set: [SIZE, NONE]";
    private const string RcValues = "Value 'INVALID' at 'returnValues' failed to satisfy constraint: Member must satisfy enum value set: [ALL_NEW, UPDATED_OLD, ALL_OLD, NONE, UPDATED_NEW]";
    private const string RcFailure = "Value 'BOGUS3' at 'returnValuesOnConditionCheckFailure' failed to satisfy constraint: Member must satisfy enum value set: [NONE, ALL_OLD]";
    private const string EmptyName = "Value '' at 'tableName' failed to satisfy constraint: Member must have length greater than or equal to 1";
    private const string NoKey = "Value null at 'key' failed to satisfy constraint: Member must not be null";

    [Test]
    [Arguments("a", 1, null)]
    [Arguments("abc", 3, null)]
    [Arguments("a.b_c-d", 3, null)]
    [Arguments("", 1, "Value '' at 'p' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments("ab", 3, "Value 'ab' at 'p' failed to satisfy constraint: Member must have length greater than or equal to 3")]
    [Arguments("bad!name", 3, "Value 'bad!name' at 'p' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    public async Task TableNameViolation_Name_IsTheViolationOrNothing(string name, int minimum, string? expected)
    {
        // Arrange
        // Act
        var violation = RequestModel.TableNameViolation(name, "p", minimum);

        // Assert
        await Assert.That(violation).IsEqualTo(expected);
    }

    [Test]
    public async Task TableNameViolation_NameOf256Characters_IsTooLong()
    {
        // Arrange
        var name = new string('a', 256);

        // Act
        var violation = RequestModel.TableNameViolation(name, "tableName", 1);

        // Assert
        await Assert.That(violation).IsEqualTo($"Value '{name}' at 'tableName' failed to satisfy constraint: Member must have length less than or equal to 255");
    }

    [Test]
    public async Task Violation_WithoutTheValue_LeavesItOut()
    {
        // Arrange
        // Act
        var violation = RequestModel.Violation("Limit", "Member must have value greater than or equal to 1");

        // Assert
        await Assert.That(violation).IsEqualTo("Value at 'Limit' failed to satisfy constraint: Member must have value greater than or equal to 1");
    }

    [Test]
    public async Task EnumSet_EnumOfWhichDynamoDbOrderIsKnown_ListsItInThatOrder()
    {
        // Arrange
        // Act
        var set = RequestModel.EnumSet("ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities, noneIsDefault: true);

        // Assert
        await Assert.That(set).IsEquivalentTo(["INDEXES", "TOTAL", "NONE"]);
    }

    [Test]
    public async Task EnumSet_EnumOfWhichTheOrderIsNotKnown_ListsNoneFirstAndThenTheNamesAsDeclared()
    {
        // Arrange
        // Act
        var set = RequestModel.EnumSet("ReturnValuesOnConditionCheckFailure", WireNames.ReturnValuesOnConditionCheckFailures, noneIsDefault: true);

        // Assert
        await Assert.That(set).IsEquivalentTo(["NONE", "ALL_OLD"]);
    }

    [Test]
    [Arguments("PutItem", """{"TableName":"Users","Item":{},"ReturnConsumedCapacity":"BOGUS","ReturnItemCollectionMetrics":"BOGUS2","ReturnValues":"INVALID"}""", $"3 validation errors detected: {RcCapacity}; {RcMetrics}; {RcValues}")]
    [Arguments("PutItem", """{"TableName":"Users","Item":{},"ReturnValuesOnConditionCheckFailure":"BOGUS3"}""", $"1 validation error detected: {RcFailure}")]
    [Arguments("DeleteItem", """{"TableName":"Users","Key":{},"ReturnValues":"INVALID","ReturnConsumedCapacity":"BOGUS"}""", $"2 validation errors detected: {RcCapacity}; {RcValues}")]
    [Arguments("UpdateItem", """{"TableName":"Users","Key":{},"ReturnValues":"INVALID","ReturnConsumedCapacity":"BOGUS"}""", $"1 validation error detected: {RcCapacity}")]
    [Arguments("PutItem", """{"TableName":"","Item":{},"ReturnValues":"INVALID"}""", $"1 validation error detected: {EmptyName}")]
    [Arguments("UpdateItem", """{"TableName":"x!","Key":{}}""", "1 validation error detected: Value 'x!' at 'tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    [Arguments("UpdateItem", """{"TableName":"","UpdateExpression":"SET a = :a"}""", $"2 validation errors detected: {EmptyName}; {NoKey}")]
    [Arguments("UpdateItem", """{"TableName":"","ReturnConsumedCapacity":"BOGUS","ReturnValues":"INVALID"}""", $"4 validation errors detected: {RcCapacity}; {EmptyName}; {RcValues}; {NoKey}")]
    [Arguments("PutItem", """{"TableName":"Users"}""", "1 validation error detected: Value null at 'item' failed to satisfy constraint: Member must not be null")]
    [Arguments("DeleteItem", """{"Key":{}}""", "1 validation error detected: Value null at 'tableName' failed to satisfy constraint: Member must not be null")]
    public async Task CheckItemWrite_Request_ReportsTheViolationsDynamoDbJoins(string operation, string json, string message)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckItemWrite(request, operation);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("PutItem", """{"TableName":"ab","Item":{"pk":{"S":"a"}},"ReturnValues":"ALL_OLD","ReturnConsumedCapacity":"TOTAL","ReturnItemCollectionMetrics":"SIZE","ReturnValuesOnConditionCheckFailure":"NONE"}""")]
    [Arguments("UpdateItem", """{"TableName":"Users","Key":{},"ReturnValues":"UPDATED_NEW"}""")]
    [Arguments("DeleteItem", """{"TableName":7,"Key":{},"ReturnValues":3}""")]
    public async Task CheckItemWrite_SoundModel_Passes(string operation, string json)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckItemWrite(request, operation);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [Arguments("""{"TableName":"x!","Select":"BOGUS"}""", "1 validation error detected: Value 'x!' at 'tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    [Arguments("""{"TableName":"Shop","Limit":0}""", "1 validation error detected: Value at 'Limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    [Arguments("""{"TableName":"Shop","Limit":-3,"Select":"BOGUS","ReturnConsumedCapacity":"BOGUS"}""", $"3 validation errors detected: {RcCapacity}; Value 'BOGUS' at 'select' failed to satisfy constraint: Member must satisfy enum value set: [SPECIFIC_ATTRIBUTES, COUNT, ALL_ATTRIBUTES, ALL_PROJECTED_ATTRIBUTES]; Value at 'Limit' failed to satisfy constraint: Member must have value greater than or equal to 1")]
    public async Task CheckRead_Request_ReportsTheViolationsDynamoDbJoins(string json, string message)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckRead(request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"TableName":"Shop","Limit":1,"Select":"COUNT"}""")]
    [Arguments("""{"TableName":"Shop","Limit":"many"}""")]
    [Arguments("""{"TableName":"Shop","Limit":99999999999}""")]
    public async Task CheckRead_SoundModel_Passes(string json)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckRead(request);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [Arguments(false, """{}""", "1 validation error detected: Value null at 'requestItems' failed to satisfy constraint: Member must not be null")]
    [Arguments(false, """{"RequestItems":{}}""", "1 validation error detected: Value at 'RequestItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments(true, """{"RequestItems":{"Users":[]}}""", "1 validation error detected: Value at 'RequestItems.Users.member' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments(false, """{"RequestItems":{"Users":{"Keys":[]}}}""", "1 validation error detected: Value at 'RequestItems.Users.member.Keys' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments(true, """{"RequestItems":{"bad!name":[{"PutRequest":{}}]},"ReturnConsumedCapacity":"BOGUS","ReturnItemCollectionMetrics":"BOGUS2"}""", $$$"""3 validation errors detected: Value '{"bad!name":[{"PutRequest":{}}]}' at 'requestItems' failed to satisfy constraint: Map keys must satisfy constraint: [Member must have length less than or equal to 255, Member must have length greater than or equal to 3, Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+]; {{{RcCapacity}}}; {{{RcMetrics}}}""")]
    [Arguments(false, """{"RequestItems":{"ab":{"Keys":[{"pk":{"S":"a"}}]}},"ReturnConsumedCapacity":"BOGUS"}""", $$$"""2 validation errors detected: Value '{"ab":{"Keys":[{"pk":{"S":"a"}}]}}' at 'requestItems' failed to satisfy constraint: Map keys must satisfy constraint: [Member must have length less than or equal to 255, Member must have length greater than or equal to 3, Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+]; {{{RcCapacity}}}""")]
    public async Task CheckBatch_Request_ReportsTheViolationsDynamoDbJoins(bool writes, string json, string message)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckBatch(request, writes);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckBatch_MoreWritesThanABatchTakes_NamesTheListOfTheTable()
    {
        // Arrange
        var writes = string.Join(",", Enumerable.Repeat("""{"DeleteRequest":{"Key":{"pk":{"S":"a"}}}}""", 26));
        var request = JsonElement.Parse($$$"""{"RequestItems":{"Users":[{{{writes}}}]}}""");

        // Act
        var act = () => RequestModel.CheckBatch(request, writes: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("1 validation error detected: Value at 'RequestItems.Users.member' failed to satisfy constraint: Member must have length less than or equal to 25", StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckBatch_MoreKeysThanABatchTakes_NamesTheKeysOfTheTable()
    {
        // Arrange
        var keys = string.Join(",", Enumerable.Repeat("""{"pk":{"S":"a"}}""", 101));
        var request = JsonElement.Parse($$$$"""{"RequestItems":{"Users":{"Keys":[{{{{keys}}}}]}}}""");

        // Act
        var act = () => RequestModel.CheckBatch(request, writes: false);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("1 validation error detected: Value at 'RequestItems.Users.member.Keys' failed to satisfy constraint: Member must have length less than or equal to 100", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(false, """{"RequestItems":[]}""")]
    [Arguments(false, """{"RequestItems":{"Users":{}}}""")]
    [Arguments(true, """{"RequestItems":{"Users":{"PutRequest":{}}}}""")]
    [Arguments(true, """{"RequestItems":{"Users":[{"PutRequest":{}}]},"ReturnItemCollectionMetrics":"SIZE"}""")]
    public async Task CheckBatch_SoundModelOrOneOfAnotherKind_Passes(bool writes, string json)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckBatch(request, writes);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    [Arguments(true, """{}""", "1 validation error detected: Value null at 'transactItems' failed to satisfy constraint: Member must not be null")]
    [Arguments(true, """{"TransactItems":[]}""", "1 validation error detected: Value at 'TransactItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments(false, """{"TransactItems":[]}""", "1 validation error detected: Value at 'TransactItems' failed to satisfy constraint: Member must have length greater than or equal to 1")]
    [Arguments(true, """{"TransactItems":[{}]}""", "1 validation error detected: Value '' at 'transactItems' failed to satisfy constraint: TransactWriteRequest should contain Delete or Put or Update or ConditionCheck")]
    [Arguments(true, """{"TransactItems":[{"Put":{"TableName":"Users"},"Delete":{"TableName":"Users"}}]}""", "1 validation error detected: Value '' at 'transactItems' failed to satisfy constraint: TransactWriteRequest should contain Delete or Put or Update or ConditionCheck")]
    [Arguments(true, """{"TransactItems":[{"Put":{"TableName":"bad!name"}},{"Update":{"TableName":"bad!name"}}]}""", "2 validation errors detected: Value 'bad!name' at 'transactItems.1.member.put.tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+; Value 'bad!name' at 'transactItems.2.member.update.tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    [Arguments(true, """{"TransactItems":[{"ConditionCheck":{}}]}""", "1 validation error detected: Value null at 'transactItems.1.member.conditionCheck.tableName' failed to satisfy constraint: Member must not be null")]
    [Arguments(false, """{"TransactItems":[{"Get":{"TableName":"bad!name"}}]}""", "1 validation error detected: Value 'bad!name' at 'transactItems.1.member.get.tableName' failed to satisfy constraint: Member must satisfy regular expression pattern: [a-zA-Z0-9_.-]+")]
    [Arguments(true, """{"ClientRequestToken":"","ReturnConsumedCapacity":"BOGUS","TransactItems":[{"Put":{"TableName":"Users"}}]}""", $"2 validation errors detected: Value '' at 'clientRequestToken' failed to satisfy constraint: Member must have length greater than or equal to 1; {RcCapacity}")]
    public async Task CheckTransaction_Request_ReportsTheViolationsDynamoDbJoins(bool writes, string json, string message)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckTransaction(request, writes);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(message, StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckTransaction_TokenOf37Characters_NamesTheTokenBeforeTheEnums()
    {
        // Arrange
        var token = new string('x', 37);
        var request = JsonElement.Parse($$$"""{"ClientRequestToken":"{{{token}}}","ReturnConsumedCapacity":"BOGUS","ReturnItemCollectionMetrics":"BOGUS2","TransactItems":[{"Put":{"TableName":"Users"}}]}""");

        // Act
        var act = () => RequestModel.CheckTransaction(request, writes: true);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(
            $"3 validation errors detected: Value '{token}' at 'clientRequestToken' failed to satisfy constraint: Member must have length less than or equal to 36; {RcCapacity}; {RcMetrics}",
            StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckTransaction_MoreActionsThanATransactionTakes_ThrowsValidation()
    {
        // Arrange
        var actions = string.Join(",", Enumerable.Repeat("""{"Get":{"TableName":"Users"}}""", 101));
        var request = JsonElement.Parse($$$"""{"TransactItems":[{{{actions}}}]}""");

        // Act
        var act = () => RequestModel.CheckTransaction(request, writes: false);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("1 validation error detected: Value at 'TransactItems' failed to satisfy constraint: Member must have length less than or equal to 100", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(true, """{"TransactItems":"none"}""")]
    [Arguments(true, """{"TransactItems":["none",{"Put":"none"},{"Delete":{"TableName":7}}],"ClientRequestToken":"a"}""")]
    [Arguments(true, """{"TransactItems":[{"Get":{"TableName":"x!"}}]}""")]
    [Arguments(false, """{"TransactItems":[{"Put":{"TableName":"x!"}}]}""")]
    public async Task CheckTransaction_SoundModelOrMembersOfAnotherKind_Passes(bool writes, string json)
    {
        // Arrange: what the operation does not have is for the reader to answer as unsupported, not for the model to count
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckTransaction(request, writes);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task CheckCreateTable_WithoutATableName_AnswersTheMessageOfCreateTable()
    {
        // Arrange
        var request = JsonElement.Parse("""{"KeySchema":[]}""");

        // Act
        var act = () => RequestModel.CheckCreateTable(request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("The parameter 'TableName' is required but was not present in the request", StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckCreateTable_MalformedTableName_IsReportedAlone()
    {
        // Arrange
        var request = JsonElement.Parse("""{"TableName":"ab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"X"}]}""");

        // Act
        var act = () => RequestModel.CheckCreateTable(request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage("1 validation error detected: Value 'ab' at 'tableName' failed to satisfy constraint: Member must have length greater than or equal to 3", StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckCreateTable_EnumsOfTheKeysAndTheNamesOfTheIndexes_AreJoinedWithTheGlobalIndexesFirst()
    {
        // Arrange
        var request = JsonElement.Parse("""
            {"TableName":"Tab","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"X"}],"KeySchema":[{"AttributeName":"pk","KeyType":"Y"}],
             "LocalSecondaryIndexes":[{"IndexName":"l1","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"a","KeyType":"Z"}]}],
             "GlobalSecondaryIndexes":[{"IndexName":"g1","KeySchema":[{"AttributeName":"a","KeyType":"HASH"}]}]}
            """);

        // Act
        var act = () => RequestModel.CheckCreateTable(request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(
            "5 validation errors detected: Value 'X' at 'attributeDefinitions.1.member.attributeType' failed to satisfy constraint: Member must satisfy enum value set: [B, N, S]; "
            + "Value 'Y' at 'keySchema.1.member.keyType' failed to satisfy constraint: Member must satisfy enum value set: [HASH, RANGE]; "
            + "Value 'g1' at 'globalSecondaryIndexes.1.member.indexName' failed to satisfy constraint: Member must have length greater than or equal to 3; "
            + "Value 'l1' at 'localSecondaryIndexes.1.member.indexName' failed to satisfy constraint: Member must have length greater than or equal to 3; "
            + "Value 'Z' at 'localSecondaryIndexes.1.member.keySchema.2.member.keyType' failed to satisfy constraint: Member must satisfy enum value set: [HASH, RANGE]",
            StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckCreateTable_KeySchemaOfThreeElements_EchoesTheListAsJavaPrintsIt()
    {
        // Arrange: an element without a name prints as null
        var request = JsonElement.Parse("""{"TableName":"Tab","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"KeyType":"RANGE"},"odd"]}""");

        // Act
        var act = () => RequestModel.CheckCreateTable(request);

        // Assert
        await Assert.That(act).Throws<FakeError>().WithMessage(
            "1 validation error detected: Value '[KeySchemaElement(attributeName=pk, keyType=HASH), KeySchemaElement(attributeName=null, keyType=RANGE), KeySchemaElement(attributeName=null, keyType=null)]' at 'keySchema' failed to satisfy constraint: Member must have length less than or equal to 2",
            StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"TableName":7}""")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":"none","KeySchema":{},"LocalSecondaryIndexes":["none",{"IndexName":7}],"GlobalSecondaryIndexes":"none"}""")]
    [Arguments("""{"TableName":"Tab","AttributeDefinitions":["none",{"AttributeType":7}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"GlobalSecondaryIndexes":[{"IndexName":"gsi"}]}""")]
    public async Task CheckCreateTable_SoundModelOrMembersOfAnotherKind_Passes(string json)
    {
        // Arrange
        var request = JsonElement.Parse(json);

        // Act
        var act = () => RequestModel.CheckCreateTable(request);

        // Assert
        await Assert.That(act).ThrowsNothing();
    }
}
