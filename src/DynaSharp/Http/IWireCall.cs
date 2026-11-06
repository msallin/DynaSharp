using System.Diagnostics;

namespace DynaSharp.Http;

/// <summary>
/// One call to DynamoDB as the <see cref="DynamoDbPipeline"/> sees it: what the call is named, how its body gets on the wire and
/// how its response is read. The pipeline does the rest, with one retry loop for every kind of call: signing, the attempt timeout,
/// retries, clock correction, error mapping and telemetry.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequestCall{TOperation, TRequest, TResponse}"/> runs an <see cref="Operations.IOperation{TRequest, TResponse}"/> of the
/// request API: it serializes a request object and reads a response object. <see cref="RawCall"/> sends JSON the caller wrote and
/// returns the response JSON. The command API of the table layer is a third kind: its call holds a body that is already written, in a
/// segment of a pooled state, and reads its response itself. A batch or a transaction of that API is a fourth, whose call names several
/// tables, and a batch may be several calls over one state.
/// </para>
/// <para>
/// Implement it with a struct that holds only references and values the call needs, and pass it by value to
/// <see cref="DynamoDbPipeline.SendAsync{TCall, TResponse}"/>: the pipeline is generic over the struct, so a call allocates no object
/// for it and no delegate. A struct is copied once per attempt. Implement every member in the struct, with no default interface
/// members: calling one on a struct that does not implement it boxes the struct.
/// </para>
/// <para>
/// A call may be repeated, so everything the pipeline hands out is valid for the whole call: <see cref="WriteBody"/> runs once,
/// and its bytes are sent again for every attempt. When adding a member here, add it to each implementation: <c>RequestCall</c>, <c>RawCall</c>,
/// and <c>CommandCall</c>, <c>PageCall</c> and <c>ActionCall</c> of the command API. Per-call settings belong here too, as one more member that the loop reads, as <see cref="MaxAttempts"/> is.
/// </para>
/// </remarks>
/// <typeparam name="TResponse">What the call returns.</typeparam>
internal interface IWireCall<TResponse>
{
    /// <summary>Gets the operation name as DynamoDB knows it, for example <c>GetItem</c>: <c>db.operation.name</c>, <c>rpc.method</c> and the tag of every metric.</summary>
    public string OperationName { get; }

    /// <summary>Gets the service for telemetry (<c>rpc.service</c>, the first part of the span name).</summary>
    public string ServiceName { get; }

    /// <summary>Gets the name of the span: the service and the operation, <c>DynamoDB.GetItem</c>. Cached by calls that repeat.</summary>
    public string SpanName { get; }

    /// <summary>Gets the value of the <c>X-Amz-Target</c> header: the target prefix of the service and the operation name.</summary>
    public string Target { get; }

    /// <summary>Gets the number of attempts of this call including the first, or zero for what the client is configured with.</summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Gets the table the call addresses, for telemetry. Called once for every call, also when nobody listens, so it must not
    /// allocate more than it has to.
    /// </summary>
    /// <returns>The table name, or <see langword="null"/> when the call addresses several tables or none.</returns>
    public string? GetTableName();

    /// <summary>
    /// Provides the request body. Called once, inside the span, before the first attempt. The bytes must stay valid until
    /// <see cref="Release"/>.
    /// </summary>
    /// <param name="buffers">The buffers of the call. The body goes to <see cref="CallBuffers.Request"/> (through <see cref="CallBuffers.Json"/> for JSON); the pipeline releases them after the last attempt. A call that holds its body elsewhere leaves them alone.</param>
    /// <returns>The body: what was written to <see cref="CallBuffers.Request"/>, or a segment the call holds.</returns>
    public ArraySegment<byte> WriteBody(CallBuffers buffers);

    /// <summary>
    /// Reads the body of a successful response. Called after every attempt that answered with a success status. It may throw
    /// <see cref="System.Text.Json.JsonException"/>, <see cref="InvalidOperationException"/> or <see cref="FormatException"/> for a body
    /// that is not the response of the operation, which the pipeline reports as an unrecognized response. The bytes are valid
    /// only during the call: copy what must stay.
    /// </summary>
    /// <param name="utf8Json">The body.</param>
    /// <returns>The response.</returns>
    public TResponse ReadResponse(ReadOnlySpan<byte> utf8Json);

    /// <summary>
    /// Reads the body of an error response as a result, for the one failure a call asked to take as an answer: a failed condition of <c>TrySendAsync</c>. Called after every attempt
    /// that answered with an error status, before the error is mapped. A call that tolerates nothing returns <see langword="false"/> at once, and so does one that tolerates a
    /// failure this body is not. On <see langword="true"/> the pipeline ends the call as a success with no error on the span or the metric, and nothing retries it, which a failed
    /// condition never was. The bytes are valid only during the call: copy what must stay.
    /// </summary>
    /// <param name="utf8Json">The body of the error response.</param>
    /// <param name="response">The response the failure stands for, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the failure is an answer of the call; <see langword="false"/> to map and throw it.</returns>
    public bool TryReadFailure(ReadOnlySpan<byte> utf8Json, out TResponse response);

    /// <summary>
    /// Adds request parameters that are worth a tag on the span. Never add keys, items or attribute values. Must not throw: the
    /// pipeline starts the span before it writes the body, so a throw would leave the span open.
    /// </summary>
    /// <param name="activity">The span, with all data requested.</param>
    public void SetActivityTags(Activity activity);

    /// <summary>
    /// Adds response values that are worth a tag on the span. Called after a successful response, and only when the span records data.
    /// Must not throw, and never adds keys, items or attribute values.
    /// </summary>
    /// <param name="activity">The span, with all data requested.</param>
    /// <param name="response">The response.</param>
    public void SetResponseTags(Activity activity, TResponse response);

    /// <summary>Gets the capacity the response reports, for the span and the capacity metric. Never read when nobody listens. Must not throw.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The reported capacity, or <see langword="null"/> or an empty list if the call has none.</returns>
    public IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TResponse response);

    /// <summary>
    /// Adds to a failure of the service what only the call knows. A call that generates placeholders explains them to a <see cref="ValidationException"/>, because the message of the
    /// service names nothing else, and that never includes keys, items or attribute values. A call that knows its key puts it on a <see cref="ConditionalCheckFailedException"/>,
    /// because the response does not say which item failed, and that goes into <see cref="ConditionalCheckFailedException.Key"/> alone, never into the message or telemetry. Called once
    /// for every <see cref="DynamoDbException"/>, before it leaves the pipeline, while the bytes of the body are still valid. Must not throw.
    /// </summary>
    /// <param name="failure">The failure of the call.</param>
    public void DescribeFailure(DynamoDbException failure);

    /// <summary>
    /// Called exactly once when the call is over, after the last attempt, whatever its outcome and also when <see cref="WriteBody"/>
    /// threw. A call that holds a pooled state gives it back here, so a retry can send the same bytes and nothing can reuse them early.
    /// </summary>
    public void Release();
}
