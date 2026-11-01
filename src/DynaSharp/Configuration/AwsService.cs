namespace DynaSharp.Configuration;

/// <summary>
/// The AWS services this library talks to. They differ in their default endpoint and in the environment variable that overrides
/// it; region, credentials, retries and timeouts are shared.
/// </summary>
internal enum AwsService
{
    /// <summary>Amazon DynamoDB (<c>dynamodb.&lt;region&gt;.amazonaws.com</c>).</summary>
    DynamoDb,

    /// <summary>Amazon DynamoDB Streams (<c>streams.dynamodb.&lt;region&gt;.amazonaws.com</c>).</summary>
    DynamoDbStreams,
}
