using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class KeysAndAttributesTests
{
    [Test]
    public async Task Constructor_OnlyKeys_ReadsAreEventuallyConsistent()
    {
        // Arrange (nothing to arrange)

        // Act
        var keys = new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] };

        // Assert
        await Assert.That(keys.ConsistentRead).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyKeys_ProjectionAndNamesAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var keys = new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }] };

        // Assert
        await Assert.That((keys.ProjectionExpression, keys.ExpressionAttributeNames)).IsEqualTo(((string?)null, (IReadOnlyDictionary<string, string>?)null));
    }

    [Test]
    public async Task Constructor_Keys_ExposesThemInOrder()
    {
        // Arrange (nothing to arrange)

        // Act
        var keys = new KeysAndAttributes { Keys = [new Dictionary<string, AttributeValue> { ["pk"] = "a" }, new Dictionary<string, AttributeValue> { ["pk"] = "b" }] };

        // Assert
        await Assert.That(keys.Keys.Select(key => key["pk"].AsString()).ToArray()).IsEquivalentTo(["a", "b"]);
    }
}
