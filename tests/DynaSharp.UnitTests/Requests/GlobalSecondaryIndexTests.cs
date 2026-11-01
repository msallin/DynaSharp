using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class GlobalSecondaryIndexTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ProvisionedThroughputIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndex { IndexName = "ByStatus", KeySchema = [], Projection = new() { ProjectionType = ProjectionType.All } };

        // Assert
        await Assert.That(value.ProvisionedThroughput).IsNull();
    }
}
