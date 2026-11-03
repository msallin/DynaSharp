using System.Net;
using System.Text.Json;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Testing.Fake;

/// <summary>Sends hand-written bodies to a fake and reads its answers, so that a test compares JSON with JSON written from the API reference.</summary>
internal static class FakeCalls
{
    /// <summary>A table with a string partition key <c>pk</c> and a number sort key <c>sk</c>, on demand.</summary>
    public const string Orders = """
        {"TableName":"Orders","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"},{"AttributeName":"sk","AttributeType":"N"}],
         "KeySchema":[{"AttributeName":"pk","KeyType":"HASH"},{"AttributeName":"sk","KeyType":"RANGE"}],"BillingMode":"PAY_PER_REQUEST"}
        """;

    /// <summary>A table with a string partition key <c>pk</c> alone, on demand.</summary>
    public const string Users = """
        {"TableName":"Users","AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"BillingMode":"PAY_PER_REQUEST"}
        """;

    /// <summary>Sends a request and gives the status and the parsed body.</summary>
    public static (HttpStatusCode Status, JsonElement Body) Send(FakeDynamoDb fake, string operation, string json)
    {
        var response = fake.Answer(operation, json);
        return (response.StatusCode, JsonElement.Parse(response.Body));
    }

    /// <summary>Sends a request that must succeed and gives the body.</summary>
    public static JsonElement Ok(FakeDynamoDb fake, string operation, string json)
    {
        var (status, body) = Send(fake, operation, json);
        return status == HttpStatusCode.OK ? body : throw new InvalidOperationException($"{operation} failed: {body}");
    }

    /// <summary>Sends a request that must fail and gives the code after the <c>#</c> and the message.</summary>
    public static (HttpStatusCode Status, string Code, string Message) Error(FakeDynamoDb fake, string operation, string json)
    {
        var (status, body) = Send(fake, operation, json);
        var type = body.GetProperty("__type").GetString()!;
        return (status, type[(type.AsSpan().LastIndexOf('#') + 1)..], body.GetProperty("message").GetString()!);
    }

    /// <summary>Makes a fake with tables created from the given bodies.</summary>
    public static FakeDynamoDb With(params string[] createTables)
    {
        var fake = new FakeDynamoDb(new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        foreach (var table in createTables)
        {
            Ok(fake, "CreateTable", table);
        }

        return fake;
    }

    /// <summary>Compares JSON by value, members in any order.</summary>
    public static bool SameJson(string expected, JsonElement actual) => JsonElement.DeepEquals(JsonElement.Parse(expected), actual);

    /// <summary>Gives a copy of an object without a member, for one whose value a test cannot know, such as <c>TableId</c>.</summary>
    public static JsonElement Without(JsonElement value, string member)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText())!.AsObject();
        node.Remove(member);
        return JsonElement.Parse(node.ToJsonString());
    }
}
