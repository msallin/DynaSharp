using System.Diagnostics;

namespace DynaSharp.Http;

/// <summary>
/// A call that the library does not model: the caller names the operation and writes the request JSON, and gets the response JSON
/// back as it came. It runs through the same pipeline as every other call, so signing, the attempt timeout, retries, clock correction,
/// error mapping and telemetry are the same, and the real operation name is in the span and the metrics.
/// </summary>
/// <param name="operation">The operation name, for example <c>DescribeLimits</c>.</param>
/// <param name="requestJson">The request body.</param>
/// <param name="targetPrefix">What the <c>X-Amz-Target</c> header starts with for the service.</param>
/// <param name="serviceName">The name of the service for telemetry.</param>
internal readonly struct RawCall(string operation, ReadOnlyMemory<byte> requestJson, string targetPrefix, string serviceName) : IWireCall<byte[]>
{
    public string OperationName => operation;

    public string ServiceName => serviceName;

    // One string per call, which a call that nobody sends to the pipeline in a loop can afford; the operations of the library
    // build theirs once.
    public string SpanName => serviceName + "." + operation;

    public string Target => targetPrefix + operation;

    public int MaxAttempts => 0;

    // The table is in the JSON, which is not parsed: such a call has none in its telemetry.
    public string? GetTableName() => null;

    // A copy, because the request needs an array and the memory of the caller may be anything. The bytes are not looked at.
    public ArraySegment<byte> WriteBody(CallBuffers buffers)
    {
        var body = buffers.Request;
        requestJson.Span.CopyTo(body.GetSpan(requestJson.Length));
        body.Advance(requestJson.Length);
        return body.WrittenSegment;
    }

    // Whatever the service answered with a success status, not parsed: a response of an operation the library does not know cannot
    // be told from a wrong one.
    public byte[] ReadResponse(ReadOnlySpan<byte> utf8Json) => utf8Json.ToArray();

    public bool TryReadFailure(ReadOnlySpan<byte> utf8Json, out byte[] response)
    {
        response = null!;
        return false;
    }

    public void SetActivityTags(Activity activity)
    {
    }

    public void SetResponseTags(Activity activity, byte[] response)
    {
    }

    public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(byte[] response) => null;

    // The caller wrote the expressions, so they need no explaining, and the JSON is not parsed, so nothing here knows a key.
    public void DescribeFailure(DynamoDbException failure)
    {
    }

    public void Release()
    {
    }
}
