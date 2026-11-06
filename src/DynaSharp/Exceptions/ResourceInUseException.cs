using System.Net;

namespace DynaSharp;

/// <summary>
/// The table or index is being created, updated or deleted, or already exists. Not retried: wait for the table with <see cref="Table{T}.WaitUntilActiveAsync(CancellationToken)"/>,
/// or let <see cref="CreateTableCommand.IfNotExists"/> take an existing table as success.
/// </summary>
/// <remarks><see cref="DynamoDbException.IsTransient"/> is <see langword="false"/>, although the state passes: a change of a table takes minutes, and polling is the way to wait for it.</remarks>
public sealed class ResourceInUseException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceInUseException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ResourceInUseException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ResourceInUseException", statusCode, requestId)
    {
    }
}
