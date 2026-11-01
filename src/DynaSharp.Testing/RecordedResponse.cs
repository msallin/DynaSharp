using System.Net;
using System.Text;

namespace DynaSharp.Testing;

/// <summary>
/// An HTTP response that a <see cref="RecordingHandler"/> received from DynamoDB. It is a copy, so it stays readable after the
/// client has disposed the response.
/// </summary>
/// <remarks>
/// The body holds the items DynamoDB returned. Treat a recording like the data it contains.
/// </remarks>
public sealed class RecordedResponse
{
    private RecordedResponse(HttpStatusCode statusCode, IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        StatusCode = statusCode;
        Headers = headers;
        Body = body;
    }

    /// <summary>Gets the HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the response headers and the content headers (such as <c>Content-Type</c>) by name, ignoring case. A header with several
    /// values is joined with a comma and a space. DynamoDB sends its request id as <c>x-amzn-RequestId</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets the response body, which for DynamoDB is UTF-8 JSON. Empty when the response had no content.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>Gets the response body as text.</summary>
    /// <value>The body decoded as UTF-8.</value>
    public string BodyText => Encoding.UTF8.GetString(Body.Span);

    /// <summary>
    /// Reads the body of <paramref name="response"/> into memory and replaces the content of the response with a copy that
    /// can be read again, so the caller still sees the body.
    /// </summary>
    internal static async Task<RecordedResponse> BufferAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var headers = RecordedHeaders.Copy(response.Headers, response.Content);
        var original = response.Content;
        var body = await original.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var replacement = new ByteArrayContent(body);
        foreach (var header in original.Headers.NonValidated)
        {
            // The replacement knows its own length.
            if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        response.Content = replacement;
        original.Dispose();
        return new RecordedResponse(response.StatusCode, headers, body);
    }
}
