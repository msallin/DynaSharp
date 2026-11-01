using System.Net;

namespace DynaSharp.Errors;

/// <summary>
/// What <see cref="ErrorMapper"/> read from an error response, handed to the factory of the exception type the error code names.
/// </summary>
/// <param name="Message">The message of the body, or a generated one if the body has none.</param>
/// <param name="RequestId">The value of the <c>x-amzn-RequestId</c> header, if any.</param>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Item">The <c>Item</c> of the body, sent with a failed condition.</param>
/// <param name="CancellationReasons">The <c>CancellationReasons</c> of the body, sent with a cancelled transaction.</param>
/// <param name="OperationName">The operation that failed, if known.</param>
/// <param name="TableName">The table of the failed call, if it has a single one.</param>
internal readonly record struct ErrorBody(
    string Message,
    string? RequestId,
    HttpStatusCode StatusCode,
    Item? Item,
    IReadOnlyList<CancellationReason>? CancellationReasons,
    string? OperationName,
    string? TableName);
