namespace DynaSharp.UnitTests.Operations;

/// <summary>An error body and what botocore's parser made of it.</summary>
/// <param name="Operation">The operation the error answers, for example <c>TransactWriteItems</c>.</param>
/// <param name="Name">The name of the vector, unique among all error vectors.</param>
/// <param name="Status">The HTTP status code of the response.</param>
/// <param name="Wire">The response body as DynamoDB sends it.</param>
/// <param name="Code">The error code botocore read from <c>__type</c>.</param>
/// <param name="Message">The message botocore read from <c>message</c> or <c>Message</c>.</param>
/// <param name="Parsed">What botocore's parser returned for the body with the shape of the modeled exception, as JSON.</param>
internal sealed record ErrorVector(string Operation, string Name, int Status, string Wire, string Code, string Message, string Parsed);
