using System.Net;

namespace DynaSharp;

/// <summary>
/// The table or index does not exist, or is not yet <c>ACTIVE</c>. Not retried: check the name and the region, or wait for a new table with
/// <see cref="Table{T}.WaitUntilActiveAsync(CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <see cref="DynamoDbException.TableName"/> says which table was asked for. A table that was just created can be reported as missing for a moment, which the wait bears with.
/// <see cref="DynamoDbException.IsTransient"/> is <see langword="false"/>.
/// </remarks>
public sealed class ResourceNotFoundException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ResourceNotFoundException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ResourceNotFoundException", statusCode, requestId)
    {
    }
}
