namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// A read-only stream that never ends: every read fills the whole buffer it is given. It counts the bytes it handed out, so a
/// test can see where a reader gave up without allocating what a real endless body would.
/// </summary>
internal sealed class EndlessStream : Stream
{
    public long BytesServed { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        buffer.Fill((byte)'x');
        BytesServed += buffer.Length;
        return buffer.Length;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
