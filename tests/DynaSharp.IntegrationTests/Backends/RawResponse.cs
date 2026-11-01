using System.Net;
using System.Text.Json;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>The status line, the body and the request id of a raw response.</summary>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="Body">The body text.</param>
/// <param name="RequestId">The <c>x-amzn-RequestId</c> header, when present.</param>
internal sealed record RawResponse(HttpStatusCode StatusCode, string Body, string? RequestId)
{
    /// <summary>Gets the error code of an error response: the <c>__type</c> without namespace, as the service sends it.</summary>
    public string? ErrorCode
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            if (!document.RootElement.TryGetProperty("__type", out var type))
            {
                return null;
            }

            // "com.amazonaws.dynamodb.v20120810#ResourceNotFoundException" -> "ResourceNotFoundException"
            var text = type.GetString()!;
            return text[(text.LastIndexOf('#', StringComparison.Ordinal) + 1)..];
        }
    }

    /// <summary>Throws when the call did not succeed, so a failing setup step shows the emulator's own words.</summary>
    /// <returns>This response.</returns>
    public RawResponse EnsureSuccess()
    {
        if (StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The emulator answered HTTP {(int)StatusCode}: {Body}");
        }

        return this;
    }
}
