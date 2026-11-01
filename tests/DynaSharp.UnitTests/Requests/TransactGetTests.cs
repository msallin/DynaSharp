using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactGetTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ProjectionAndNamesAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var get = new TransactGet { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } };

        // Assert
        await Assert.That((get.ProjectionExpression, get.ExpressionAttributeNames)).IsEqualTo(((string?)null, (IReadOnlyDictionary<string, string>?)null));
    }
}
