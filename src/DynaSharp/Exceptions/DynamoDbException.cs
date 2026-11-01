using System.Net;

namespace DynaSharp;

/// <summary>
/// An error returned by DynamoDB. Specific errors have derived types; all others surface as this type with their
/// <see cref="ErrorCode"/>.
/// </summary>
public class DynamoDbException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DynamoDbException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="errorCode">The DynamoDB error code without namespace, for example <c>ValidationException</c>.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    /// <param name="requestId">The AWS request id from the <c>x-amzn-RequestId</c> header, for support cases.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public DynamoDbException(string message, string errorCode, HttpStatusCode statusCode, string? requestId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>Gets the DynamoDB error code without namespace, for example <c>ValidationException</c>.</summary>
    public string ErrorCode { get; }

    /// <summary>Gets the HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Gets the AWS request id, or <see langword="null"/> if the response had none.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// Gets the DynamoDB operation that failed, for example <c>PutItem</c>. Set by the client on every error that DynamoDB (or a
    /// proxy answering for it) returns; <see langword="null"/> on an exception made elsewhere. The message stays the one DynamoDB
    /// sent, which often does not say what was called.
    /// </summary>
    public string? OperationName { get; internal init; }

    /// <summary>
    /// Gets the table the failed call addressed, exactly as the request named it (a name or an ARN). <see langword="null"/> when
    /// the call has no single table: a batch or transaction (which may span several), <c>ListTables</c>, or a
    /// <c>ListStreams</c> without a table. Never a key or a value.
    /// </summary>
    public string? TableName { get; internal init; }

    /// <inheritdoc/>
    /// <remarks>
    /// The text of <see cref="Exception.ToString"/>, followed by a line with <see cref="OperationName"/> and <see cref="TableName"/>
    /// when they are known.
    /// </remarks>
    public override string ToString()
    {
        var text = base.ToString();
        if (OperationName is null)
        {
            return text;
        }

        return TableName is null
            ? $"{text}{Environment.NewLine}Operation: {OperationName}"
            : $"{text}{Environment.NewLine}Operation: {OperationName}, table: {TableName}";
    }
}
