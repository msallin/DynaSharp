using System.Net;

namespace DynaSharp;

/// <summary>
/// DynamoDB throttled the request: the table or an index is over its provisioned read or write capacity. The client has retried it with backoff, so this reaches the caller when the table
/// stayed throttled.
/// </summary>
/// <remarks>
/// <see cref="DynamoDbException.IsTransient"/> is <see langword="true"/>: wait and try again, raise the capacity, or switch the table to on-demand. <see cref="DynamoDbException.TableName"/>
/// names the table. A hot partition throttles a table that is under its capacity as a whole; <c>ReturnCapacity()</c> shows what a call consumes.
/// </remarks>
public sealed class ProvisionedThroughputExceededException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProvisionedThroughputExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ProvisionedThroughputExceededException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ProvisionedThroughputExceededException", statusCode, requestId)
    {
    }
}
