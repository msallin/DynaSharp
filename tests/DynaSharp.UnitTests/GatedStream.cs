namespace DynaSharp.UnitTests;

/// <summary>
/// A read-only stream whose reads complete when a gate task completes, and which captures no context while it waits. The tests that
/// look at where the library resumes need a read that is pending and then completes by itself.
/// </summary>
/// <param name="data">What the stream hands out.</param>
/// <param name="gate">The task that lets reads complete.</param>
internal sealed class GatedStream(byte[] data, Task gate) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await gate.ConfigureAwait(false);
        var count = Math.Min(buffer.Length, data.Length - _position);
        data.AsMemory(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
