#pragma warning disable DYNA213 // These updates name attributes the entity does not have, because the discriminator is all that is under test.

using DynaSharp.Testing;
using DynaSharp.UnitTests.Fluent.Mix;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// What an update of an entity writes of its discriminator, as bodies written by hand from <c>api.md</c> section 8, because <c>UpdateItem</c> can create the item. The
/// tests name <see cref="Customer"/>, whose value is <c>CUSTOMER</c> in <c>_type</c>.
/// </summary>
public sealed class DiscriminatorWriteTests
{
    private const string Start = """{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"}}""";

    private const string Name = "_type";

    [Test]
    public async Task ToRequestJson_UpdateWithoutAChange_WritesOnlyTheDiscriminator()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = if_not_exists(#n0, :v0)","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_UpdateByRawPath_WritesTheDiscriminatorAsAHandleDoes()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set("city", "Bern").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"city","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"Bern"},":v1":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_UpdateWithoutASet_StartsTheSetClause()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Add("n", 1).Remove("old").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n2 = if_not_exists(#n2, :v1) REMOVE #n1 ADD #n0 :v0","ExpressionAttributeNames":{"#n0":"n","#n1":"old","#n2":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_ValueThatIsThereAlready_SharesItsPlaceholder()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set("kind", "CUSTOMER").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v0)","ExpressionAttributeNames":{"#n0":"kind","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_ConditionOnTheDiscriminator_SharesItsNameAndStillWritesIt()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).If(Attr(Name).NotExists()).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = if_not_exists(#n0, :v0)","ConditionExpression":"attribute_not_exists(#n0)","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_IfExists_StillWritesIt()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).IfExists().ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n1 = if_not_exists(#n1, :v0)","ConditionExpression":"attribute_exists(#n0)","ExpressionAttributeNames":{"#n0":"pk","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_OwnAttributeOfTheType_NamesThatOne()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Client.Table<Tenant>("Shop").Update(Tenant.Keys.Of("a")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"UpdateExpression":"SET #n0 = if_not_exists(#n0, :v0)","ExpressionAttributeNames":{"#n0":"kind"},"ExpressionAttributeValues":{":v0":{"S":"TENANT"}}}""");
    }

    [Test]
    public async Task ToRequestJson_DiscriminatorIsAKeyOfTheTable_LeavesItOut()
    {
        // Arrange (DynamoDB rejects an update of a key, and the key is on every item)
        using var fixture = new Fixture();

        // Act
        var json = fixture.Client.Table<TypeKeyed>("Shop").Update(new StoredKey<TypeKeyed>("a", "NOTE")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"},"sk":{"S":"NOTE"}}}""");
    }

    [Test]
    public async Task ToRequestJson_TypeThatOptedOut_WritesNothing()
    {
        // Arrange
        using var fixture = new Fixture();
        var path = "city";

        // Act
        var json = fixture.Client.Table<OptedOut>("Shop").Update(OptedOut.Keys.Of("a")).Set(path, "Bern").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("_type");
    }

