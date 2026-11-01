namespace DynaSharp.UnitTests.Serialization;

/// <summary>A response body of real DynamoDB.</summary>
/// <param name="Name">The name of the recording, unique in the file.</param>
/// <param name="Operation">The operation, <c>GetItem</c> or <c>Query</c>.</param>
/// <param name="Wire">The response body.</param>
internal sealed record RealAwsResponse(string Name, string Operation, string Wire);
