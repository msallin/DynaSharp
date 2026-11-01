using System.Text;
using DynaSharp.Http;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Http;

public sealed class CallBuffersTests
{
    [Test]
    public async Task Constructor_Pool_RentsNothing()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();

        // Act
        _ = new CallBuffers(pool);

        // Assert
        await Assert.That(pool.RentedLengths).IsEmpty();
    }

    [Test]
    public async Task Json_AfterFlush_HasWrittenToTheRequestBuffer()
    {
        // Arrange
        var buffers = new CallBuffers(new CountingArrayPool<byte>());
        buffers.Json.Reset();

        // Act
        buffers.Json.WriteStartObject();
        buffers.Json.WriteEndObject();
        await buffers.Json.FlushAsync();

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffers.Request.WrittenSpan)).IsEqualTo("{}");
    }

    [Test]
    public async Task Release_RequestAndResponseWritten_ReturnsBothArrays()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        var buffers = new CallBuffers(pool);
        buffers.Request.GetSpan(10);
        buffers.Response.GetSpan(10);

        // Act
        buffers.Release();

        // Assert
        await Assert.That((pool.Returned, pool.Outstanding)).IsEqualTo((2, 0));
    }

    [Test]
    public async Task Json_AfterACallFailedInTheMiddleOfItsBody_WritesTheNextBodyClean()
    {
        // Arrange (a call that failed leaves a half-written object in the writer and bytes in the buffer)
        var buffers = new CallBuffers(new CountingArrayPool<byte>());
        buffers.Json.Reset();
        buffers.Json.WriteStartObject();
        buffers.Json.WritePropertyName("unfinished");
        buffers.Release();

        // Act
        buffers.Json.Reset();
        buffers.Json.WriteStartObject();
        buffers.Json.WriteEndObject();
        await buffers.Json.FlushAsync();

        // Assert
        await Assert.That(Encoding.UTF8.GetString(buffers.Request.WrittenSpan)).IsEqualTo("{}");
    }
}
