using System.Net;
using System.Text.Json.Nodes;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// What a backend answered to one operation, in the form that is compared: <see cref="Result"/> must be equal, <see cref="Messages"/> only should be.
/// </summary>
/// <param name="ErrorCode">The error code without namespace, or <see langword="null"/> for success.</param>
/// <param name="Result">The answer in canonical form: the response, or the error's members other than its message (the item of a failed condition, the cancellation reasons).</param>
/// <param name="Capacity">The <c>ConsumedCapacity</c> of the answer, in canonical form, apart from <see cref="Result"/> so that a divergence of capacity alone can be told from one of the data.</param>
/// <param name="Messages">The error messages that are not part of <see cref="Result"/>: messages are DynamoDB's fine print, which the fake mimics only in part.</param>
/// <param name="Shown">The answer for a report.</param>
internal sealed record Outcome(string? ErrorCode, string Result, string Capacity, IReadOnlyList<string> Messages, string Shown)
{
    // The member of a cancellation reason that holds its code.
    private const string CodeMember = "Code";

    // Messages the fake mimics word for word, so they are part of the result. A cancellation reason's message for a failed condition is the same text.
    private static readonly HashSet<string> MimickedCodes = new(["ConditionalCheckFailedException", "TransactionCanceledException"], StringComparer.Ordinal);

    /// <summary>Gets the error code or <c>OK</c>, for a summary.</summary>
    public string Code => ErrorCode ?? "OK";

    /// <summary>Reads a raw response of a single request.</summary>
    /// <param name="response">The response.</param>
    /// <param name="normalize">Turns a successful response into the form that is compared.</param>
    /// <returns>The outcome.</returns>
    public static Outcome Of(RawResponse response, Func<JsonObject, JsonObject>? normalize = null)
    {
        var body = string.IsNullOrEmpty(response.Body) ? [] : JsonNode.Parse(response.Body)!.AsObject();
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var capacity = body["ConsumedCapacity"];
            body.Remove("ConsumedCapacity");
            return Success(normalize is null ? body : normalize(body), capacity);
        }

        return Failure(response.StatusCode, body);
    }

    /// <summary>Gets the outcome of a successful answer, also one that the runner put together, such as the pages of a query.</summary>
    /// <param name="result">The answer, without its consumed capacity.</param>
    /// <param name="capacity">The consumed capacity, or <see langword="null"/>.</param>
    /// <returns>The outcome.</returns>
    public static Outcome Success(JsonNode result, JsonNode? capacity)
    {
        // A batch or a transaction reports one entry per table, in no order.
        var capacityText = capacity is JsonArray entries
            ? "[" + string.Join(",", entries.Select(static entry => Canonical.Of(entry)).Order(StringComparer.Ordinal)) + "]"
            : Canonical.Of(capacity);
        return new(null, Canonical.Of(result), capacityText, [], Canonical.Shown(result) + (capacity is null ? string.Empty : " capacity " + capacityText));
    }

    private static Outcome Failure(HttpStatusCode status, JsonObject body)
    {
        var type = body["__type"]?.GetValue<string>() ?? "(no __type)";
        var code = type[(type.LastIndexOf('#', StringComparison.Ordinal) + 1)..];
        var messages = new List<string>();
        var rest = new JsonObject { ["status"] = (int)status };
        foreach (var (name, value) in body)
        {
            if (name is "__type")
            {
                continue;
            }

            if (name is "message" or "Message")
            {
                if (MimickedCodes.Contains(code))
                {
                    rest["message"] = value!.DeepClone();
                }
                else
                {
                    messages.Add(value!.GetValue<string>());
                }

                continue;
            }

            rest[name] = value?.DeepClone();
        }

        if (rest["CancellationReasons"] is JsonArray reasons)
        {
            foreach (var reason in reasons.OfType<JsonObject>())
            {
                if (reason["Message"] is { } message && reason[CodeMember]?.GetValue<string>() != "ConditionalCheckFailed")
                {
                    messages.Add(message.GetValue<string>());
                    reason.Remove("Message");
                }
            }
        }

        return new Outcome(code, Canonical.Of(rest), "null", messages, $"{code}: {string.Join(" | ", messages)} {Canonical.Shown(rest)}");
    }
}
