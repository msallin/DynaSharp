using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class StreamRecordTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ApproximateCreationDateTimeIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.ApproximateCreationDateTime).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_KeysIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.Keys).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_NewImageIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.NewImage).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_OldImageIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.OldImage).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SequenceNumberIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.SequenceNumber).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SizeBytesIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.SizeBytes).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamViewTypeIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new StreamRecord { };

        // Assert
        await Assert.That(value.StreamViewType).IsEqualTo(StreamViewType.Unspecified);
    }
}
