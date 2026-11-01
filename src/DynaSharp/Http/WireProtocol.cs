namespace DynaSharp.Http;

/// <summary>
/// The fixed parts of the DynamoDB wire protocol (AWS JSON 1.0), shared by the pipeline, the signer and <c>DynaSharp.Testing</c>.
/// </summary>
internal static class WireProtocol
{
    /// <summary>The content type of every request and response. Signed as this exact value, without parameters.</summary>
    public const string ContentType = "application/x-amz-json-1.0";

    /// <summary>The header that names the operation.</summary>
    public const string TargetHeaderName = "X-Amz-Target";

    /// <summary>What the value of <see cref="TargetHeaderName"/> starts with, followed by the operation name.</summary>
    public const string TargetPrefix = "DynamoDB_20120810.";

    /// <summary>What the value of <see cref="TargetHeaderName"/> starts with for DynamoDB Streams, followed by the operation name.</summary>
    public const string StreamsTargetPrefix = "DynamoDBStreams_20120810.";

    /// <summary>The header with the request id of a response.</summary>
    public const string RequestIdHeaderName = "x-amzn-RequestId";

    /// <summary>What CloudTrail and AWS support see as the client. Not signed.</summary>
    public const string UserAgent = "DynaSharp";
}
