using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class PooledByteBufferAppendTests
{
    [Test]
    public async Task Append_Bytes_GrowsAsNeeded()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool, 4);

        // Act
        buffer.Append("abc"u8);
        buffer.Append((byte)'d');
        buffer.Append("efgh"u8);

        // Assert
        await Assert.That(Encoding.ASCII.GetString(buffer.WrittenSpan)).IsEqualTo("abcdefgh");
    }

    [Test]
    public async Task Append_FirstWrite_RentsTheFirstLength()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool, 32);

        // Act
        buffer.Append((byte)'a');

        // Assert
        await Assert.That(pool.RentedLengths.Single()).IsEqualTo(32);
    }

    [Test]
    public async Task AppendAscii_Text_WritesOneBytePerCharacter()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(CountingPool(), 2);

        // Act
        buffer.AppendAscii("text");

        // Assert
        await Assert.That(Encoding.ASCII.GetString(buffer.WrittenSpan)).IsEqualTo("text");
    }

    [Test]
    [Arguments(0, "0")]
    [Arguments(9, "9")]
    [Arguments(10, "10")]
    [Arguments(int.MaxValue, "2147483647")]
    public async Task AppendNumber_Value_WritesItsDigits(int value, string expected)
    {
        // Arrange
        using var buffer = new PooledByteBuffer(CountingPool(), 2);

        // Act
        buffer.AppendNumber(value);

        // Assert
        await Assert.That(Encoding.ASCII.GetString(buffer.WrittenSpan)).IsEqualTo(expected);
    }

    [Test]
    public async Task Prepend_Byte_MovesWhatIsThere()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(CountingPool(), 2);
        buffer.Append("bc"u8);

        // Act
        buffer.Prepend((byte)'a');

        // Assert
        await Assert.That(Encoding.ASCII.GetString(buffer.WrittenSpan)).IsEqualTo("abc");
    }

    [Test]
    public async Task Truncate_Position_DropsWhatCameAfter()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(CountingPool(), 2);
        buffer.Append("abc"u8);

        // Act
        buffer.Truncate(1);

        // Assert
        await Assert.That(Encoding.ASCII.GetString(buffer.WrittenSpan)).IsEqualTo("a");
    }

    [Test]
    public async Task Clear_SmallArray_KeepsIt()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool, 8);
        buffer.Append("abc"u8);

        // Act
        buffer.Clear(maxRetainedLength: 8);

        // Assert
        await Assert.That((buffer.WrittenCount, pool.Returned)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Clear_LargeArray_GivesItBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool, 16);
        buffer.Append("abc"u8);

        // Act
        buffer.Clear(maxRetainedLength: 8);

        // Assert
        await Assert.That((buffer.WrittenCount, pool.Returned)).IsEqualTo((0, 1));
    }

    private static CountingArrayPool<byte> CountingPool() => new();
}
