using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DynaSharp.PackageSmoke;

/// <summary>A stub of DynamoDB that holds one item: a put stores it, a get returns it, and an update answers with what the test gave it.</summary>
internal sealed class OneItemHandler : HttpMessageHandler
{
    private string? _item;

    /// <summary>Gets the body of the last request.</summary>
    public string LastBody { get; private set; } = string.Empty;

    /// <summary>Gets or sets what an update answers.</summary>
    public string UpdateResponse { get; set; } = "{}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        var target = request.Headers.GetValues("X-Amz-Target").Single();
        var body = target switch
        {
            "DynamoDB_20120810.PutItem" => Store(),
            "DynamoDB_20120810.GetItem" => $$"""{"Item":{{_item}}}""",
            "DynamoDB_20120810.UpdateItem" => UpdateResponse,
            _ => throw new InvalidOperationException("The stub does not answer " + target),
        };
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.0");
        return new HttpResponseMessage { Content = content };
    }

    private string Store()
    {
        using var document = JsonDocument.Parse(LastBody);
        _item = document.RootElement.GetProperty("Item").GetRawText();
        return "{}";
    }
}
