using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class ListTablesRequestTests
{
    [Test]
    public async Task Constructor_NothingSet_PagingMembersAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListTablesRequest();

        // Assert
        await Assert.That((value.ExclusiveStartTableName, value.Limit)).IsEqualTo(((string?)null, (int?)null));
    }
}
