using System.Buffers;
using System.Text.Json;
using DynaSharp.Serialization;

namespace DynaSharp.Http;

/// <summary>
/// What every call needs besides its own data, kept between calls: a buffer and a JSON writer for the request body, and a buffer for
/// the response body of the current attempt. The pipeline takes one from a small pool for each call and gives it back after the last
/// attempt, which saves a writer and two buffer objects per call.
/// </summary>
/// <remarks>
/// <para>
/// The arrays are not kept: each buffer rents one when it is written to and returns it when the call (or the attempt, for the
/// response) is over, as a buffer of its own would. So an idle object holds no memory of a call and nothing of one can reach another;
/// what is reused is the object, and a writer whose state <see cref="Utf8JsonWriter.Reset()"/> clears before each use.
/// </para>
/// <para>One call uses an object at a time. It is never shared, and it is back in the pool only when the call is over.</para>
/// </remarks>
internal sealed class CallBuffers
{
    /// <summary>Initializes a new instance of the <see cref="CallBuffers"/> class.</summary>
    /// <param name="pool">The pool of the arrays of both buffers.</param>
    public CallBuffers(ArrayPool<byte> pool)
    {
        Request = new PooledByteBuffer(pool);
        Response = new PooledByteBuffer(pool);
        Json = WireJson.CreateWriter(Request);
    }

    /// <summary>Gets the buffer for the request body. A call that holds its body elsewhere does not touch it, and it never rents an array.</summary>
    public PooledByteBuffer Request { get; }

    /// <summary>Gets the writer that writes to <see cref="Request"/>. Call <see cref="Utf8JsonWriter.Reset()"/> first and <see cref="Utf8JsonWriter.Flush"/> last.</summary>
    public Utf8JsonWriter Json { get; }

    /// <summary>Gets the buffer for the response body of the attempt that runs.</summary>
    public PooledByteBuffer Response { get; }

    /// <summary>Gives the arrays back, so that the object can serve the next call.</summary>
    public void Release()
    {
        Request.Release();
        Response.Release();
    }
}
