using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>
/// One page of a read on the pipeline, with its span, its metrics and its own retries. Unlike a <see cref="CommandCall{TOperation, TResponse}"/> it does not own
/// the state: the state lives for all pages, so whoever follows them releases it, once, however the read ends.
/// </summary>
/// <typeparam name="TOperation">The operation.</typeparam>
/// <typeparam name="T">How the items are read.</typeparam>
/// <param name="state">The state of the read.</param>
internal readonly struct PageCall<TOperation, T>(CommandState state) : IWireCall<PageData<T>>
    where TOperation : ICommandOperation<PageData<T>>
{
    public string OperationName => TOperation.Name;

    public string ServiceName => TelemetryNames.RpcServiceValue;

    public string SpanName => Names.SpanName;

    public string Target => Names.Target;

    public int MaxAttempts => state.MaxAttempts;

    public string? GetTableName() => state.Table.Name;

    public ArraySegment<byte> WriteBody(CallBuffers buffers) => state.WritePageBody();

    // The key replaces the one before only after the whole response was read, and the last page has none to replace it with. The body is another buffer, so
    // whatever happens here, a retry of this page sends what the first attempt sent.
    public PageData<T> ReadResponse(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        var page = TOperation.ReadResponse(ref reader, state);
        var key = state.PageKey;
        key.Truncate(0);
        key.Append(utf8Json.Slice(page.KeyOffset, page.KeyLength));
        return page;
    }

    public bool TryReadFailure(ReadOnlySpan<byte> utf8Json, out PageData<T> response)
    {
        response = default!;
        return false;
    }

    public void SetActivityTags(Activity activity) => TOperation.SetActivityTags(activity, state);

    public void SetResponseTags(Activity activity, PageData<T> response) => TOperation.SetResponseTags(activity, response);

    public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(PageData<T> response) => ConsumedCapacityTelemetry.ToList(state.ResponseCapacity);

    public void DescribeFailure(DynamoDbException failure)
    {
        if (failure is ValidationException rejection)
        {
            RejectedRequest.Describe(rejection, state.Body.WrittenSegment);
        }
    }

    public void Release()
    {
    }

    // Built once per operation, not per call.
    private static class Names
    {
        public static readonly string Target = WireProtocol.TargetPrefix + TOperation.Name;

        public static readonly string SpanName = TelemetryNames.RpcServiceValue + "." + TOperation.Name;
    }
}
