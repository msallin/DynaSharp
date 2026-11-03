using System.Text;
using DynaSharp.Keys;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Keys;

/// <summary>The pooled fallback of <see cref="KeyWriter"/>: it rents only when the stack buffer is too small and returns every array.</summary>
public sealed class KeyWriterPoolTests
{
    private const string Name = "Name";

    [Test]
    public async Task Write_ValueLongerThanTheBuffer_RentsOnceAndReturnsTheArrayOnDispose()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        var (text, outstandingBeforeDispose) = WriteAndCountOutstanding(pool, 8, new string('a', 100));

        // Assert
        await Assert.That((text.Length, outstandingBeforeDispose, pool.Outstanding, pool.Returned, pool.RentedLengths.Count))
            .IsEqualTo((100, 1, 0, 1, 1));
    }

    [Test]
    public async Task Write_GrowthTwice_ReturnsTheFirstArrayWhenItIsOutgrown()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        var text = Text(
            pool,
            4,
            (ref KeyWriter writer) =>
            {
                writer.WriteLiteral("0123456789"u8);
                writer.WriteLiteral(Encoding.ASCII.GetBytes(new string('x', 100)));
            });

        // Assert
        await Assert.That((text.Length, pool.RentedLengths.Count, pool.Returned, pool.Outstanding)).IsEqualTo((110, 2, 2, 0));
    }

    [Test]
    public async Task Write_GrowthKeepsEverythingWrittenSoFar()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        var text = Text(
            pool,
            4,
            (ref KeyWriter writer) =>
            {
                writer.WriteLiteral("ab"u8);
                writer.WriteString("cdefghij", default, Name);
                writer.WriteGuid(Guid.Empty);
                writer.WriteDigits(7, 3, Name);
            });

        // Assert
        await Assert.That(text).IsEqualTo("abcdefghij00000000-0000-0000-0000-000000000000007");
    }

    [Test]
    public async Task Write_ValueThatFits_NeverTouchesThePool()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        _ = Text(pool, 64, (ref KeyWriter writer) => writer.WriteString("small", default, Name));

        // Assert
        await Assert.That((pool.RentedLengths.Count, pool.Returned)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Dispose_CalledTwice_ReturnsTheArrayOnce()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        var act = () => DisposeTwice(pool);

        // Assert
        await Assert.That(act).ThrowsNothing();
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((1, 0));
    }

    [Test]
    public async Task Dispose_AfterWrites_EmptiesTheWriter()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        var length = LengthAfterDispose(pool);

        // Assert
        await Assert.That(length).IsEqualTo(0);
    }

    [Test]
    public async Task Write_DefaultWriter_GrowsFromTheSharedPool()
    {
        // Arrange
        // Act
        var text = WriteWithDefaultWriter();

        // Assert
        await Assert.That(text).IsEqualTo("USER#1");
    }

    [Test]
    public async Task Constructor_PublicOverload_WritesIntoTheBufferWithoutRenting()
    {
        // Arrange
        // Act
        var text = WriteWithPublicConstructor();

        // Assert
        await Assert.That(text).IsEqualTo("USER#1");
    }

    [Test]
    public async Task Write_ValuesThatFit_AllocateNothing()
    {
        // Arrange (a first pass compiles everything the second pass measures)
        WriteEverything();

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            WriteEverything();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        await Assert.That(allocated).IsEqualTo(0);
    }

    private static void WriteEverything()
    {
        var writer = new KeyWriter(stackalloc byte[512]);
        try
        {
            writer.WriteLiteral("USER#"u8);
            writer.WriteString("Zurich", "#"u8, Name);
            writer.WriteUtf8Value("Paid"u8, "#"u8, Name);
            writer.WriteGuid(Guid.Empty);
            writer.WriteInteger(-42L, "#"u8, Name);
            writer.WriteInteger(42UL, "#"u8, Name);
            writer.WriteDigits(42L, 8, Name);
            writer.WriteDigits(42UL, 8, Name);
            writer.WriteDecimal(12.50m, "#"u8, Name);
            writer.WriteFixedPoint(-12.5m, 4, 2, Name);
            writer.WriteFixedPoint(12.5m, 4, 2, Name);
            writer.WriteTimestamp(DateTimeOffset.UnixEpoch, KeyTimeFormat.Ticks);
            writer.WriteTimestamp(DateTime.UnixEpoch, KeyTimeFormat.Millisecond, Name);
            writer.WriteDate(new DateOnly(2026, 10, 31));
            writer.WriteFormattable(1234, "#"u8, Name);
            writer.WriteUpperBound();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static string Text(CountingArrayPool<byte> pool, int bufferLength, KeyWriterAction action) =>
        Encoding.UTF8.GetString(KeyVectorRunner.Write(action, bufferLength, pool));

    private static (string Text, int Outstanding) WriteAndCountOutstanding(CountingArrayPool<byte> pool, int bufferLength, string value)
    {
        var writer = new KeyWriter(stackalloc byte[bufferLength], pool);
        try
        {
            writer.WriteString(value, default, Name);
            return (writer.ToString(), pool.Outstanding);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void DisposeTwice(CountingArrayPool<byte> pool)
    {
        var writer = new KeyWriter(stackalloc byte[2], pool);
        writer.WriteString("long enough to rent", default, Name);
        writer.Dispose();
        writer.Dispose();
    }

    private static int LengthAfterDispose(CountingArrayPool<byte> pool)
    {
        var writer = new KeyWriter(stackalloc byte[2], pool);
        writer.WriteString("long enough to rent", default, Name);
        writer.Dispose();
        return writer.WrittenSpan.Length;
    }

    private static string WriteWithDefaultWriter()
    {
        var writer = default(KeyWriter);
        try
        {
            writer.WriteLiteral("USER#"u8);
            writer.WriteString("1", default, Name);
            return writer.ToString();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static string WriteWithPublicConstructor()
    {
        var writer = new KeyWriter(stackalloc byte[16]);
        try
        {
            writer.WriteLiteral("USER#"u8);
            writer.WriteString("1", default, Name);
            return writer.ToString();
        }
        finally
        {
            writer.Dispose();
        }
    }
}
