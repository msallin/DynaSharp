using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

/// <summary>
/// The oracle is DynamoDB Local: <c>build/generate_fake_vectors.py</c> sent each case to it and recorded the answers in <c>Vectors/*.json</c>. Each case is
/// replayed on a fresh fake. Status and code are compared always, the message where the step says <c>compareMessage</c>, and the body of a success as
/// JSON, with members in any order, the elements of a set in any order and numbers by value, so that 2.0 of DynamoDB Local is 2.
/// </summary>
public sealed partial class FakeVectorTests
{
    private const string ResourcePrefix = "fake-vectors/";

    private static readonly Lazy<IReadOnlyDictionary<string, JsonObject>> Cases = new(Load);

    public static IEnumerable<string> CaseNames() => Cases.Value.Keys.Order(StringComparer.Ordinal);

    [Test]
    public async Task Vectors_Always_AreThere()
    {
        // Arrange (the files are embedded resources of this project)

        // Act
        var count = Cases.Value.Count;

        // Assert
        await Assert.That(count).IsGreaterThanOrEqualTo(20);
    }

    [Test]
    [MethodDataSource(nameof(CaseNames))]
    public async Task Answer_RecordedCase_AnswersAsDynamoDbLocal(string name)
    {
        // Arrange
        var steps = Cases.Value[name]["steps"]!.AsArray();
        var fake = new FakeDynamoDb();

        // Act
        var differences = new List<string>();
        foreach (var (step, index) in steps.Select(static (step, index) => (step!.AsObject(), index)))
        {
            var operation = step["operation"]!.GetValue<string>();
            var answer = fake.Answer(operation, Expand(step["request"]!).ToJsonString());
            differences.AddRange(Compare(step, answer).Select(difference => $"step {index} ({operation}): {difference}"));
        }

        // Assert
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Expand_RepeatMarker_IsTheCharacterThatManyTimes()
    {
        // Arrange
        var node = JsonNode.Parse("""{"v":{"S":"${x*3}"},"w":["${y*2}","plain"]}""")!;

        // Act
        var expanded = Expand(node).ToJsonString();

        // Assert
        await Assert.That(expanded).IsEqualTo("""{"v":{"S":"xxx"},"w":["yy","plain"]}""");
    }

    private static IEnumerable<string> Compare(JsonObject step, StubResponse answer)
    {
        var expectedStatus = (HttpStatusCode)step["status"]!.GetValue<int>();
        if (answer.StatusCode != expectedStatus)
        {
            yield return $"status {(int)answer.StatusCode}, expected {(int)expectedStatus}: {answer.Body}";
            yield break;
        }

        var body = JsonNode.Parse(answer.Body)!.AsObject();
        if (expectedStatus != HttpStatusCode.OK)
        {
            var type = body["__type"]!.GetValue<string>();
            var code = type[(type.AsSpan().LastIndexOf('#') + 1)..];
            var message = body["message"]?.GetValue<string>();
            if (code != step["code"]!.GetValue<string>())
            {
                yield return $"code {code}, expected {step["code"]}: {message}";
            }
            else if (step["compareMessage"] is not null && message != step["message"]!.GetValue<string>())
            {
                yield return $"message \"{message}\", expected \"{step["message"]}\"";
            }

            // The codes of the reasons; their messages are best effort, as every message is.
            if (step["cancellationReasons"] is { } reasons && Codes(reasons) != Codes(body["CancellationReasons"]))
            {
                yield return $"reasons {body["CancellationReasons"]?.ToJsonString()}, expected {reasons.ToJsonString()}";
            }

            yield break;
        }

        if (step["response"] is { } response && Canonical(response) != Canonical(body))
        {
            yield return $"body {body.ToJsonString()}, expected {response.ToJsonString()}";
        }
    }

    private static string Codes(JsonNode? reasons) => reasons is JsonArray array ? string.Join(",", array.Select(static reason => reason!["Code"]!.GetValue<string>())) : "none";

    // A text that is equal for equal answers: members sorted by name, set elements sorted, numbers printed by value.
    private static string Canonical(JsonNode? node) => Sorted(node, null)?.ToJsonString() ?? "null";

    private static JsonNode? Sorted(JsonNode? node, string? member) => node switch
    {
        null => null,
        JsonObject value => new JsonObject(value.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => KeyValuePair.Create(pair.Key, Sorted(pair.Value, pair.Key)))),
        JsonArray value when member is "SS" or "NS" or "BS" => new JsonArray([.. value.Select(static element => element!.ToJsonString()).Order(StringComparer.Ordinal).Select(static text => JsonNode.Parse(text))]),
        JsonArray value => new JsonArray([.. value.Select(element => Sorted(element, null))]),
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => JsonValue.Create(value.GetValue<double>().ToString(CultureInfo.InvariantCulture)),
        _ => node!.DeepClone(),
    };

    // Input: {"S":"${x*3}"} -> {"S":"xxx"}; see the remarks of build/generate_fake_vectors.py.
    private static JsonNode Expand(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value.Select(static pair => KeyValuePair.Create(pair.Key, (JsonNode?)Expand(pair.Value!)))),
        JsonArray value => new JsonArray([.. value.Select(static element => Expand(element!))]),
        JsonValue value when value.GetValueKind() == JsonValueKind.String && RepeatMarker().Match(value.GetValue<string>()) is { Success: true } match =>
            JsonValue.Create(new string(match.Groups[1].Value[0], int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))),
        _ => node.DeepClone(),
    };

    private static Dictionary<string, JsonObject> Load()
    {
        var assembly = typeof(FakeVectorTests).Assembly;
        var cases = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var file = Path.GetFileNameWithoutExtension(resource[ResourcePrefix.Length..]);
            foreach (var entry in JsonNode.Parse(stream)!["cases"]!.AsArray())
            {
                cases.Add($"{file}/{entry!["name"]!.GetValue<string>()}", entry.AsObject());
            }
        }

        return cases;
    }

    [GeneratedRegex(@"^\$\{(.)\*(\d+)\}$")]
    private static partial Regex RepeatMarker();
}
