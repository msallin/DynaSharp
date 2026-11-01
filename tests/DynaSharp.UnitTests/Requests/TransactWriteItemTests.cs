using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactWriteItemTests
{
    [Test]
    public async Task Constructor_NothingSet_NoActionIsSet()
    {
        // Arrange (nothing to arrange)

        // Act
        var item = new TransactWriteItem();

        // Assert
        await Assert.That((item.ConditionCheck, item.Put, item.Delete, item.Update)).IsEqualTo(((TransactConditionCheck?)null, (TransactPut?)null, (TransactDelete?)null, (TransactUpdate?)null));
    }
}
