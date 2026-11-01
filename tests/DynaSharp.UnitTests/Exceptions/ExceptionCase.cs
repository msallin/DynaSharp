using System.Net;

namespace DynaSharp.UnitTests.Exceptions;

/// <summary>One typed service exception: how to create it and what it must report. The display name in test output is the error code.</summary>
/// <param name="ErrorCode">The expected error code.</param>
/// <param name="StatusCode">The expected HTTP status code.</param>
/// <param name="Create">Creates the exception from a message and a request id.</param>
public readonly record struct ExceptionCase(string ErrorCode, HttpStatusCode StatusCode, Func<string, string?, DynamoDbException> Create)
{
    public override string ToString() => ErrorCode;
}
