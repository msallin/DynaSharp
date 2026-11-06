using System.Net;

namespace DynaSharp;

/// <summary>
/// DynamoDB rejected the request: a malformed expression, a key that does not match the schema, an empty key, an item over 400 KB. Not retried: the request must change.
/// </summary>
/// <remarks>
/// A command of the table API writes every attribute name and value as a placeholder, so the message of the service talks about <c>#n2</c>
/// and <c>:v3</c>. For such a failure <see cref="Expressions"/>, <see cref="ExpressionNames"/> and <see cref="ExpressionValueTypes"/>
/// say what those stand for, and <see cref="Exception.Message"/> gains one line with the same content. Values are never included, only
/// their types, so the exception is as safe to log as any other. A request of the request API has none of this: its caller wrote the
/// expressions. <see cref="DynamoDbException.IsTransient"/> is <see langword="false"/>.
/// </remarks>
public sealed class ValidationException : DynamoDbException
{
    private string? _explanation;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by DynamoDB.</param>
    /// <param name="requestId">The AWS request id, if known.</param>
    /// <param name="statusCode">The HTTP status code of the response.</param>
    public ValidationException(string message, string? requestId = null, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, "ValidationException", statusCode, requestId)
    {
    }

    /// <summary>Gets the message of the service, followed for a command by a line that explains the placeholders.</summary>
    public override string Message => _explanation is null ? base.Message : $"{base.Message}{Environment.NewLine}{_explanation}";

    /// <summary>
    /// Gets the expressions of the rejected command as sent, by the name of the request member, for example <c>ConditionExpression</c> with
    /// <c>#n0 = :v0</c>. <see langword="null"/> for a failure of the request API, and for a command without expressions.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Expressions { get; private set; }

    /// <summary>Gets what the name placeholders of <see cref="Expressions"/> stand for, for example <c>#n0</c> with <c>status</c>.</summary>
    public IReadOnlyDictionary<string, string>? ExpressionNames { get; private set; }

    /// <summary>
    /// Gets the type descriptors of the value placeholders of <see cref="Expressions"/>, for example <c>:v0</c> with <c>S</c>. The values themselves are
    /// left out on purpose.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExpressionValueTypes { get; private set; }

    /// <summary>Attaches what explains the placeholders of a rejected command.</summary>
    /// <param name="expressions">The expressions by request member.</param>
    /// <param name="names">The name placeholders.</param>
    /// <param name="valueTypes">The type descriptors of the value placeholders.</param>
    /// <param name="explanation">The line for the message.</param>
    internal void Describe(IReadOnlyDictionary<string, string> expressions, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, string> valueTypes, string explanation)
    {
        Expressions = expressions;
        ExpressionNames = names;
        ExpressionValueTypes = valueTypes;
        _explanation = explanation;
    }
}
