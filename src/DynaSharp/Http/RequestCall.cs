using System.Diagnostics;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Http;

/// <summary>
/// A call of the request API: an <see cref="IOperation{TRequest, TResponse}"/> and its request object. Every member forwards to the
/// operation, so the operation stays what it was before the pipeline knew other kinds of call.
/// </summary>
/// <typeparam name="TOperation">The operation.</typeparam>
/// <typeparam name="TRequest">The request type of the operation.</typeparam>
/// <typeparam name="TResponse">The response type of the operation.</typeparam>
/// <param name="request">The request.</param>
internal readonly struct RequestCall<TOperation, TRequest, TResponse>(TRequest request) : IWireCall<TResponse>
    where TOperation : IOperation<TRequest, TResponse>
{
    public string OperationName => TOperation.Name;

    public string ServiceName => TOperation.ServiceName;

    public string SpanName => Names.SpanName;

    public string Target => Names.Target;

    public int MaxAttempts => 0;

    public string? GetTableName() => TOperation.GetTableName(request);

    public ArraySegment<byte> WriteBody(CallBuffers buffers)
    {
        // Reset first: the writer served other calls, and one of them may have failed in the middle of its body.
        var json = buffers.Json;
        json.Reset();
        TOperation.WriteRequest(new DynamoDbWriter(json), request);
        json.Flush();
        return buffers.Request.WrittenSegment;
    }

    public TResponse ReadResponse(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        return TOperation.ReadResponse(ref reader);
    }

    public bool TryReadFailure(ReadOnlySpan<byte> utf8Json, out TResponse response)
    {
        response = default!;
        return false;
    }

    public void SetActivityTags(Activity activity) => TOperation.SetActivityTags(activity, request);

    public void SetResponseTags(Activity activity, TResponse response) => TOperation.SetResponseTags(activity, response);

    public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TResponse response) => TOperation.GetConsumedCapacity(response);

    // The caller wrote the expressions, so they need no explaining. A failed condition gets the key the request holds.
    public void DescribeFailure(DynamoDbException failure)
    {
        if (failure is ConditionalCheckFailedException condition && TOperation.GetKey(request) is { } key)
        {
            condition.Describe(key);
        }
    }

    public void Release()
    {
    }

    // Built once per operation, not per call.
    private static class Names
    {
        public static readonly string Target = TOperation.TargetPrefix + TOperation.Name;

        public static readonly string SpanName = TOperation.ServiceName + "." + TOperation.Name;
    }
}
