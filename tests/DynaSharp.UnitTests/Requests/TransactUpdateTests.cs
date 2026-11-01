using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactUpdateTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembersOfUpdate_ConditionAndFailureOptionsAreUnset()
    {
        // Arrange (nothing to arrange)

        // Act
        var update = new TransactUpdate { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, UpdateExpression = "SET a = :a" };

        // Assert
        await Assert.That((update.ConditionExpression, update.ExpressionAttributeNames, update.ExpressionAttributeValues, update.ReturnValuesOnConditionCheckFailure)).IsEqualTo(((string?)null, (IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null, ReturnValuesOnConditionCheckFailure.None));
    }
}
