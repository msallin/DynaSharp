using System.Net;
using System.Text;
using DynaSharp.Http;

namespace DynaSharp.Testing.Fake;

/// <summary>What the fake answers to one request: the status, the JSON body and the request id that goes into <c>x-amzn-RequestId</c>.</summary>
/// <param name="StatusCode">200, or the status of the error.</param>
/// <param name="Body">The UTF-8 JSON body.</param>
/// <param name="RequestId">A new id per answer.</param>
internal readonly record struct FakeAnswer(HttpStatusCode StatusCode, ReadOnlyMemory<byte> Body, string RequestId)
{
    /// <summary>Makes the answer a response of a <see cref="StubHandler"/>, one that fits any operation, because the fake answered the operation of the request.</summary>
    /// <returns>The response.</returns>
    public StubResponse ToStubResponse() => new()
    {
        StatusCode = StatusCode,
        Body = Encoding.UTF8.GetString(Body.Span),
        Headers = new Dictionary<string, string> { [WireProtocol.RequestIdHeaderName] = RequestId },
    };
}
