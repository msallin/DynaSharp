using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>
/// One request of a batch or a transaction on the pipeline. It has the tags of the same request of the request API, so telemetry does not tell the
/// two apart: one span per request, with the tables of that request.
/// </summary>
/// <typeparam name="TOperation">The operation.</typeparam>
/// <typeparam name="TResponse">What the call returns.</typeparam>
/// <param name="state">The state of the command.</param>
/// <param name="version">The version of the state.</param>
internal readonly struct ActionCall<TOperation, TResponse>(CommandState state, int version) : IWireCall<TResponse>
    where TOperation : IActionOperation<TResponse>
{
    public string OperationName => TOperation.Name;

    public string ServiceName => TelemetryNames.RpcServiceValue;

    public string SpanName => Names.SpanName;

    public string Target => Names.Target;

    public int MaxAttempts => state.MaxAttempts;

    public string? GetTableName()
    {
        var scan = default(TableScan);
        state.Actions.AddTables(ref scan);
        return scan.Only;
    }

    public ArraySegment<byte> WriteBody(CallBuffers buffers)
    {
        var body = TOperation.WriteBody(state, buffers);
        state.Actions.SentBody = body;
        return body;
    }

    public TResponse ReadResponse(ReadOnlySpan<byte> utf8Json) => TOperation.ReadResponse(utf8Json, state);

    public void SetActivityTags(Activity activity)
    {
        var scan = TableScan.Collecting();
        state.Actions.AddTables(ref scan);
        scan.SetTag(activity);
    }

    public void SetResponseTags(Activity activity, TResponse response)
    {
    }

    public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TResponse response) => TOperation.GetConsumedCapacity(response);

    public void DescribeRejection(ValidationException rejection) => RejectedRequest.Describe(rejection, state.Actions.SentBody);

    public void Release()
    {
        if (TOperation.EndsCommand)
        {
            state.Release(version);
        }
    }

    // Built once per operation, not per call.
    private static class Names
    {
        public static readonly string Target = WireProtocol.TargetPrefix + TOperation.Name;

        public static readonly string SpanName = TelemetryNames.RpcServiceValue + "." + TOperation.Name;
    }
}
