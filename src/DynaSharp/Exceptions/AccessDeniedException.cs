using System.Net;

namespace DynaSharp;

/// <summary>
/// The credentials are valid but lack permission for the operation. Not retried: grant the IAM action, such as <c>dynamodb:PutItem</c> on the table.
/// </summary>
/// <remarks>
/// <see cref="DynamoDbException.OperationName"/> and <see cref="DynamoDbException.TableName"/> say which action on which table the policy must allow. A table of an entity with
/// indexes needs the actions on the index ARNs too. <see cref="DynamoDbException.IsTransient"/> is <see langword="false"/>.
/// </remarks>
public sealed class AccessDeniedException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AccessDeniedException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public AccessDeniedException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "AccessDeniedException", statusCode, requestId)
    {
    }
}
