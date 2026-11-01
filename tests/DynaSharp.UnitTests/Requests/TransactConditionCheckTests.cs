using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TransactConditionCheckTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembersOfConditionCheck_OptionsAreUnset()
    {
        // Arrange (nothing to arrange)

        // Act
        var check = new TransactConditionCheck { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "a" }, ConditionExpression = "attribute_exists(pk)" };

        // Assert
        await Assert.That((check.ExpressionAttributeNames, check.ExpressionAttributeValues, check.ReturnValuesOnConditionCheckFailure)).IsEqualTo(((IReadOnlyDictionary<string, string>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null, ReturnValuesOnConditionCheckFailure.None));
    }
}
