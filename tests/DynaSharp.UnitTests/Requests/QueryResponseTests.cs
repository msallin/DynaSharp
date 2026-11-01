using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class QueryResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_ItemsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new QueryResponse();

        // Assert
        await Assert.That(response.Items).IsEmpty();
    }

    [Test]
    public async Task Constructor_NothingSet_CountsAreZero()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new QueryResponse();

        // Assert
        await Assert.That((response.Count, response.ScannedCount)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Constructor_NothingSet_LastEvaluatedKeyIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new QueryResponse();

        // Assert
        await Assert.That(response.LastEvaluatedKey).IsNull();
    }

    [Test]
    public async Task Constructor_NothingSet_ConsumedCapacityIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = new QueryResponse();

        // Assert
        await Assert.That(response.ConsumedCapacity).IsNull();
    }
}
