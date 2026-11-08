using System.Buffers;
using System.Globalization;
using System.Text;

namespace DynaSharp.Serialization;

/// <summary>
/// A contiguous, growable byte buffer rented from an <see cref="ArrayPool{T}"/> (by default the shared one), for request
/// and response bodies.
/// </summary>
/// <remarks>
/// The buffer grows by doubling. <see cref="Dispose"/> returns the array to the pool; afterwards the written data is
/// empty and asking for more space throws <see cref="ObjectDisposedException"/>. A buffer that is used for one call after the
/// other (see <see cref="PooledByteBuffer(ArrayPool{byte}, int)"/>) gives its array back with <see cref="Release"/> after each, and
/// takes a new one when it is written to again. Not thread-safe.
/// </remarks>
internal sealed class PooledByteBuffer : IBufferWriter<byte>, IDisposable
{
    internal const int DefaultInitialCapacity = 4096;

    // Reads of unknown length ask for at least this much free space, so a nearly full buffer does not cause tiny reads.
    private const int MinimumReadSize = 4096;

    // int.MaxValue has ten digits, and a sign would be the eleventh.
    private const int MaxInt32Length = 11;

    private readonly ArrayPool<byte> _pool;
    private readonly int _initialCapacity;
    private byte[] _buffer;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PooledByteBuffer"/> class on the shared pool.</summary>
    /// <param name="initialCapacity">The minimum initial capacity in bytes.</param>
    public PooledByteBuffer(int initialCapacity = DefaultInitialCapacity)
        : this(initialCapacity, ArrayPool<byte>.Shared)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PooledByteBuffer"/> class.</summary>
    /// <param name="initialCapacity">The minimum initial capacity in bytes.</param>
    /// <param name="pool">The pool to rent arrays from and return them to.</param>
    public PooledByteBuffer(int initialCapacity, ArrayPool<byte> pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _pool = pool;
        _initialCapacity = initialCapacity;
        _buffer = pool.Rent(initialCapacity);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledByteBuffer"/> class that holds no array yet, for an object that serves
    /// one call after the other: the first write rents an array of <see cref="DefaultInitialCapacity"/> (or <see cref="Reserve"/>
    /// rents the size it is given), and <see cref="Release"/> gives it back.
    /// </summary>
    /// <param name="pool">The pool to rent arrays from and return them to.</param>
    /// <param name="firstRentLength">The length of the first array, for a buffer that usually stays small.</param>
    public PooledByteBuffer(ArrayPool<byte> pool, int firstRentLength = DefaultInitialCapacity)
    {
        _pool = pool;
        _initialCapacity = firstRentLength;
        _buffer = [];
    }

    /// <summary>Gets the number of bytes written.</summary>
    public int WrittenCount { get; private set; }

    /// <summary>Gets the written bytes. Valid until the buffer grows or is disposed.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    /// <summary>Gets the written bytes as an array segment, for example for <c>ByteArrayContent</c>.</summary>
    public ArraySegment<byte> WrittenSegment => new(_buffer, 0, WrittenCount);

    /// <inheritdoc/>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - WrittenCount);
        WrittenCount += count;
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureFree(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureFree(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    /// <summary>Makes sure that at least <paramref name="capacity"/> more bytes fit, renting exactly that much if the buffer holds no array.</summary>
    /// <param name="capacity">The number of bytes that will be written.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    /// <exception cref="ObjectDisposedException">The buffer is disposed.</exception>
    public void Reserve(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_buffer.Length - WrittenCount < capacity)
        {
            Grow(capacity);
        }
    }

    /// <summary>
    /// Returns the array to the pool and empties the buffer, which can be written to again: unlike <see cref="Dispose"/> it does not
    /// end the life of the object. Calling it on a buffer that holds no array has no effect.
    /// </summary>
    public void Release()
    {
        if (_buffer.Length > 0)
        {
            _pool.Return(_buffer);
            _buffer = [];
        }

        WrittenCount = 0;
    }

    /// <summary>
    /// Empties the buffer for the next use and keeps its array, which saves the round trip to the pool, unless the array has
    /// grown beyond what one idle object should hold on to. The bytes stay in the array, as they do in a pool.
    /// </summary>
    /// <param name="maxRetainedLength">The longest array the buffer keeps.</param>
    public void Clear(int maxRetainedLength)
    {
        if (_buffer.Length > maxRetainedLength)
        {
            Release();
        }

        WrittenCount = 0;
    }

    /// <summary>Drops what was written after a position, which is how a candidate that turned out to be a duplicate is taken back.</summary>
    /// <param name="length">The number of bytes to keep. It is a position this buffer reached before, so it is not checked.</param>
    public void Truncate(int length) => WrittenCount = length;

    /// <summary>Takes the first and the last byte off what follows an offset, moving the rest down.</summary>
    /// <remarks>
    /// With the offset of the parenthesis in <c>x AND (a OR b)</c> that leaves <c>x AND a OR b</c>.
    /// </remarks>
    /// <param name="offset">The offset of the first byte. The last one is the end of what was written.</param>
    public void Unwrap(int offset)
    {
        _buffer.AsSpan(offset + 1, WrittenCount - offset - 2).CopyTo(_buffer.AsSpan(offset));
        WrittenCount -= 2;
    }

    /// <summary>Appends one byte.</summary>
    /// <param name="value">The byte.</param>
    public void Append(byte value)
    {
        EnsureRoom(1);
        _buffer[WrittenCount++] = value;
    }

    /// <summary>Appends bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    public void Append(scoped ReadOnlySpan<byte> bytes)
    {
        EnsureRoom(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(WrittenCount));
        WrittenCount += bytes.Length;
    }

    /// <summary>Appends text that the caller knows to be ASCII, one byte per character.</summary>
    /// <param name="text">The text.</param>
    public void AppendAscii(scoped ReadOnlySpan<char> text)
    {
        EnsureRoom(text.Length);
        Ascii.FromUtf16(text, _buffer.AsSpan(WrittenCount), out var written);
        WrittenCount += written;
    }

    /// <summary>Appends a non-negative integer as decimal digits.</summary>
    /// <param name="value">The integer.</param>
    public void AppendNumber(int value)
    {
        // The numbers of placeholders: most are single digits, and formatting one costs more than everything around it.
        if (value < 10)
        {
            Append((byte)('0' + value));
            return;
        }

        EnsureRoom(MaxInt32Length);
        value.TryFormat(_buffer.AsSpan(WrittenCount), out var written, default, CultureInfo.InvariantCulture);
        WrittenCount += written;
    }

    /// <summary>Puts one byte in front of what was written, which moves all of it.</summary>
    /// <param name="value">The byte.</param>
    public void Prepend(byte value)
    {
        EnsureRoom(1);
        var written = _buffer.AsSpan(0, WrittenCount);
        written.CopyTo(_buffer.AsSpan(1));
        _buffer[0] = value;
        WrittenCount++;
    }

    /// <summary>Appends the rest of a stream, for example a response body.</summary>
    /// <param name="stream">The stream to read to its end.</param>
    /// <param name="length">
    /// The number of bytes the stream holds, if known (for example from <c>Content-Length</c>). The buffer then grows
    /// at most once and exactly that many bytes are read, unless that is more than <paramref name="maxLength"/>, which ends the call at
    /// once without reading; <see langword="null"/> reads until the stream ends.
    /// </param>
    /// <param name="maxLength">
    /// The most bytes the buffer may hold afterwards. A stream that is longer is read for one byte more than that and then
    /// abandoned, so a stream that never ends cannot fill the memory.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// <see langword="true"/> when the data has been read; <see langword="false"/> when it is longer than <paramref name="maxLength"/>,
    /// in which case the buffer holds part of it and should be disposed.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative, or within <paramref name="maxLength"/> but larger than a buffer can be.</exception>
    /// <exception cref="EndOfStreamException">The stream ends before <paramref name="length"/> bytes.</exception>
    public async ValueTask<bool> ReadFromAsync(Stream stream, long? length, int maxLength, CancellationToken cancellationToken)
    {
        if (length is { } exact)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(exact, nameof(length));
            if (exact > maxLength - WrittenCount)
            {
                return false;
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(exact, Array.MaxLength - WrittenCount, nameof(length));
            var count = (int)exact;
            await stream.ReadExactlyAsync(GetMemory(count)[..count], cancellationToken).ConfigureAwait(false);
            WrittenCount += count;
            return true;
        }

        while (true)
        {
            // At most one byte more than the limit, which is all it takes to see that the limit is exceeded.
            var free = GetMemory(MinimumReadSize);
            var room = (int)Math.Min(free.Length, (long)maxLength - WrittenCount + 1);
            var read = await stream.ReadAsync(free[..room], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return true;
            }

            WrittenCount += read;
            if (WrittenCount > maxLength)
            {
                return false;
            }
        }
    }

    /// <summary>Returns the array to the pool. Calling it again has no effect.</summary>
    public void Dispose()
    {
        // Release gives the array back once, so a second call finds none.
        // Not cleared: pool contents stay inside the process, which holds the same data as objects, and clearing costs on every call.
        _disposed = true;
        Release();
    }

    private void EnsureFree(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureRoom(Math.Max(sizeHint, 1));
    }

    private void EnsureRoom(int required)
    {
        if (_buffer.Length - WrittenCount >= required)
        {
            return;
        }

        // A disposed buffer has no space left, so this check costs nothing on the fast path.
        ObjectDisposedException.ThrowIf(_disposed, this);
        Grow(_buffer.Length == 0 ? Math.Max(required, _initialCapacity) : required);
    }

    private void Grow(int required)
    {
        var larger = _pool.Rent(Math.Max(checked(WrittenCount + required), _buffer.Length * 2));
        WrittenSpan.CopyTo(larger);
        if (_buffer.Length > 0)
        {
            _pool.Return(_buffer);
        }

        _buffer = larger;
    }
}
