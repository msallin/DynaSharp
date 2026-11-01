using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// The typed forms of <c>Query</c> as bodies written by hand: a key goes out as its formatted text, on every table and index, alone and in a fan-out. A named <see cref="PartitionKey{TEntity}"/>
/// goes through a generic helper: <c>dotnet format</c> without a built generator does not know the entity as one, and reports such an argument as an error that it could fix.
/// </summary>
public sealed class PartitionKeyTests
{
    private const string MemberQuery = """{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0 AND begins_with(#n1, :v1)","FilterExpression":"#n2 = :v2","ExpressionAttributeNames":{"#n0":"pk","#n1":"sk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"TENANT#t#USER#u"},":v1":{"S":"REGION#EU#CITY#"},":v2":{"S":"MEMBER"}}}""";

    private const string UntypedQuery = """{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"pk"},"ExpressionAttributeValues":{":v0":{"S":"TENANT#t#USER#u"}}}""";

    private const string StatusQuery = """{"TableName":"Shop","IndexName":"GSI1","KeyConditionExpression":"#n0 = :v0","FilterExpression":"#n1 = :v1","ExpressionAttributeNames":{"#n0":"gsi1pk","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"STATUS#Pro"},":v1":{"S":"ORDER"}}}""";

    private const string UntypedStatusQuery = """{"TableName":"Shop","IndexName":"GSI1","KeyConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"gsi1pk"},"ExpressionAttributeValues":{":v0":{"S":"STATUS#Pro"}}}""";

    [Test]
    public async Task Value_KeyOfABuilder_IsTheFormattedKey()
    {
        // Arrange, Act
        var key = Member.Keys.Pk.Of("t", "u");

        // Assert
        await Assert.That((key.Value, key.ToString())).IsEqualTo(("TENANT#t#USER#u", "TENANT#t#USER#u"));
    }

    [Test]
    public async Task Value_DefaultKey_IsEmpty()
    {
        // Arrange
        var key = default(PartitionKey<Member>);

        // Act
        var (value, text) = (key.Value, key.ToString());

        // Assert
        await Assert.That((value, text)).IsEqualTo((string.Empty, string.Empty));
    }

    [Test]
    public async Task Query_KeyOfTheEntity_SendsTheFormattedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Member>("Shop").Query(Member.Keys.Pk.Of("t", "u"), Member.Keys.Sk.BeginsWith("EU")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(MemberQuery);
    }

    [Test]
    public async Task Query_KeyOnATableWithoutASchema_SendsTheFormattedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query(Member.Keys.Pk.Of("t", "u")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(UntypedQuery);
    }

    [Test]
    public async Task Query_KeyOfAnIndex_SendsTheFormattedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<KeyedOrder>("Shop").Index(KeyedOrder.Indexes.Gsi1);

        // Act
        var json = index.Query(KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(StatusQuery);
    }

    [Test]
    public async Task Query_KeyOnAnIndexWithoutASchema_SendsTheFormattedKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index(KeyedOrder.Indexes.Gsi1, "gsi1pk");

