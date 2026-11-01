using System.Diagnostics;
using DynaSharp.Http;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Operations;

/// <summary>
/// One DynamoDB API operation: what it is called on the wire, how its request is written and how its response is read.
/// </summary>
/// <remarks>
/// Implemented by internal structs without state. The pipeline is generic over the implementation, so calling an operation
/// needs no delegates or closures, and the JIT (or the AOT compiler) specializes the pipeline per operation.
/// </remarks>
/// <typeparam name="TRequest">The request type of the public API.</typeparam>
/// <typeparam name="TResponse">The response type of the public API.</typeparam>
internal interface IOperation<TRequest, TResponse>
{
    /// <summary>Gets the operation name as DynamoDB knows it, for example <c>GetItem</c>.</summary>
    public static abstract string Name { get; }

    /// <summary>
    /// Gets what the <c>X-Amz-Target</c> header starts with, followed by <see cref="Name"/>. The default is that of DynamoDB;
    /// the operations of DynamoDB Streams override it.
    /// </summary>
    public static virtual string TargetPrefix => WireProtocol.TargetPrefix;

    /// <summary>
    /// Gets the name of the service for telemetry (<c>rpc.service</c>, the first part of the span name), as the AWS SDKs report it.
    /// The default is DynamoDB; the operations of DynamoDB Streams override it.
    /// </summary>
    public static virtual string ServiceName => TelemetryNames.RpcServiceValue;

    /// <summary>
    /// Gets the table the request addresses, for telemetry. Called once for every call, also when nobody listens, so it must not
    /// allocate more than it has to.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>
    /// The table name, or <see langword="null"/> when the request addresses several tables or none. <see cref="OperationTelemetry"/>
    /// then leaves <c>db.collection.name</c> and <c>aws.dynamodb.table_names</c> to the operation (see <see cref="SetActivityTags"/>).
    /// </returns>
    public static abstract string? GetTableName(TRequest request);

    /// <summary>Writes the request as the JSON object DynamoDB expects as the request body.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="request">The request.</param>
    public static abstract void WriteRequest(DynamoDbWriter writer, TRequest request);

    /// <summary>Reads the JSON object DynamoDB returns as the body of a successful response.</summary>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <returns>The response.</returns>
    public static abstract TResponse ReadResponse(ref DynamoDbReader reader);

    /// <summary>
    /// Adds request parameters that are worth a tag on the operation's span. Never add keys, items or attribute values. Must not
    /// throw: the pipeline starts the span before it serializes the request, so a throw would leave the span open.
    /// </summary>
    /// <param name="activity">The span, with all data requested.</param>
    /// <param name="request">The request.</param>
    public static virtual void SetActivityTags(Activity activity, TRequest request)
    {
    }

    /// <summary>
    /// Adds response values that are worth a tag on the operation's span, for example the number of items returned. Called after
    /// a successful response, and only when the span records data, so it costs nothing when nobody listens. It must never throw
    /// and never add keys, items or attribute values. The consumed capacity is tagged by <see cref="OperationTelemetry"/>, through
    /// <see cref="GetConsumedCapacity"/>; leave it out here.
    /// </summary>
    /// <param name="activity">The span, with all data requested.</param>
    /// <param name="response">The response.</param>
    public static virtual void SetResponseTags(Activity activity, TResponse response)
    {
    }

    /// <summary>
    /// Gets the capacity the response reports, which feeds the <c>aws.dynamodb.consumed_capacity</c> span attribute and the
    /// <c>dynasharp.client.consumed_capacity</c> metric. Operations that address several tables return one entry per table.
    /// Never read when nobody listens. It must never throw.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The reported capacity, or <see langword="null"/> or an empty list if the request did not ask for it or the operation has none.</returns>
    public static virtual IReadOnlyList<ConsumedCapacity>? GetConsumedCapacity(TResponse response) => null;
}
