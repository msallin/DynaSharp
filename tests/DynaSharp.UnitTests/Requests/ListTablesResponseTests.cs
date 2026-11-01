using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class ListTablesResponseTests
{
    [Test]
    public async Task Constructor_NothingSet_TableNamesIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListTablesResponse();

        // Assert
        await Assert.That(value.TableNames).IsEmpty();
    }

    [Test]
    public async Task Constructor_NothingSet_LastEvaluatedTableNameIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new ListTablesResponse();

        // Assert
        await Assert.That(value.LastEvaluatedTableName).IsNull();
    }
}
