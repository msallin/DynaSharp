using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class DynamoDbNumberTests
{
    [Test]
    public async Task TryFormat_ValueThatFits_WritesItsDigitsAsUtf8()
    {
        // Arrange
        var destination = new byte[16];

        // Act
        var formatted = DynamoDbNumber.TryFormat(-1234.5, destination, out var written);

        // Assert
        await Assert.That((formatted, Encoding.UTF8.GetString(destination, 0, written))).IsEqualTo((true, "-1234.5"));
    }

    [Test]
    public async Task TryFormat_DestinationTooSmall_ReturnsFalse()
    {
        // Arrange
        var destination = new byte[3];

        // Act
        var formatted = DynamoDbNumber.TryFormat(123456, destination, out var written);

        // Assert
        await Assert.That((formatted, written)).IsEqualTo((false, 0));
    }
}
