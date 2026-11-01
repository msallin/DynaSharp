namespace DynaSharp.UnitTests.Operations;

/// <summary>A response body and what botocore's parser made of it.</summary>
/// <param name="Operation">The operation, for example <c>Query</c>.</param>
/// <param name="Name">The name of the vector, unique within the operation.</param>
/// <param name="Wire">The response body as DynamoDB sends it.</param>
/// <param name="Parsed">What botocore's parser returned for the body, as JSON.</param>
internal sealed record ResponseVector(string Operation, string Name, string Wire, string Parsed);
