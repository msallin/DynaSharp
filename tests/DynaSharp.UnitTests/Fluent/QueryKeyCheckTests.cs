using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;

// These tests query with a key of another attribute on purpose, to see the query throw at run time.
#pragma warning disable DYNA224

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What <c>Query</c> does with a <see cref="PartitionKey{TEntity}"/> of the entity that was built for another attribute, which the compiler cannot see.</summary>
public sealed class QueryKeyCheckTests
{
    [Test]
    public async Task Query_TableKeyOnAnIndex_ThrowsNamingTheBuilderOfTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byStatus = client.Table<TenantInvoice>("Billing").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = byStatus.Query(TenantInvoice.Keys.Pk.Of("acme")));

        // Assert
        await Assert.That(failure.Message).Contains("TenantInvoice.Keys.ByStatusPk.Of").And.Contains("attribute pk").And.Contains("reads bystatuspk");
    }

    [Test]
    public async Task Query_IndexKeyOnTheTable_ThrowsNamingTheBuilderOfTheTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");
        var key = TenantInvoice.Keys.ByStatusPk.Of("acme", InvoiceState.Paid); // in a variable, which DYNA224 does not follow

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = invoices.Query(key));

        // Assert
        await Assert.That(failure.Message).Contains("TenantInvoice.Keys.Pk.Of");
    }

    [Test]
    public async Task Query_TableKeyOnAnIndexKeyedByTheDiscriminator_ThrowsNamingTheKeyOfTheDiscriminator()
    {
        // Arrange (an index the entity does not declare, whose partition key is the discriminator)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byType = client.Table<Customer>("Shop").Index("ByType", "_type", "sk");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = byType.Query(Customer.Keys.Pk.Of("a")));

        // Assert
        await Assert.That(failure.Message).Contains("Use QueryByDiscriminator() of the index.");
    }

    [Test]
    public async Task Query_TableKeyOnAnIndexTheEntityDoesNotDeclare_ThrowsNamingTheDeclaration()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byCity = client.Table<Customer>("Shop").Index("ByCity", "city");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = byCity.Query(Customer.Keys.Pk.Of("a")));

        // Assert
        await Assert.That(failure.Message).Contains("Declare the index on DynaSharp.UnitTests.Fluent.Customer");
    }

    [Test]
    public async Task QueryByDiscriminator_IndexKeyedByIt_SendsTheDiscriminator()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byType = client.Table<Customer>("Shop").Index("ByType", "_type", "sk");

        // Act
        var json = byType.QueryByDiscriminator().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"S":"CUSTOMER"}
            """);
    }

    [Test]
    public async Task QueryByDiscriminator_EntityWithoutOne_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byType = client.Table<Session>("Shop").Index("ByType", "_type");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = byType.QueryByDiscriminator());

        // Assert
        await Assert.That(failure.Message).Contains("has no discriminator");
    }

    [Test]
    public async Task Query_KeyOnAPlainIndexKey_ThrowsNamingItsBuilder()
    {
        // Arrange (the sort key of a sparse index can be plain, and a partition key of an index too)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<PlainIndexed>("Shop").Index(PlainIndexed.Indexes.ByCode);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = index.Query(PlainIndexed.Keys.Pk.Of("a")));

        // Assert
        await Assert.That(failure.Message).Contains("PlainIndexed.Keys.ByCodePk.Of");
    }

    [Test]
    public async Task Query_NumberKeyOfAnotherAttribute_ThrowsNamingTheWayOut()
    {
        // Arrange (a plain key knows its attribute as a key of a template does, and this table has nothing that entity declares)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = shop.Query(NumberKeyed.Keys.Pk.Of(7)));

        // Assert
        await Assert.That(failure.Message).Contains("attribute Id").And.Contains("as an AttributeValue");
    }

    [Test]
    public async Task Query_NumberKeyOfAnotherAttributeOnAnIndexWithoutASchema_ThrowsNamingTheWayOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byCode = client.Table("Shop", "pk").Index("ByCode", "code");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = byCode.Query(NumberKeyed.Keys.Pk.Of(7)));

        // Assert
        await Assert.That(failure.Message).Contains("attribute Id").And.Contains("as an AttributeValue");
    }

    [Test]
    public async Task Query_NumberKeysOfAnotherAttributeOnAnIndexWithoutASchema_ThrowsNamingTheWayOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byCode = client.Table("Shop", "pk").Index("ByCode", "code");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = byCode.Query([NumberKeyed.Keys.Pk.Of(7)]));

        // Assert
        await Assert.That(failure.Message).Contains("attribute Id").And.Contains("as an AttributeValue");
    }

    [Test]
    public async Task Query_BinaryKeyOfAnotherAttribute_ThrowsNamingTheWayOut()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = shop.Query([BinaryKeyed.Keys.Pk.Of([1])]));

        // Assert
        await Assert.That(failure.Message).Contains("attribute Id").And.Contains("as an AttributeValue");
    }

    [Test]
    public async Task Query_IndexKeyOnAPlainTableKey_ThrowsNamingItsBuilder()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var devices = client.Table<PlainDevice>("Shop");
        var key = PlainDevice.Keys.ByShelfPk.Of("s"); // in a variable, which DYNA224 does not follow

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = devices.Query(key));

        // Assert
        await Assert.That(failure.Message).Contains("PlainDevice.Keys.Pk.Of");
    }

    [Test]
    public async Task Query_TableKeyOnALocalIndex_ReadsTheAttributeOfTheTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<Ticket>("Shop").Index(Ticket.Indexes.ByTitle);

        // Act
        var json = index.Query(Ticket.Keys.Pk.Of("t1")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"pk"
            """);
    }

    [Test]
    public async Task Query_TableKeyOnAnIndexWithTheSameAttribute_ReadsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<LabeledNote>("Shop").Index(LabeledNote.Indexes.OnPartition);

        // Act
        var json = index.Query(LabeledNote.Keys.Pk.Of("n1")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ExpressionAttributeNames":{"#n0":"pk"
            """);
    }

    [Test]
    public async Task Query_KeyOfAnotherEntityOnAnOverloadedIndex_ReadsIt()
    {
        // Arrange (the index was opened through one entity, and the attribute is what counts)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk").Index<OverloadedBadge>(OverloadedBadge.Indexes.Gsi1);

        // Act
        var json = index.Query(OverloadedPass.Keys.Gsi1Pk.Of("g1")).ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"S":"GATE#g1"}
            """);
    }

    [Test]
    public async Task Query_TableKeyOfAnEntityOnAnIndexWithoutASchema_ThrowsNamingTheBuilder()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk").Index<OverloadedBadge>(OverloadedBadge.Indexes.Gsi1);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = index.Query(OverloadedBadge.Keys.Pk.Of("b1")));

        // Assert
        await Assert.That(failure.Message).Contains("OverloadedBadge.Keys.Gsi1Pk.Of");
    }

    [Test]
    public async Task Query_IndexKeyOfAnEntityOnATableWithoutASchema_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = shop.Query(OverloadedBadge.Keys.Gsi1Pk.Of("h")));

        // Assert
        await Assert.That(failure.Message).Contains("OverloadedBadge.Keys.Pk.Of");
    }

    [Test]
    public async Task Query_TableKeysOnAnIndexFanOut_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byStatus = client.Table<TenantInvoice>("Billing").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = byStatus.Query([TenantInvoice.Keys.Pk.Of("acme")]));

        // Assert
        await Assert.That(failure.Message).Contains("TenantInvoice.Keys.ByStatusPk.Of");
    }

    [Test]
    public async Task Query_IndexKeysOnATableFanOut_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var invoices = client.Table<TenantInvoice>("Billing");
        var key = TenantInvoice.Keys.ByStatusPk.Of("acme", InvoiceState.Paid); // in a variable, which DYNA224 does not follow

        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = invoices.Query([key]));

        // Assert
        await Assert.That(failure.Message).Contains("TenantInvoice.Keys.Pk.Of");
    }

    [Test]
    public async Task Query_FanOutsOnATableAndAnIndexWithoutASchema_Throw()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var index = shop.Index<OverloadedBadge>(OverloadedBadge.Indexes.Gsi1);

        // Act
        var onTable = Assert.Throws<ArgumentException>(() => _ = shop.Query([OverloadedBadge.Keys.Gsi1Pk.Of("h")]));
        var onIndex = Assert.Throws<ArgumentException>(() => _ = index.Query([OverloadedBadge.Keys.Pk.Of("b")]));

        // Assert
        await Assert.That((onTable.Message.Contains("Keys.Pk.Of", StringComparison.Ordinal), onIndex.Message.Contains("Keys.Gsi1Pk.Of", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Query_KeyThatDoesNotSayItsAttribute_IsNotChecked()
    {
        // Arrange (code that made a key by hand before keys said what they are for)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byStatus = client.Table<TenantInvoice>("Billing").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var json = QueryOf(byStatus, new PartitionKey<TenantInvoice>("x"));

        // Assert
        await Assert.That(json).Contains("""
            ":v0":{"S":"x"}
            """);
    }

    // dotnet format runs without the generator, which reports a named key as an error it could fix.
    private static string QueryOf<T>(Index<T> index, PartitionKey<T> key)
        where T : DynaSharp.Mapping.IDynamoDbEntity<T> => index.Query(key).ToRequestJson();
}
