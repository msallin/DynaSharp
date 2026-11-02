using DynaSharp.Requests;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Testing.Consumer;

namespace DynaSharp.UnitTests.Testing;

/// <summary>The answer to a <c>Select: COUNT</c>, read through the client.</summary>
public sealed class StubResponseCountTests
{
    [Test]
    public async Task Count_Count_IsABodyWithoutItems()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Count(3);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Count":3,"ScannedCount":3}""");
    }

    [Test]
    public async Task Count_ScannedCount_SaysHowManyAFilterSaw()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Count(3, scannedCount: 9));

        // Act
        var response = await client.QueryAsync(TestClient.CreateQuery());

        // Assert
        await Assert.That((response.Count, response.ScannedCount, response.Items.Count, response.LastEvaluatedKey)).IsEqualTo((3, 9, 0, null));
    }

    [Test]
    public async Task Count_Zero_IsAValidAnswer()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Count(0));

        // Act
        var count = await client.Table("Users", "pk").Query("a").CountAsync();

        // Assert
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Count_ItemKey_ContinuesTheCount()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.Create(stub);
        stub.Enqueue(StubResponse.Count(2, new Item { ["pk"] = "a" }));
        stub.Enqueue(StubResponse.Count(3));

        // Act
        var count = await client.Table("Users", "pk").Query("a").CountAsync();

        // Assert
        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    public async Task Count_GeneratedKey_WritesItAsTheLastEvaluatedKey()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Count(1, new SubscriberKey("ada"), scannedCount: 4);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Count":1,"ScannedCount":4,"LastEvaluatedKey":{"pk":{"S":"ada"}}}""");
    }

    [Test]
    public async Task Count_NegativeCount_ThrowsNamingTheParameter()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Count(-1);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithParameterName("count");
    }

    [Test]
    public async Task Count_ScannedCountBelowTheCount_ThrowsNamingTheParameter()
    {
        // Arrange (nothing to arrange)

        // Act
        var act = () => StubResponse.Count(3, scannedCount: 2);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithParameterName("scannedCount");
    }

    [Test]
    public async Task Count_NullItemKey_Throws()
    {
        // Arrange
        Item key = null!;

        // Act
        var act = () => StubResponse.Count(1, key);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Count_NullGeneratedKey_Throws()
    {
        // Arrange
        DynaSharp.Mapping.IKey key = null!;

        // Act
        var act = () => StubResponse.Count(1, key);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }
}
