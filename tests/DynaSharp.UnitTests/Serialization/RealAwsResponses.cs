using System.Text.Json;

namespace DynaSharp.UnitTests.Serialization;

/// <summary>
/// Response bodies recorded from real DynamoDB by the DynamoDB Conformance Suite (Apache-2.0, see NOTICE), embedded as
/// <c>real_aws_responses.json</c>. They are the one source of responses in the unit tests that neither this library, botocore nor
/// an emulator produced.
/// </summary>
internal static class RealAwsResponses
{
    private static readonly Lazy<IReadOnlyList<RealAwsResponse>> Data = new(Load);

    public static IReadOnlyList<RealAwsResponse> All => Data.Value;

    public static IEnumerable<string> Names(string operation) =>
        All.Where(response => response.Operation == operation).Select(response => response.Name);

    public static RealAwsResponse Get(string name) => All.Single(response => response.Name == name);

    private static List<RealAwsResponse> Load()
    {
        using var stream = typeof(RealAwsResponses).Assembly.GetManifestResourceStream("real_aws_responses.json")
            ?? throw new InvalidOperationException("The embedded resource real_aws_responses.json is missing.");
        using var document = JsonDocument.Parse(stream);
        return [.. document.RootElement.GetProperty("responses").EnumerateArray()
            .Select(element => new RealAwsResponse(
                element.GetProperty("name").GetString()!,
                element.GetProperty("operation").GetString()!,
                element.GetProperty("wire").GetString()!))];
    }
}
