using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class StreamSummaryTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_TableNameIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamSummary { StreamArn = "arn" };

        // Assert
        await Assert.That(value.TableName).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamLabelIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamSummary { StreamArn = "arn" };

        // Assert
        await Assert.That(value.StreamLabel).IsNull();
    }
}