    [Test]
    public async Task ToRequestJson_TableWithoutASchema_WritesNothing()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Client.Table("Shop", "pk", "sk").Update("a", "b").Set("city", "Bern").ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("_type");
    }

    [Test]
    public async Task ToRequestJson_DeleteOfAnEntity_WritesNothing()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Delete(Customer.Keys.Of("a")).ToRequestJson();

        // Assert
        await Assert.That(json).DoesNotContain("_type");
    }

    [Test]
    public async Task ToRequestJson_ReturnOfOneAttribute_WritesItToo()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Add("n", 1).Return(Attr<int>("n")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n1 = if_not_exists(#n1, :v1) ADD #n0 :v0","ExpressionAttributeNames":{"#n0":"n","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"CUSTOMER"}},"ReturnValues":"UPDATED_NEW"}""");
    }

    [Test]
    public async Task ToRequestJson_TransactionOfSeveralKinds_WritesItForEveryUpdateOfAnEntityAndNothingElse()
    {
        // Arrange
        using var fixture = new Fixture();
        var customers = fixture.Customers;
        var shop = fixture.Client.Table("Other", "pk", "sk");

        // Act
        var json = fixture.Client.TransactWrite().Token("t")
            .Update(customers, Customer.Keys.Of("a"))
            .Update(shop, "c", "d").Set("x", 1)
            .Delete(customers, Customer.Keys.Of("e"))
            .Update(customers, Customer.Keys.Of("g")).Set("y", 1)
            .ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"TransactItems":[""" +
            """{"Update":{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"}},"UpdateExpression":"SET #n0 = if_not_exists(#n0, :v0)","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}},""" +
            """{"Update":{"TableName":"Other","Key":{"pk":{"S":"c"},"sk":{"S":"d"}},"UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"x"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}},""" +
            """{"Delete":{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#e"},"sk":{"S":"PROFILE"}}}},""" +
            """{"Update":{"TableName":"Shop","Key":{"pk":{"S":"CUSTOMER#g"},"sk":{"S":"PROFILE"}},"UpdateExpression":"SET #n0 = :v0, #n1 = if_not_exists(#n1, :v1)","ExpressionAttributeNames":{"#n0":"y","#n1":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"S":"CUSTOMER"}}}}""" +
            """],"ClientRequestToken":"t"}""");
    }

    [Test]
    public async Task ToRequestJson_RawSetOfTheDiscriminator_IsTheOnlyAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set(Name, "OTHER").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"OTHER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_RawSetIfNotExists_IsTheOnlyAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).SetIfNotExists(Name, "CUSTOMER").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = if_not_exists(#n0, :v0)","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_RawRemove_IsTheOnlyAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Remove(Name).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"REMOVE #n0","ExpressionAttributeNames":{"#n0":"_type"}}""");
    }

    [Test]
    public async Task ToRequestJson_RawAdd_IsTheOnlyAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Add(Name, 1).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"ADD #n0 :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}""");
    }

    [Test]
    public async Task ToRequestJson_RawDelete_IsTheOnlyAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Delete(Name, AttributeValue.FromStringSet(["x"])).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"DELETE #n0 :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"SS":["x"]}}}""");
    }

    [Test]
    public async Task ToRequestJson_RawPathInsideTheDiscriminator_IsTheOnlyAction()
    {
        // Arrange (DynamoDB rejects a path that overlaps another one as well)
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set("_type.inner", 1).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0.#n1 = :v0","ExpressionAttributeNames":{"#n0":"_type","#n1":"inner"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}""");
    }

    [Test]
    public async Task ToRequestJson_InterpolatedActionWithAHandle_IsTheOnlyAction()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set($"{Attr(Name)} = {"OTHER"}").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"OTHER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_InterpolatedActionThatSpellsTheNameOut_IsTheOnlyActionAndAddsNoName()
    {
        // Arrange (an unused name is an error of its own)
        using var fixture = new Fixture();

        // Act (DynamoDB refuses a bare _type, which DYNA212 says; only the rendering is under test)
#pragma warning disable DYNA212
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set($"_type = {"OTHER"}").ToRequestJson();
#pragma warning restore DYNA212

        // Assert
        await Assert.That(json).IsEqualTo(Start + ""","UpdateExpression":"SET _type = :v0","ExpressionAttributeValues":{":v0":{"S":"OTHER"}}}""");
    }

    [Test]
    public async Task ToRequestJson_DiscriminatorAsTheLaterOfTwoActionsOfOneCall_IsTheOnlyOne()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set($"{Attr("n")} = if_not_exists({Attr("m")}, {1}), {Attr(Name)} = {2}").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = if_not_exists(#n1, :v0), #n2 = :v1",
            """);
    }

    [Test]
    public async Task ToRequestJson_DiscriminatorInsideAFunctionOnTheRightSide_OnlyReadsItSoItIsStillWritten()
    {
        // Arrange (DynamoDB takes a path read and written in one update)
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set($"{Attr("n")} = if_not_exists({Attr(Name)}, {1}), {Attr("m")} = {2}").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = if_not_exists(#n1, :v0), #n2 = :v1, #n1 = if_not_exists(#n1, :v2)",
            """);
    }

    [Test]
    public async Task ToRequestJson_DiscriminatorOnTheRightSide_OnlyReadsItSoItIsStillWritten()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).Set($"{Attr("copy")} = {Attr(Name)}").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "UpdateExpression":"SET #n0 = #n1, #n1 = if_not_exists(#n1, :v0)",
            """);
    }

    [Test]
    public async Task ToRequestJson_PathsThatOnlyStartLikeTheDiscriminator_AreOtherAttributes()
    {
        // Arrange (the name is #n1, and #n10 and #n11 start the same way)
        using var fixture = new Fixture();

        // Act
        var json = fixture.Customers.Update(Customer.Keys.Of("a")).If(Attr("first").Exists()).If(Attr(Name).Exists())
            .Set("a2", 1).Set("a3", 1).Set("a4", 1).Set("a5", 1).Set("a6", 1).Set("a7", 1).Set("a8", 1).Set("a9", 1).Set("a10", 1).Set("a11", 1).Set("_type2", 1)
            .ToRequestJson();

        // Assert
        await Assert.That(json).Contains("#n11 = :v0, #n12 = :v0, #n1 = if_not_exists(#n1, :v1)\"");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly StubHandler _handler = new();

        public Fixture()
        {
            Client = TestClient.Create(_handler);
            Customers = Client.Table<Customer>("Shop");
        }

        public DynamoDbClient Client { get; }

        public Table<Customer> Customers { get; }

        public void Dispose()
        {
            Client.Dispose();
            _handler.Dispose();
        }
    }
}
