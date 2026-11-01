using System.Net;

namespace DynaSharp;

/// <summary>
/// The request rate exceeds the provisioned throughput of the table or an index. Retried automatically before it surfaces.
/// </summary>
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
