using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class StreamDescriptionTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamLabelIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.StreamLabel).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamStatusIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.StreamStatus).IsEqualTo(StreamStatus.Unknown);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamViewTypeIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.StreamViewType).IsEqualTo(StreamViewType.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_CreationRequestDateTimeIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.CreationRequestDateTime).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_TableNameIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.TableName).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_KeySchemaIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.KeySchema).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ShardsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.Shards).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_LastEvaluatedShardIdIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamDescription { StreamArn = "arn" };

        // Assert
        await Assert.That(value.LastEvaluatedShardId).IsNull();
    }
}
