using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>
/// A command on the pipeline: its body is the one in the state, and the state goes back when the call is over. The pipeline calls
/// <see cref="Release"/> exactly once, also when the body cannot be finished, and <see cref="ClientCore"/> does when the client is disposed, so
/// that is the only way a sent command gets back to the pool.
/// </summary>
/// <typeparam name="TOperation">The operation.</typeparam>
/// <typeparam name="TResponse">What the call returns.</typeparam>
/// <param name="state">The state of the command.</param>
/// <param name="version">The version of the state, which belongs to this call from here on.</param>
internal readonly struct CommandCall<TOperation, TResponse>(CommandState state, int version) : IWireCall<TResponse>
    where TOperation : ICommandOperation<TResponse>
{
    public string OperationName => TOperation.Name;

    public string ServiceName => TelemetryNames.RpcServiceValue;

    public string SpanName => Names.SpanName;

    public string Target => Names.Target;

    public int MaxAttempts => state.MaxAttempts;

    public string? GetTableName() => state.Table.Name;

    public ArraySegment<byte> WriteBody(CallBuffers buffers) => state.Finish();

    public TResponse ReadResponse(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new DynamoDbReader(utf8Json);
        return TOperation.ReadResponse(ref reader, state);
    }

    public void SetActivityTags(Activity activity) => TOperation.SetActivityTags(activity, state);

    public void SetResponseTags(Activity activity, TResponse response) => TOperation.SetResponseTags(activity, response);

    public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TResponse response) => ConsumedCapacityTelemetry.ToList(state.ResponseCapacity);

    public void DescribeFailure(DynamoDbException failure)
    {
        switch (failure)
        {
            case ValidationException rejection:
                RejectedRequest.Describe(rejection, state.Body.WrittenSegment);
                break;
            case ConditionalCheckFailedException condition:
                FailedCondition.Describe(condition, state.Body.WrittenSegment, state.Table.ItemKeyNames);
                FailedCondition.Explain(condition, TOperation.Name, state.Table, state.Write.Conditions);
                break;
        }
    }

    public void Release() => state.Release(version);

    // Built once per operation, not per call.
    private static class Names
    {
        public static readonly string Target = WireProtocol.TargetPrefix + TOperation.Name;

        public static readonly string SpanName = TelemetryNames.RpcServiceValue + "." + TOperation.Name;
    }
}
