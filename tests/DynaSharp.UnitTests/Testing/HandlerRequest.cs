using System.Text;

namespace DynaSharp.UnitTests.Testing;

/// <summary>Sends a request through a handler of the testing package the way the client does, without a client.</summary>
internal static class HandlerRequest
{
    public const string Endpoint = "https://dynamodb.eu-central-2.amazonaws.com/";

    public static async Task<HttpResponseMessage> PostAsync(
        HttpMessageHandler handler,
        string body = "{}",
        string? target = "DynamoDB_20120810.GetItem",
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-amz-json-1.0"),
        };
        if (target is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Amz-Target", target);
        }

        return await client.SendAsync(request, cancellationToken);
    }
}
