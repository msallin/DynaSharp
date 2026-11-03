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

    // Error codes as DynamoDB sends them, which is also what ErrorCode reports. InternalServerError and RequestLimitExceeded have no "Exception" suffix on the wire.
    // When adding an exception type: add its factory here, with the names of the call, its wire code to Retries/FailureClassifier.cs if it is retried (or a case of IsTransient there
    // if it passes later without a retry), a row to the tests of both and to the transience cases of DynamoDbExceptionTests, and the type to the list in the remarks of
    // DynamoDbClientOptions.MaxAttempts.
    private static readonly Dictionary<string, Func<ErrorBody, DynamoDbException>> Factories = new(StringComparer.Ordinal)
    {
        ["AccessDeniedException"] = static body => new AccessDeniedException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ConditionalCheckFailedException"] = static body => new ConditionalCheckFailedException(body.Message, body.Item, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["IdempotentParameterMismatchException"] = static body => new IdempotentParameterMismatchException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ExpiredIteratorException"] = static body => new ExpiredIteratorException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["InternalServerError"] = static body => new InternalServerErrorException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ItemCollectionSizeLimitExceededException"] = static body => new ItemCollectionSizeLimitExceededException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["LimitExceededException"] = static body => new LimitExceededException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ProvisionedThroughputExceededException"] = static body => new ProvisionedThroughputExceededException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ReplicatedWriteConflictException"] = static body => new ReplicatedWriteConflictException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["RequestLimitExceeded"] = static body => new RequestLimitExceededException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ResourceInUseException"] = static body => new ResourceInUseException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ResourceNotFoundException"] = static body => new ResourceNotFoundException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ThrottlingException"] = static body => new ThrottlingException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["TransactionCanceledException"] = static body => new TransactionCanceledException(body.Message, body.CancellationReasons, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["TransactionConflictException"] = static body => new TransactionConflictException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["TransactionInProgressException"] = static body => new TransactionInProgressException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["TrimmedDataAccessException"] = static body => new TrimmedDataAccessException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["UnrecognizedClientException"] = static body => new UnrecognizedClientException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
        ["ValidationException"] = static body => new ValidationException(body.Message, body.RequestId, body.StatusCode) { OperationName = body.OperationName, TableName = body.TableName },
    };

    /// <summary>Maps an error response.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestId">The value of the <c>x-amzn-RequestId</c> header, if any.</param>
    /// <param name="body">The response body.</param>
    /// <param name="operationName">The operation that failed, if known.</param>
    /// <param name="tableName">The table of the failed call, if it has a single one.</param>
    /// <returns>The exception to throw. Never <see langword="null"/>.</returns>
    public static DynamoDbException Map(HttpStatusCode statusCode, string? requestId, ReadOnlySpan<byte> body, string? operationName = null, string? tableName = null)
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

        return Factories.TryGetValue(code, out var create)
            ? create(new ErrorBody(message, requestId, statusCode, item, cancellationReasons, operationName, tableName))
            : new DynamoDbException(message, code, statusCode, requestId) { OperationName = operationName, TableName = tableName };
    }

    /// <summary>Creates the exception for a response that is not a DynamoDB document.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="requestId">The value of the <c>x-amzn-RequestId</c> header, if any.</param>
    /// <param name="innerException">Why the body could not be understood, if parsing failed.</param>
    /// <param name="message">A message found in the body, if any. Otherwise a message that names the status code is used.</param>
    /// <param name="operationName">The operation that failed, if known.</param>
    /// <param name="tableName">The table of the failed call, if it has a single one.</param>
    /// <returns>A <see cref="DynamoDbException"/> with the error code <see cref="UnrecognizedResponseCode"/>.</returns>
    public static DynamoDbException Unrecognized(HttpStatusCode statusCode, string? requestId, Exception? innerException, string? message = null, string? operationName = null, string? tableName = null) =>
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

    // "ValidationException:http://internal.amazon.com/coral/com.amazon.coral.validate/" and
    // "com.amazon.coral.validate#ValidationException" both name ValidationException.
    private static string ExtractCode(string? type)
    {
        var code = type.AsSpan();
        var colon = code.IndexOf(':');
        if (colon >= 0)
        {
            code = code[..colon];
        }

        return code[(code.LastIndexOf('#') + 1)..].ToString();
    }
}
