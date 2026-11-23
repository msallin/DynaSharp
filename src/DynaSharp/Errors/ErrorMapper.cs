using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Errors;

/// <summary>
/// Turns the body of a failed DynamoDB response into the matching exception.
/// </summary>
/// <remarks>
/// DynamoDB reports errors as <c>{"__type":"com.amazonaws.dynamodb.v20120810#ResourceNotFoundException","message":"..."}</c>.
/// The error code is the part of <c>__type</c> after the <c>#</c>. The message is in <c>message</c> or, for some errors,
/// <c>Message</c>.
/// </remarks>
internal static class ErrorMapper
{
    /// <summary>
    /// The <see cref="DynamoDbException.ErrorCode"/> of a response that is not a DynamoDB document: an empty body, a body that is
    /// not JSON (for example the HTML of a proxy answering 503), JSON without <c>__type</c>, or a successful response that cannot be parsed.
    /// </summary>
    public const string UnrecognizedResponseCode = "UnrecognizedResponse";

    /// <summary>Maps an error response.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestId">The value of the <c>x-amzn-RequestId</c> header, if any.</param>
    /// <param name="body">The response body.</param>
    /// <param name="operationName">The operation that failed.</param>
    /// <param name="tableName">The table of the failed call, if it has a single one.</param>
    /// <returns>The exception to throw. Never <see langword="null"/>.</returns>
    public static DynamoDbException Map(HttpStatusCode statusCode, string? requestId, ReadOnlySpan<byte> body, string operationName, string? tableName)
    {
        string? type = null;
        string? message = null;
        Item? item = null;
        ReadOnlyCollection<CancellationReason>? cancellationReasons = null;
        try
        {
            var reader = new DynamoDbReader(body);
            reader.ReadStartItem();
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("__type"u8))
                {
                    type = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("message"u8) || reader.AttributeNameEquals("Message"u8))
                {
                    message = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("Item"u8))
                {
                    item = AttributeValueConverter.ReadItem(ref reader);
                }
                else if (reader.AttributeNameEquals("CancellationReasons"u8))
                {
                    cancellationReasons = ReadCancellationReasons(ref reader);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Unrecognized(statusCode, requestId, exception, null, operationName, tableName);
        }

        var code = ExtractCode(type);
        if (code.Length == 0)
        {
            return Unrecognized(statusCode, requestId, null, message, operationName, tableName);
        }

        // An empty message is as useless as none: DynamoDB Local sends one for IdempotentParameterMismatchException.
        if (string.IsNullOrEmpty(message))
        {
            message = $"DynamoDB returned the error {code} (HTTP {(int)statusCode}) without a message.";
        }

        // Error codes as DynamoDB sends them, which is also what ErrorCode reports. InternalServerError and RequestLimitExceeded have no "Exception" suffix on the wire.
        // When adding an exception type: add it here, its wire code to Retries/FailureClassifier.cs if it is retried (or a case of IsTransient there if it passes later
        // without a retry), a row to the tests of both and to the transience cases of DynamoDbExceptionTests, and the type to the list in the remarks of
        // DynamoDbClientOptions.MaxAttempts.
        DynamoDbException failure = code switch
        {
            "AccessDeniedException" => new AccessDeniedException(message, requestId, statusCode),
            "ConditionalCheckFailedException" => new ConditionalCheckFailedException(message, item, requestId, statusCode),
            "IdempotentParameterMismatchException" => new IdempotentParameterMismatchException(message, requestId, statusCode),
            "ExpiredIteratorException" => new ExpiredIteratorException(message, requestId, statusCode),
            "InternalServerError" => new InternalServerErrorException(message, requestId, statusCode),
            "ItemCollectionSizeLimitExceededException" => new ItemCollectionSizeLimitExceededException(message, requestId, statusCode),
            "LimitExceededException" => new LimitExceededException(message, requestId, statusCode),
            "ProvisionedThroughputExceededException" => new ProvisionedThroughputExceededException(message, requestId, statusCode),
            "ReplicatedWriteConflictException" => new ReplicatedWriteConflictException(message, requestId, statusCode),
            "RequestLimitExceeded" => new RequestLimitExceededException(message, requestId, statusCode),
            "ResourceInUseException" => new ResourceInUseException(message, requestId, statusCode),
            "ResourceNotFoundException" => new ResourceNotFoundException(message, requestId, statusCode),
            "ThrottlingException" => new ThrottlingException(message, requestId, statusCode),
            "TransactionCanceledException" => new TransactionCanceledException(message, cancellationReasons, requestId, statusCode),
            "TransactionConflictException" => new TransactionConflictException(message, requestId, statusCode),
            "TransactionInProgressException" => new TransactionInProgressException(message, requestId, statusCode),
            "TrimmedDataAccessException" => new TrimmedDataAccessException(message, requestId, statusCode),
            "UnrecognizedClientException" => new UnrecognizedClientException(message, requestId, statusCode),
            "ValidationException" => new ValidationException(message, requestId, statusCode),
            _ => new DynamoDbException(message, code, statusCode, requestId),
        };

        failure.OperationName = operationName;
        failure.TableName = tableName;
        return failure;
    }

