using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary><c>Consistent()</c> on what cannot be read consistently, which DynamoDB rejects, and on what can.</summary>
public sealed class ConsistentReadTests
{
    [Test]
    public async Task Consistent_QueryOfAGlobalIndexOfAnEntity_ThrowsNamingTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byStatus = client.Table<TenantInvoice>("Shop").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => byStatus.Query("a").Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("ByStatus").And.Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_ScanOfAGlobalIndexOfAnEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byStatus = client.Table<TenantInvoice>("Shop").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => byStatus.Scan().Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_FanOutOfAGlobalIndexOfAnEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byStatus = client.Table<TenantInvoice>("Shop").Index(TenantInvoice.Indexes.ByStatus);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => byStatus.Query(["a", "b"]).Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_MixedQueryOfAGlobalIndex_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index<OverloadedBadge>(OverloadedBadge.Indexes.Gsi1);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => index.Query("a").Of<OverloadedBadge, OverloadedPass>().Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_MixedScanOfAGlobalIndex_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index<OverloadedBadge>(OverloadedBadge.Indexes.Gsi1);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => index.Scan().Of<OverloadedBadge, OverloadedPass>().Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_GlobalIndexWithThePartitionKeyOfTheTable_Throws()
    {
        // Arrange (the attribute does not tell, and the entity does)
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table<LabeledNote>("Shop").Index(LabeledNote.Indexes.OnPartition);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => index.Query("a").Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_IndexByHandWithAnotherPartitionKey_ThrowsBecauseOnlyAGlobalOneHasThat()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index("ByStatus", "status");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => index.Query("a").Consistent());

        // Assert
        await Assert.That(failure.Message).Contains("global secondary index");
    }

    [Test]
    public async Task Consistent_LocalIndexOfAnEntity_AsksForIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var byTitle = client.Table<Ticket>("Shop").Index(Ticket.Indexes.ByTitle);

        // Act
        var json = byTitle.Query("a").Consistent().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConsistentRead":true
            """);
    }

    [Test]
    public async Task Consistent_IndexByHandWithThePartitionKeyOfTheTable_GoesThroughBecauseItsKindIsNotKnown()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var index = client.Table("Shop", "pk", "sk").Index("LSI1", "pk", "lsi1sk");

        // Act
        var json = index.Scan().Consistent().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConsistentRead":true
            """);
    }

    [Test]
    public async Task Consistent_Table_AsksForIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Query(["a"]).Consistent().ToRequestJson();

        // Assert
        await Assert.That(json).Contains("""
            "ConsistentRead":true
            """);
    }
}
