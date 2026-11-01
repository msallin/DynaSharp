namespace DynaSharp.UnitTests.Errors;

/// <summary>One error response as DynamoDB sends it and the exception it must become. The display name in test output is the error code of the body.</summary>
/// <param name="Name">The name shown in test output.</param>
/// <param name="Body">The response body.</param>
/// <param name="ExceptionType">The expected exception type.</param>
/// <param name="Message">The expected message.</param>
public readonly record struct ErrorCase(string Name, string Body, Type ExceptionType, string Message)
{
    public override string ToString() => Name;
}
