namespace DynaSharp.UnitTests;

public sealed class ConsumedCapacityTests
{
    [Test]
    public async Task Constructor_NothingSet_AllMembersAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var capacity = new ConsumedCapacity();

        // Assert
        await Assert.That((capacity.TableName, capacity.CapacityUnits, capacity.ReadCapacityUnits, capacity.WriteCapacityUnits))
            .IsEqualTo(((string?)null, (double?)null, (double?)null, (double?)null));
    }
}
