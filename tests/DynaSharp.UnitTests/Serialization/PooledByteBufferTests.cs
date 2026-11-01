using System.Text;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Serialization;

public sealed class PooledByteBufferTests
{
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Constructor_CapacityNotPositive_ThrowsArgumentOutOfRangeException(int capacity)
    {
        // Arrange (nothing to arrange beyond the argument)

        // Act
        var act = () => new PooledByteBuffer(capacity);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("initialCapacity");
    }

    [Test]
    public async Task GetSpan_NewBuffer_ProvidesAtLeastInitialCapacity()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(300);

        // Act
        var length = buffer.GetSpan().Length;

        // Assert
        await Assert.That(length).IsGreaterThanOrEqualTo(300);
    }

    [Test]
    public async Task WrittenSpan_AfterAdvance_ContainsWrittenBytes()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();

        // Act
        Write(buffer, "abc");

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo("abc");
    }

    [Test]
    public async Task WrittenSegment_AfterAdvance_CoversWrittenBytesFromStart()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        Write(buffer, "abc");

        // Act
        var segment = buffer.WrittenSegment;

        // Assert
        await Assert.That(Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count)).IsEqualTo("abc");
    }

    [Test]
    public async Task WrittenCount_AfterTwoAdvances_ReturnsSum()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();

        // Act
        buffer.Advance(3);
        buffer.Advance(4);

        // Assert
        await Assert.That(buffer.WrittenCount).IsEqualTo(7);
    }

    [Test]
    public async Task Advance_Negative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();

        // Act
        var act = () => buffer.Advance(-1);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("count");
    }

    [Test]
    public async Task Advance_BeyondFreeSpace_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        var free = buffer.GetSpan().Length;

        // Act
        var act = () => buffer.Advance(free + 1);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("count");
    }

    [Test]
    public async Task Advance_BeyondFreeSpaceAfterWriting_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        buffer.Advance(10);
        var free = buffer.GetSpan().Length;

        // Act
        var act = () => buffer.Advance(free + 1);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("count");
    }

    [Test]
    public async Task Advance_ExactlyFreeSpace_Succeeds()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        var free = buffer.GetSpan().Length;

        // Act
        buffer.Advance(free);

        // Assert
        await Assert.That(buffer.WrittenCount).IsEqualTo(free);
    }

    [Test]
    public async Task GetSpan_FullBuffer_ReturnsNonEmptySpan()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        buffer.Advance(buffer.GetSpan().Length);

        // Act
        var length = buffer.GetSpan().Length;

        // Assert
        await Assert.That(length).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task GetSpan_HintLargerThanFreeSpace_GrowsToFitHint()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        buffer.Advance(buffer.GetSpan().Length);

        // Act
        var length = buffer.GetSpan(5000).Length;

        // Assert
        await Assert.That(length).IsGreaterThanOrEqualTo(5000);
    }

    [Test]
    public async Task GetSpan_HintFitsFreeSpace_DoesNotGrow()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        var free = buffer.GetSpan().Length;

        // Act
        var length = buffer.GetSpan(free).Length;

        // Assert
        await Assert.That(length).IsEqualTo(free);
    }

    [Test]
    public async Task GetSpan_Growing_KeepsWrittenBytes()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        Write(buffer, new string('a', 200));

        // Act
        Write(buffer, new string('b', 200));

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo(new string('a', 200) + new string('b', 200));
    }

    [Test]
    public async Task GetMemory_HintLargerThanFreeSpace_GrowsAndKeepsWrittenBytes()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        Write(buffer, "abc");

        // Act
        var memory = buffer.GetMemory(1000);
        "def"u8.CopyTo(memory.Span);
        buffer.Advance(3);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo("abcdef");
    }

    [Test]
    public async Task GetMemory_NewBuffer_ProvidesAtLeastHint()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);

        // Act
        var length = buffer.GetMemory(100).Length;

        // Assert
        await Assert.That(length).IsGreaterThanOrEqualTo(100);
    }

    [Test]
    public async Task GetSpan_NegativeHint_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();

        // Act
        var act = () => buffer.GetSpan(-1).Length;

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("sizeHint");
    }

    [Test]
    public async Task GetSpan_HintOverflowingTotalSize_ThrowsOverflowException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        buffer.Advance(1);

        // Act
        var act = () => buffer.GetSpan(int.MaxValue).Length;

        // Assert
        await Assert.That(act).ThrowsExactly<OverflowException>();
    }

    [Test]
    public async Task Dispose_WrittenBytes_ResetsWrittenCount()
    {
        // Arrange
        var buffer = new PooledByteBuffer();
        Write(buffer, "abc");

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(buffer.WrittenCount).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var buffer = new PooledByteBuffer();
        buffer.Dispose();

        // Act
        var act = buffer.Dispose;

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task GetSpan_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = new PooledByteBuffer();
        buffer.Dispose();

        // Act
        var act = () => buffer.GetSpan().Length;

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadFromAsync_KnownLength_AppendsExactlyThatManyBytes(bool asynchronous)
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        Write(buffer, "head:");
        var data = Payload(10_000);
        using var stream = new ChunkedStream(data, 1000, asynchronous);

        // Act
        await buffer.ReadFromAsync(stream, data.Length, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo("head:" + Encoding.UTF8.GetString(data));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ReadFromAsync_ReadCompletesLater_DoesNotResumeOnTheContextOfTheCaller(bool knownLength)
    {
        // Arrange (the stream has nothing yet when the buffer reads, so the read is the first await that suspends)
        var dataReady = new TaskCompletionSource();
        var data = Payload(100);
        using var buffer = new PooledByteBuffer(256);
        using var stream = new GatedStream(data, dataReady.Task);
        var context = new CountingSynchronizationContext();
        var read = context.Start(() => buffer.ReadFromAsync(stream, knownLength ? data.Length : null, int.MaxValue, CancellationToken.None).AsTask());

        // Act
        dataReady.SetResult();
        await read;

        // Assert
        await Assert.That((context.Posts, buffer.WrittenCount)).IsEqualTo((0, data.Length));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadFromAsync_UnknownLength_AppendsUntilEndOfStream(bool asynchronous)
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        Write(buffer, "head:");
        var data = Payload(10_000);
        using var stream = new ChunkedStream(data, 1000, asynchronous);

        // Act
        await buffer.ReadFromAsync(stream, null, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo("head:" + Encoding.UTF8.GetString(data));
    }

    [Test]
    public async Task ReadFromAsync_EndlessStreamOfUnknownLength_GivesUpOneByteAfterTheLimit()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        using var stream = new EndlessStream();

        // Act
        var completed = await buffer.ReadFromAsync(stream, null, 10_000, CancellationToken.None);

        // Assert
        await Assert.That((completed, buffer.WrittenCount, stream.BytesServed)).IsEqualTo((false, 10_001, 10_001L));
    }

    [Test]
    public async Task ReadFromAsync_UnknownLengthExactlyTheLimit_Completes()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        using var stream = new ChunkedStream(Payload(10_000), 1000, true);

        // Act
        var completed = await buffer.ReadFromAsync(stream, null, 10_000, CancellationToken.None);

        // Assert
        await Assert.That((completed, buffer.WrittenCount)).IsEqualTo((true, 10_000));
    }

    [Test]
    public async Task ReadFromAsync_UnknownLengthOneByteOverTheLimit_ReturnsFalse()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        using var stream = new ChunkedStream(Payload(10_001), 1000, true);

        // Act
        var completed = await buffer.ReadFromAsync(stream, null, 10_000, CancellationToken.None);

        // Assert
        await Assert.That(completed).IsFalse();
    }

    [Test]
    public async Task ReadFromAsync_UnknownLengthAfterEarlierData_CountsTheEarlierData()
    {
        // Arrange (the limit is for the whole buffer)
        using var buffer = new PooledByteBuffer(256);
        Write(buffer, "head:");
        using var stream = new ChunkedStream(Payload(100), 1000, false);

        // Act
        var completed = await buffer.ReadFromAsync(stream, null, 100, CancellationToken.None);

        // Assert
        await Assert.That(completed).IsFalse();
    }

    [Test]
    public async Task ReadFromAsync_KnownLengthAboveTheLimit_ReturnsFalseWithoutReading()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        using var stream = new EndlessStream();

        // Act
        var completed = await buffer.ReadFromAsync(stream, 10_001, 10_000, CancellationToken.None);

        // Assert
        await Assert.That((completed, buffer.WrittenCount, stream.BytesServed)).IsEqualTo((false, 0, 0L));
    }

    [Test]
    public async Task ReadFromAsync_KnownLengthExactlyTheLimit_Completes()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256);
        using var stream = new ChunkedStream(Payload(10_000), 1000, false);

        // Act
        var completed = await buffer.ReadFromAsync(stream, 10_000, 10_000, CancellationToken.None);

        // Assert
        await Assert.That((completed, buffer.WrittenCount)).IsEqualTo((true, 10_000));
    }

    [Test]
    public async Task ReadFromAsync_UnknownLengthFromMemoryStream_ReadsEverything()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        var data = Payload(100_000);
        using var stream = new MemoryStream(data);

        // Act
        await buffer.ReadFromAsync(stream, null, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(buffer.WrittenCount).IsEqualTo(100_000);
    }

    [Test]
    public async Task ReadFromAsync_KnownLengthZero_AppendsNothing()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var stream = new MemoryStream();

        // Act
        await buffer.ReadFromAsync(stream, 0, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(buffer.WrittenCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReadFromAsync_KnownLengthShorterThanStream_ReadsOnlyThatLength()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var stream = new MemoryStream("abcdef"u8.ToArray());

        // Act
        await buffer.ReadFromAsync(stream, 4, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo("abcd");
    }

    [Test]
    public async Task ReadFromAsync_StreamShorterThanKnownLength_ThrowsEndOfStreamException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var stream = new MemoryStream("abc"u8.ToArray());

        // Act
        var act = async () => await buffer.ReadFromAsync(stream, 4, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<EndOfStreamException>();
    }

    [Test]
    public async Task ReadFromAsync_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var stream = new MemoryStream();

        // Act
        var act = async () => await buffer.ReadFromAsync(stream, -1, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("length");
    }

    [Test]
    public async Task ReadFromAsync_LengthBeyondMaximumBufferSizeAndLimit_ReturnsFalse()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var stream = new MemoryStream();

        // Act
        var complete = await buffer.ReadFromAsync(stream, 3_000_000_000, 1000, CancellationToken.None);

        // Assert
        await Assert.That(complete).IsFalse();
    }

    [Test]
    public async Task ReadFromAsync_LengthBeyondMaximumBufferSize_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        buffer.Advance(1);
        using var stream = new MemoryStream();

        // Act
        var act = async () => await buffer.ReadFromAsync(stream, Array.MaxLength, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("length");
    }

    [Test]
    [Arguments(10L)]
    [Arguments(null)]
    public async Task ReadFromAsync_Canceled_ThrowsOperationCanceledException(long? length)
    {
        // Arrange
        using var buffer = new PooledByteBuffer();
        using var stream = new ChunkedStream(Payload(10), 10, yieldOnRead: true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var act = async () => await buffer.ReadFromAsync(stream, length, int.MaxValue, cancellation.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Constructor_Always_RentsInitialCapacityFromPool()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        using var buffer = new PooledByteBuffer(300, pool);

        // Assert
        await Assert.That(string.Join(',', pool.RentedLengths)).IsEqualTo("300");
    }

    [Test]
    public async Task GetSpan_FullBuffer_DoublesCapacity()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256, new CountingArrayPool<byte>());
        buffer.Advance(256);

        // Act
        var free = buffer.GetSpan(1).Length;

        // Assert
        await Assert.That(free).IsEqualTo(256);
    }

    [Test]
    public async Task GetSpan_HintBeyondDoubling_GrowsToWrittenPlusHint()
    {
        // Arrange
        using var buffer = new PooledByteBuffer(256, new CountingArrayPool<byte>());
        buffer.Advance(200);

        // Act
        var free = buffer.GetSpan(1000).Length;

        // Assert
        await Assert.That(free).IsEqualTo(1000);
    }

    [Test]
    public async Task Dispose_AfterGrowing_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffer = new PooledByteBuffer(256, pool);
        for (var i = 0; i < 5; i++)
        {
            Write(buffer, new string('a', 300));
        }

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_CalledTwice_ReturnsArrayOnce()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffer = new PooledByteBuffer(256, pool);
        buffer.Dispose();

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(pool.Returned).IsEqualTo(1);
    }

    [Test]
    public async Task ReadFromAsync_KnownLength_GrowsAtMostOnce()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(256, pool);
        using var stream = new ChunkedStream(Payload(100_000), 1000, yieldOnRead: false);

        // Act
        await buffer.ReadFromAsync(stream, 100_000, int.MaxValue, CancellationToken.None);

        // Assert
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadFromAsync_UnknownLength_ReturnsEveryRentedArrayOnDispose(bool asynchronous)
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffer = new PooledByteBuffer(256, pool);
        using var stream = new ChunkedStream(Payload(100_000), 1000, asynchronous);
        await buffer.ReadFromAsync(stream, null, int.MaxValue, CancellationToken.None);

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_AfterStreamEndedEarly_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffer = new PooledByteBuffer(256, pool);
        using var stream = new ChunkedStream(Payload(1000), 100, yieldOnRead: true);
        try
        {
            await buffer.ReadFromAsync(stream, 5000, int.MaxValue, CancellationToken.None);
        }
        catch (EndOfStreamException)
        {
            // Expected: the stream is shorter than announced.
        }

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_AfterCanceledRead_ReturnsEveryRentedArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffer = new PooledByteBuffer(256, pool);
        using var stream = new ChunkedStream(Payload(10_000), 100, yieldOnRead: true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        try
        {
            await buffer.ReadFromAsync(stream, null, int.MaxValue, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: the read was canceled.
        }

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_PoolOnly_RentsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        using var buffer = new PooledByteBuffer(pool);

        // Assert
        await Assert.That(pool.RentedLengths).IsEmpty();
    }

    [Test]
    public async Task GetSpan_BufferWithoutArray_RentsTheDefaultCapacity()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);

        // Act
        buffer.GetSpan(10);

        // Assert
        await Assert.That(pool.RentedLengths).IsEquivalentTo([PooledByteBuffer.DefaultInitialCapacity]);
    }

    [Test]
    public async Task GetSpan_BufferWithoutArrayAndRequestAboveTheDefault_RentsWhatIsRequested()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);

        // Act
        buffer.GetSpan(10_000);

        // Assert
        await Assert.That(pool.RentedLengths).IsEquivalentTo([10_000]);
    }

    [Test]
    public async Task Reserve_BufferWithoutArray_RentsExactlyThatMuch()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);

        // Act
        buffer.Reserve(100);

        // Assert
        await Assert.That(pool.RentedLengths).IsEquivalentTo([100]);
    }

    [Test]
    public async Task Reserve_ArrayLargeEnough_RentsNothingMore()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);
        buffer.Reserve(100);

        // Act
        buffer.Reserve(100);

        // Assert
        await Assert.That(pool.RentedLengths).IsEquivalentTo([100]);
    }

    [Test]
    public async Task Reserve_ArrayTooSmall_KeepsWhatWasWritten()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);
        buffer.Reserve(8);
        Write(buffer, "head");

        // Act
        buffer.Reserve(100);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffer.WrittenSpan)).IsEqualTo("head");
    }

    [Test]
    public async Task Reserve_ArrayTooSmall_ReturnsTheSmallArray()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);
        buffer.Reserve(8);

        // Act
        buffer.Reserve(100);

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned)).IsEqualTo((1, 1));
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Reserve_CapacityNotPositive_ThrowsArgumentOutOfRangeException(int capacity)
    {
        // Arrange
        using var buffer = new PooledByteBuffer();

        // Act
        var act = () => buffer.Reserve(capacity);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentOutOfRangeException>().WithParameterName("capacity");
    }

    [Test]
    public async Task Reserve_DisposedBuffer_ThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = new PooledByteBuffer();
        buffer.Dispose();

        // Act
        var act = () => buffer.Reserve(10);

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    [Test]
    public async Task Release_BufferWithData_ReturnsTheArrayAndEmptiesTheBuffer()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(10, pool);
        Write(buffer, "abc");

        // Act
        buffer.Release();

        // Assert
        await Assert.That((pool.Outstanding, buffer.WrittenCount)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task Release_BufferWithoutArray_ReturnsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);

        // Act
        buffer.Release();

        // Assert
        await Assert.That(pool.Returned).IsEqualTo(0);
    }

    [Test]
    public async Task Release_ThenWrite_RentsANewArrayAndHoldsOnlyTheNewData()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var buffer = new PooledByteBuffer(pool);
        Write(buffer, "first");
        buffer.Release();

        // Act
        Write(buffer, "second");

        // Assert
        await Assert.That((Encoding.UTF8.GetString(buffer.WrittenSpan), pool.RentedLengths.Count)).IsEqualTo(("second", 2));
    }

    [Test]
    public async Task Dispose_AfterRelease_ReturnsNothingAgain()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffer = new PooledByteBuffer(10, pool);
        buffer.Release();

        // Act
        buffer.Dispose();

        // Assert
        await Assert.That(pool.Returned).IsEqualTo(1);
    }

    [Test]
    public async Task GetSpan_AfterReleaseAndDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = new PooledByteBuffer(10);
        buffer.Release();
        buffer.Dispose();

        // Act
        var act = () => buffer.GetSpan().Length;

        // Assert
        await Assert.That(act).ThrowsExactly<ObjectDisposedException>();
    }

    private static void Write(PooledByteBuffer buffer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        bytes.CopyTo(buffer.GetSpan(bytes.Length));
        buffer.Advance(bytes.Length);
    }

    private static byte[] Payload(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)('a' + (i % 26));
        }

        return data;
    }
}
