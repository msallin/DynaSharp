using System.Net;

namespace DynaSharp;

/// <summary>
/// The access key id or session token is invalid or expired. Not retried: check the credentials the client was given, see <see cref="DynamoDbClientOptions.Credentials"/>.
/// </summary>
/// <remarks>
/// An expired token of a provider that can refresh it, such as <see cref="Credentials.ContainerCredentialsProvider"/>, is replaced and the request resent once before this surfaces, as
/// <see cref="DynamoDbClientOptions.MaxAttempts"/> describes. <see cref="DynamoDbException.IsTransient"/> is <see langword="false"/>.
/// </remarks>
public sealed class UnrecognizedClientException : DynamoDbException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnrecognizedClientException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public UnrecognizedClientException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "UnrecognizedClientException", statusCode, requestId)
    {
    }
}
