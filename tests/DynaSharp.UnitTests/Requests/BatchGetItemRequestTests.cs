using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class BatchGetItemRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ConsumedCapacityIsNotRequested()
    {
        // Arrange (nothing to arrange)

        // Act
        var request = new BatchGetItemRequest { RequestItems = new Dictionary<string, KeysAndAttributes> { ["Users"] = new() { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] } } };

        // Assert
        await Assert.That(request.ReturnConsumedCapacity).IsEqualTo(ReturnConsumedCapacity.None);
    }
}
