using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactDeleteTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembersOfDelete_ConditionAndFailureOptionsAreUnset()
    {
        // Arrange (nothing to arrange)

        // Act
        var delete = new TransactDelete { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" } };

        // Assert
        await Assert.That((delete.ConditionExpression, delete.ExpressionAttributeNames, delete.ExpressionAttributeValues, delete.ReturnValuesOnConditionCheckFailure)).IsEqualTo(((string?)null, (IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null, ReturnValuesOnConditionCheckFailure.None));
    }
}
