namespace DynaSharp.UnitTests;

public sealed class CancellationReasonTests
{
    [Test]
    public async Task Constructor_NothingSet_CodeMessageAndItemAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var reason = new CancellationReason();

        // Assert
        await Assert.That((reason.Code, reason.Message, reason.Item)).IsEqualTo(((string?)null, (string?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    public async Task Constructor_AllMembers_ExposesThem()
    {
        // Arrange (nothing to arrange)

        // Act
        var reason = new CancellationReason { Code = "ConditionalCheckFailed", Message = "The conditional request failed", Item = new Dictionary<string, AttributeValue> { ["pk"] = "a" } };

        // Assert
        await Assert.That((reason.Code, reason.Message, reason.Item!["pk"].AsString())).IsEqualTo(("ConditionalCheckFailed", "The conditional request failed", "a"));
    }
}
