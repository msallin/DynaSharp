namespace DynaSharp.UnitTests.Exceptions;

public sealed class ConditionalCheckFailedExceptionTests
{
    [Test]
    public async Task Constructor_ItemGiven_SetsItem()
    {
        // Arrange
        var item = new Item();

        // Act
        var exception = new ConditionalCheckFailedException("The conditional request failed.", item);

        // Assert
        await Assert.That(exception.Item).IsSameReferenceAs(item);
    }

    [Test]
    public async Task Constructor_NoItem_ItemIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new ConditionalCheckFailedException("The conditional request failed.");

        // Assert
        await Assert.That(exception.Item).IsNull();
    }

    [Test]
    public async Task Constructor_RequestIdGiven_SetsRequestId()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new ConditionalCheckFailedException("The conditional request failed.", null, "A1B2C3");

        // Assert
        await Assert.That(exception.RequestId).IsEqualTo("A1B2C3");
    }

    [Test]
    public async Task Constructor_ExceptionNotFromThePipeline_HasNoKey()
    {
        // Arrange (nothing to arrange)

        // Act
        var exception = new ConditionalCheckFailedException("The conditional request failed.", new Item { ["pk"] = "a" });

        // Assert
        await Assert.That(exception.Key).IsNull();
    }

    [Test]
    public async Task Describe_Key_SetsItAndLeavesTheTextAlone()
    {
        // Arrange
        var exception = new ConditionalCheckFailedException("The conditional request failed.");
        var key = new Item { ["pk"] = "USER#alice" };

        // Act
        exception.Describe(key);

        // Assert
        await Assert.That((exception.Key, exception.Message, exception.ToString().Contains("alice", StringComparison.Ordinal))).IsEqualTo((key, "The conditional request failed.", false));
    }
}
