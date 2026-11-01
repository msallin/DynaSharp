using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class GlobalSecondaryIndexUpdateTests
{
    [Test]
    public async Task Constructor_NothingSet_EveryKindIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new GlobalSecondaryIndexUpdate();

        // Assert
        await Assert.That((value.Create, value.Update, value.Delete)).IsEqualTo(((GlobalSecondaryIndex?)null, (UpdateGlobalSecondaryIndexAction?)null, (DeleteGlobalSecondaryIndexAction?)null));
    }
}
