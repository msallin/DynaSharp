using System.Net.Http.Headers;
using System.Text;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// A minimal DynamoDB client that sends JSON by hand, independent of the code under test. It sets tables up and gives the tests
/// the unmapped wire response to compare the client's results with. Emulators accept any signature, so the authorization header is
/// a fixed placeholder. Real AWS does not, so there this class is handed a way to send, see <see cref="RealAws"/>.
/// </summary>
internal sealed class RawDynamoDb
{
    /// <summary>The target prefix of the table API.</summary>
    public const string TableTargetPrefix = "DynamoDB_20120810.";

    /// <summary>The target prefix of the streams API.</summary>
    public const string StreamsTargetPrefix = "DynamoDBStreams_20120810.";

    // Shaped like a SigV4 header: DynamoDB Local reads the access key and the region from it, nothing is verified.
    private const string Authorization =
        "AWS4-HMAC-SHA256 Credential=test/20260101/us-east-1/dynamodb/aws4_request, SignedHeaders=host;x-amz-date, Signature=0";

    private readonly Func<string, string, CancellationToken, Task<RawResponse>> _send;

    /// <summary>Initializes a new instance of the <see cref="RawDynamoDb"/> class for an emulator.</summary>
    /// <param name="endpoint">The emulator's address.</param>
    /// <param name="http">The HTTP client to send with.</param>
    /// <param name="targetPrefix">The service part of <c>X-Amz-Target</c>: the table API by default, <see cref="StreamsTargetPrefix"/> for streams.</param>
    public RawDynamoDb(Uri endpoint, HttpClient http, string targetPrefix = TableTargetPrefix)
        : this((operation, json, cancellationToken) => SendUnsignedAsync(endpoint, http, targetPrefix, operation, json, cancellationToken))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RawDynamoDb"/> class that sends some other way.</summary>
    /// <param name="send">Sends an operation with a body.</param>
    public RawDynamoDb(Func<string, string, CancellationToken, Task<RawResponse>> send)
    {
        _send = send;
    }

    /// <summary>Sends one operation.</summary>
    /// <param name="operation">The operation name, for example <c>CreateTable</c>.</param>
    /// <param name="json">The request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The status, the body and the request id, whether the call succeeded or not.</returns>
    public Task<RawResponse> SendAsync(string operation, string json, CancellationToken cancellationToken = default) => _send(operation, json, cancellationToken);

    private static async Task<RawResponse> SendUnsignedAsync(Uri endpoint, HttpClient http, string targetPrefix, string operation, string json, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/x-amz-json-1.0")),
        };
        request.Headers.TryAddWithoutValidation("X-Amz-Target", targetPrefix + operation);
        request.Headers.TryAddWithoutValidation("Authorization", Authorization);
        request.Headers.TryAddWithoutValidation("X-Amz-Date", "20260101T000000Z");

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var requestId = response.Headers.TryGetValues("x-amzn-RequestId", out var values) ? values.FirstOrDefault() : null;
        return new RawResponse(response.StatusCode, body, requestId);
    }
}
