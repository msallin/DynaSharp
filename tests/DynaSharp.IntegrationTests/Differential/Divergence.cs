using System.Text.Json.Nodes;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>Where the fake and a backend answered one operation differently.</summary>
/// <param name="Index">The position of the operation in the sequence.</param>
/// <param name="Operation">The operation.</param>
/// <param name="Fake">The fake's answer.</param>
/// <param name="Emulator">The other backend's answer.</param>
/// <param name="Witnesses">The answers of further backends, for a report, keyed by backend.</param>
/// <param name="KnownAs">The reason, when it is a known divergence of the backend (see <see cref="KnownDivergences"/>); <see langword="null"/> otherwise.</param>
internal sealed record Divergence(int Index, Operation Operation, Outcome Fake, Outcome Emulator, IReadOnlyList<(string Backend, Outcome Outcome)> Witnesses, string? KnownAs)
{
    /// <summary>Gets what a minimized sequence must still show: the operation and the two error codes.</summary>
    public string Signature => $"{Operation.Name}: fake {Fake.Code}, emulator {Emulator.Code}";

    /// <summary>Gets a value indicating whether the answers differ in more than their messages.</summary>
    public bool IsHard => Fake.ErrorCode != Emulator.ErrorCode || Fake.Result != Emulator.Result || Fake.Capacity != Emulator.Capacity;

    /// <summary>Gets a value indicating whether the answers differ in their consumed capacity alone.</summary>
    public bool IsCapacityOnly => Fake.ErrorCode == Emulator.ErrorCode && Fake.Result == Emulator.Result && Fake.Capacity != Emulator.Capacity;

    /// <summary>Writes the divergence as one line of JSON, for the report of a long run.</summary>
    /// <param name="seed">The seed of its sequence.</param>
    /// <param name="backend">The backend compared with the fake.</param>
    /// <returns>The line.</returns>
    public string ToReportLine(int seed, Backend backend)
    {
        var witnesses = new JsonObject();
        foreach (var (name, outcome) in Witnesses)
        {
            witnesses[name] = Of(outcome);
        }

        return new JsonObject
        {
            ["kind"] = !IsHard ? "message" : KnownAs is null ? "hard" : "known",
            ["known"] = KnownAs,
            ["seed"] = seed,
            ["backend"] = backend.ToString(),
            ["index"] = Index,
            ["operation"] = Operation.Name,
            ["request"] = Canonical.Shown(Operation.Body),
            ["fake"] = Of(Fake),
            ["emulator"] = Of(Emulator),
            ["witnesses"] = witnesses,
        }.ToJsonString();
    }

    private static JsonObject Of(Outcome outcome) => new()
    {
        ["code"] = outcome.Code,
        ["result"] = outcome.Result,
        ["capacity"] = outcome.Capacity,
        ["messages"] = new JsonArray([.. outcome.Messages.Select(static message => (JsonNode)message)]),
    };
}
