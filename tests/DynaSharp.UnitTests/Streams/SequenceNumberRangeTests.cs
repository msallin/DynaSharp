using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Streams;

public sealed class SequenceNumberRangeTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_StartingSequenceNumberIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SequenceNumberRange { };

        // Assert
        await Assert.That(value.StartingSequenceNumber).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EndingSequenceNumberIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new SequenceNumberRange { };

        // Assert
        await Assert.That(value.EndingSequenceNumber).IsNull();
    }
}