    /// <summary>Creates the exception for a response that is not a DynamoDB document.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestId">The value of the <c>x-amzn-RequestId</c> header, if any.</param>
    /// <param name="innerException">Why the body could not be understood, if parsing failed.</param>
    /// <param name="message">A message found in the body, if any. Otherwise a message that names the status code is used.</param>
    /// <param name="operationName">The operation that failed.</param>
    /// <param name="tableName">The table of the failed call, if it has a single one.</param>
    /// <returns>A <see cref="DynamoDbException"/> with the error code <see cref="UnrecognizedResponseCode"/>.</returns>
    public static DynamoDbException Unrecognized(HttpStatusCode statusCode, string? requestId, Exception? innerException, string? message, string operationName, string? tableName) =>
        new(
            message ?? $"DynamoDB returned HTTP {(int)statusCode} ({statusCode}) with a body that is not a DynamoDB response.",
            UnrecognizedResponseCode,
            statusCode,
            requestId,
            innerException)
        {
            OperationName = operationName,
            TableName = tableName,
        };

    /// <summary>
    /// Reads the error code out of <c>__type</c>: <c>ValidationException:http://internal.amazon.com/coral/com.amazon.coral.validate/</c> and
    /// <c>com.amazon.coral.validate#ValidationException</c> both name <c>ValidationException</c>.
    /// </summary>
    /// <param name="type">The value of <c>__type</c>, if the body has one.</param>
    /// <returns>The code, or an empty string.</returns>
    internal static string ExtractCode(string? type)
    {
        var code = type.AsSpan();
        var colon = code.IndexOf(':');
        if (colon >= 0)
        {
            code = code[..colon];
        }

        return code[(code.LastIndexOf('#') + 1)..].ToString();
    }

    // "CancellationReasons":[{"Code":"ConditionalCheckFailed","Message":"The conditional request failed","Item":{...}},{"Code":"None"}]
    // The entries keep their order: the reason at an index belongs to the action at that index of the request.
    private static ReadOnlyCollection<CancellationReason> ReadCancellationReasons(ref DynamoDbReader reader)
    {
        var reasons = new List<CancellationReason>();
        OperationJson.ReadStartArray(ref reader);
        while (reader.TryReadStartItem())
        {
            string? code = null;
            string? message = null;
            IReadOnlyDictionary<string, AttributeValue>? item = null;
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("Code"u8))
                {
                    code = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("Message"u8))
                {
                    message = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("Item"u8))
                {
                    item = AttributeValueConverter.ReadItem(ref reader);
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }

            reasons.Add(new CancellationReason { Code = code, Message = message, Item = item });
        }

        return reasons.AsReadOnly();
    }
}
