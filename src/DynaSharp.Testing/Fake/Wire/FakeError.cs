using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using DynaSharp.Errors;
using DynaSharp.Serialization;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A failure of a request to the fake, as DynamoDB reports it: a code, an HTTP status, a message, and for some codes more of the body. The wire renders it as
/// <c>{"__type":"com.amazonaws.dynamodb.v20120810#ValidationException","message":"..."}</c>, and the direct API of <see cref="FakeDynamoDb"/> throws what the client would make of that body.
/// </summary>
/// <remarks>
/// <para>
/// Everything the fake does not implement inside an operation it does is <see cref="Unsupported"/>, with its own namespace, <c>DynaSharp.Testing#UnsupportedByFakeException</c>, so
/// that <see cref="DynamoDbException.ErrorCode"/> reads <c>UnsupportedByFakeException</c> and no test mistakes it for an answer of DynamoDB. An operation it does not implement at all is
/// <see cref="UnknownOperation"/>, what DynamoDB Local sends.
/// </para>
/// <para>
/// When adding a factory: the code DynamoDB sends, HTTP 400 unless DynamoDB sends another status, and a test that renders it.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "It never leaves the fake: the wire renders it, and the direct API throws the exception of the client.")]
internal sealed class FakeError : Exception
{
    /// <summary>The namespace of every code DynamoDB sends.</summary>
    public const string DynamoDbNamespace = "com.amazonaws.dynamodb.v20120810#";

    /// <summary>The code of what the fake does not implement inside an operation it implements.</summary>
    public const string UnsupportedCode = "UnsupportedByFakeException";

    /// <summary>Where the docs say what the fake does, which every unsupported message names.</summary>
    public const string ScopeLink = "https://msallin.github.io/DynaSharp/testing.html#what-the-fake-does";

    private const string ConditionFailedMessage = "The conditional request failed";

    private FakeError(string type, string message, HttpStatusCode status = HttpStatusCode.BadRequest, Action<Utf8JsonWriter>? extraBody = null)
        : base(message)
    {
        Type = type;
        Status = status;
        ExtraBody = extraBody;
    }

    /// <summary>Gets the error code, the part of <see cref="Type"/> after the <c>#</c>.</summary>
    public string Code => Type[(Type.AsSpan().LastIndexOf('#') + 1)..];

    /// <summary>Gets the value of <c>__type</c>.</summary>
    public string Type { get; }

    /// <summary>Gets the HTTP status.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>Gets what the body holds beyond <c>__type</c> and <c>message</c>, or <see langword="null"/>.</summary>
    public Action<Utf8JsonWriter>? ExtraBody { get; }

    /// <summary>A request DynamoDB rejects.</summary>
    /// <param name="message">The message, in the words of DynamoDB where they are known.</param>
    /// <returns>The error.</returns>
    public static FakeError Validation(string message) => new(DynamoDbNamespace + "ValidationException", message);

    /// <summary>A body that is not the JSON an operation takes, such as a string where a number goes.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The error.</returns>
    public static FakeError Serialization(string message) => new(DynamoDbNamespace + "SerializationException", message);

    /// <summary>A table, or an index, that does not exist.</summary>
    /// <param name="message">The message, which differs between the item operations and the table operations.</param>
    /// <returns>The error.</returns>
    public static FakeError ResourceNotFound(string message = "Requested resource not found") => new(DynamoDbNamespace + "ResourceNotFoundException", message);

    /// <summary>A table that exists already.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The error.</returns>
    public static FakeError ResourceInUse(string message) => new(DynamoDbNamespace + "ResourceInUseException", message);

    /// <summary>A change beyond what DynamoDB allows at once, such as two index changes in one <c>UpdateTable</c>.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The error.</returns>
    public static FakeError LimitExceeded(string message) => new(DynamoDbNamespace + "LimitExceededException", message);

    /// <summary>A condition that did not hold.</summary>
    /// <param name="returned">The item as it was, when the request asked for it with <c>ReturnValuesOnConditionCheckFailure: ALL_OLD</c>, else <see langword="null"/>.</param>
    /// <returns>The error.</returns>
    public static FakeError ConditionalCheckFailed(Item? returned) =>
        new(DynamoDbNamespace + "ConditionalCheckFailedException", ConditionFailedMessage, extraBody: returned is null ? null : json => ResponseJson.WriteItem(json, "Item", returned));

    /// <summary>A transaction that was cancelled, with one reason per action of the request, in its order.</summary>
    /// <param name="reasons">The reasons: <c>None</c> for an action without a problem.</param>
    /// <returns>The error.</returns>
    public static FakeError TransactionCanceled(IReadOnlyList<CancellationReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        var codes = string.Join(", ", reasons.Select(static reason => reason.Code ?? "None"));
        return new(
            DynamoDbNamespace + "TransactionCanceledException",
            $"Transaction cancelled, please refer cancellation reasons for specific reasons [{codes}]",
            extraBody: json => ResponseJson.WriteCancellationReasons(json, reasons));
    }

    /// <summary>A client request token that came again with another request.</summary>
    /// <returns>The error.</returns>
    public static FakeError IdempotentParameterMismatch() =>
        new(DynamoDbNamespace + "IdempotentParameterMismatchException", "The request uses the same client token as a previous, but non-identical request.");

    /// <summary>A shard iterator older than the 15 minutes it is valid.</summary>
    /// <returns>The error.</returns>
    public static FakeError ExpiredIterator() => new(DynamoDbNamespace + "ExpiredIteratorException", "Shard iterator has expired");

    /// <summary>A position in a shard older than the 24 hours records are kept.</summary>
    /// <returns>The error.</returns>
    public static FakeError TrimmedDataAccess() =>
        new(DynamoDbNamespace + "TrimmedDataAccessException", "The operation attempted to read past the oldest stream record in a shard");

    /// <summary>An operation the fake does not implement, or a request without one.</summary>
    /// <param name="operation">The operation, or <see langword="null"/> for a request without <c>X-Amz-Target</c>.</param>
    /// <returns>The error.</returns>
    public static FakeError UnknownOperation(string? operation) =>
        new(DynamoDbNamespace + "UnknownOperationException", $"FakeDynamoDb does not implement {operation ?? "a request without X-Amz-Target"}. It is not supported.");

    /// <summary>Something inside an operation the fake implements that it does not: a member, a value of a member, a legacy parameter.</summary>
    /// <param name="what">What it is, such as <c>the member Tags</c>.</param>
    /// <param name="operation">The operation, or <see langword="null"/> where the caller does not know it.</param>
    /// <returns>The error.</returns>
    public static FakeError Unsupported(string what, string? operation = null) =>
        new(
            "DynaSharp.Testing#" + UnsupportedCode,
            $"FakeDynamoDb does not support {what}{(operation is null ? string.Empty : " in " + operation)}. It answers what {ScopeLink} lists, and nothing it does not implement is ignored.");

    /// <summary>Renders the body DynamoDB sends for this error.</summary>
    /// <returns>The UTF-8 JSON.</returns>
    public byte[] RenderBody()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, WireJson.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("__type", Type);
            json.WriteString("message", Message);
            ExtraBody?.Invoke(json);
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Makes the exception a client throws for this error, through the mapper of the client, so that the direct API fails as a call would.</summary>
    /// <param name="operation">The operation, for <see cref="DynamoDbException.OperationName"/>.</param>
    /// <param name="tableName">The table, for <see cref="DynamoDbException.TableName"/>.</param>
    /// <returns>The exception.</returns>
    public DynamoDbException ToDynamoDbException(string operation, string? tableName) => ErrorMapper.Map(Status, null, RenderBody(), operation, tableName);
}