        // Act
        var json = index.Query(KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro)).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(UntypedStatusQuery);
    }

    [Test]
    public async Task Query_KeysOfTheEntity_FansOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<Member>("Shop").Query([Member.Keys.Pk.Of("t", "u"), Member.Keys.Pk.Of("t", "v")], Member.Keys.Sk.BeginsWith()).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :pk AND begins_with(#n1, :v0)","FilterExpression":"#n2 = :v1","ExpressionAttributeNames":{"#n0":"pk","#n1":"sk","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"S":"REGION#"},":v1":{"S":"MEMBER"},":pk":{"S":"TENANT#t#USER#u"}}}""");
    }

    [Test]
    public async Task Query_KeysOnATableWithoutASchema_FansOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query([Member.Keys.Pk.Of("t", "u"), Member.Keys.Pk.Of("t", "v")]).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :pk","ExpressionAttributeNames":{"#n0":"pk"},"ExpressionAttributeValues":{":pk":{"S":"TENANT#t#USER#u"}}}""");
    }

    [Test]
    public async Task Query_KeysOfAnIndex_FansOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<KeyedOrder>("Shop").Index(KeyedOrder.Indexes.Gsi1);

        // Act
        var json = index.Query([KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro), KeyedOrder.Keys.Gsi1Pk.Of(Plan.Free)]).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"GSI1","KeyConditionExpression":"#n0 = :pk","FilterExpression":"#n1 = :v0","ExpressionAttributeNames":{"#n0":"gsi1pk","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"ORDER"},":pk":{"S":"STATUS#Pro"}}}""");
    }

    [Test]
    public async Task Query_KeysOnAnIndexWithoutASchema_FansOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index(KeyedOrder.Indexes.Gsi1, "gsi1pk");

        // Act
        var json = index.Query([KeyedOrder.Keys.Gsi1Pk.Of(Plan.Pro), KeyedOrder.Keys.Gsi1Pk.Of(Plan.Free)]).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"GSI1","KeyConditionExpression":"#n0 = :pk","ExpressionAttributeNames":{"#n0":"gsi1pk"},"ExpressionAttributeValues":{":pk":{"S":"STATUS#Pro"}}}""");
    }

    [Test]
    public async Task Value_NumberKey_IsItsDigits()
    {
        // Arrange, Act
        var key = NumberKeyed.Keys.Pk.Of(7);

        // Assert
        await Assert.That(key.Value).IsEqualTo("7");
    }

    [Test]
    public async Task Value_BinaryKey_IsItsBase64()
    {
        // Arrange, Act
        var key = BinaryKeyed.Keys.Pk.Of([1, 2]);

        // Assert
        await Assert.That(key.Value).IsEqualTo("AQI=");
    }

    [Test]
    public async Task Value_PlainKeyStoredAsAString_IsWhatTheConverterWrites()
    {
        // Arrange
        var at = new DateTimeOffset(2026, 10, 31, 14, 34, 56, TimeSpan.FromHours(2));

        // Act
        var text = BinaryKeyed.Keys.Sk.Format(at);

        // Assert
        await Assert.That(text).IsEqualTo("2026-10-31T12:34:56.0000000Z");
    }

    [Test]
    public async Task Query_NumberKey_SendsANumber()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<NumberKeyed>("Shop").Query(NumberKeyed.Keys.Pk.Of(7), SortKey.Ge(NumberKeyed.Keys.Sk.Of(5))).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","KeyConditionExpression":"#n0 = :v0 AND #n1 >= :v1","FilterExpression":"#n2 = :v2","ExpressionAttributeNames":{"#n0":"Id","#n1":"Sequence","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"N":"7"},":v1":{"N":"5"},":v2":{"S":"NUMBERED"}}}""");
    }

    [Test]
    public async Task Query_BinaryKey_SendsBinary()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<BinaryKeyed>("Shop").Query(BinaryKeyed.Keys.Pk.Of([1, 2])).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":v0":{"B":"AQI="}""");
    }

    [Test]
    public async Task Query_NumberKeys_FanOutAsNumbers()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table<NumberKeyed>("Shop").Query([NumberKeyed.Keys.Pk.Of(1), NumberKeyed.Keys.Pk.Of(2)]).ToRequestJson();

        // Assert
        await Assert.That(json).Contains(""":pk":{"N":"1"}""");
    }

    [Test]
    public async Task Query_BarePlaceholderOfAnIndex_SendsTheValueOfTheProperty()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<OwnedThing>("Shop").Index(OwnedThing.Indexes.ByOwner);

        // Act
        var json = index.Query(OwnedThing.Keys.ByOwnerPk.Of("alice"), SortKey.Gt(OwnedThing.Keys.ByOwnerSk.Of(3))).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"ByOwner","KeyConditionExpression":"#n0 = :v0 AND #n1 > :v1","ExpressionAttributeNames":{"#n0":"owner","#n1":"rank"},"ExpressionAttributeValues":{":v0":{"S":"alice"},":v1":{"N":"3"}}}""");
    }

    [Test]
    public async Task Query_EmptyPlainKey_ThrowsAsAnEmptyKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var things = client.Table<OwnedThing>("Shop").Index(OwnedThing.Indexes.ByOwner);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(things.Query(OwnedThing.Keys.ByOwnerPk.Of(string.Empty)).ToRequestJson()));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKey");
    }

    [Test]
    public async Task Query_DefaultKey_ThrowsAsAnEmptyKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var members = client.Table<Member>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(QueryOf(members, default)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKey");
    }

    [Test]
    public async Task Query_DefaultKeyAmongSeveral_ThrowsNamingThePosition()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var members = client.Table<Member>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(members.Query([Member.Keys.Pk.Of("t", "u"), default]).ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("position 1 is empty");
    }

    [Test]
    public async Task Query_RepeatedKey_ThrowsNamingThePositions()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var members = client.Table<Member>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(members.Query([Member.Keys.Pk.Of("t", "u"), Member.Keys.Pk.Of("t", "u")]).ToRequestJson()));

        // Assert
        await Assert.That(failure.Message).Contains("position 1 repeats the one at position 0");
    }

    [Test]
    public async Task Query_NullListOfKeys_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var members = client.Table<Member>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(QueryOfAll(members, null!)));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKeys");
    }

    private static string QueryOf<T>(Table<T> table, PartitionKey<T> key)
        where T : DynaSharp.Mapping.IDynamoDbEntity<T> => table.Query(key).ToRequestJson();

    private static string QueryOfAll<T>(Table<T> table, IEnumerable<PartitionKey<T>> keys)
        where T : DynaSharp.Mapping.IDynamoDbEntity<T> => table.Query(keys).ToRequestJson();
}
