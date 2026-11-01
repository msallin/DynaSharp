using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactPutTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembersOfPut_ConditionAndFailureOptionsAreUnset()
    {
        // Arrange (nothing to arrange)

        // Act
        var put = new TransactPut { TableName = "Users", Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" } };

        // Assert
        await Assert.That((put.ConditionExpression, put.ExpressionAttributeNames, put.ExpressionAttributeValues, put.ReturnValuesOnConditionCheckFailure)).IsEqualTo(((string?)null, (IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null, ReturnValuesOnConditionCheckFailure.None));
    }
}
