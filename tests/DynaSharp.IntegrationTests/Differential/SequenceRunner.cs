using System.Globalization;
using System.Text;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// Runs a sequence on the fake and a backend at once, operation by operation, on two fresh tables each, and stops at the first operation whose results differ, since the
/// tables may differ from then on. A failure is minimized: operations are left out one by one, as long as the shorter sequence still fails with the same signature.
/// </summary>
/// <param name="fake">The fake.</param>
/// <param name="emulator">The backend compared with it.</param>
/// <param name="witnesses">Further backends whose answers go into a report, and decide nothing.</param>
internal sealed class SequenceRunner(Target fake, Target emulator, IReadOnlyList<Target> witnesses)
{
    private const int MinimizationBudget = 150;

    /// <summary>Runs a sequence once.</summary>
    /// <param name="schema">The schema of its tables.</param>
    /// <param name="operations">The operations.</param>
    /// <param name="withWitnesses">Whether the witnesses get the sequence too.</param>
    /// <returns>What the run showed.</returns>
    public async Task<SequenceResult> RunAsync(Schema schema, IReadOnlyList<Operation> operations, bool withWitnesses = true)
    {
        var main = Tables.UniqueName();
        var other = Tables.UniqueName();
        var run = Guid.NewGuid().ToString("N")[..12];
        string Names(string json) => json.Replace(Schema.Main, main, StringComparison.Ordinal).Replace(Schema.Other, other, StringComparison.Ordinal).Replace("$RUN", run, StringComparison.Ordinal);
        string Placeholders(string json) => json.Replace(main, Schema.Main, StringComparison.Ordinal).Replace(other, Schema.Other, StringComparison.Ordinal).Replace(run, "$RUN", StringComparison.Ordinal);

        List<Target> targets = [fake, emulator, .. withWitnesses ? witnesses : []];
        var steps = new List<(Outcome, Outcome)>();
        var messages = new List<Divergence>();
        var passed = new List<Divergence>();
        try
        {
            foreach (var target in targets)
            {
                await target.CreateTablesAsync(schema, main, other);
            }

            for (var i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                var outcomes = new List<Outcome>();
                foreach (var target in targets)
                {
                    outcomes.Add(await target.SendAsync(operation, Names, Placeholders));
                }

                steps.Add((outcomes[0], outcomes[1]));
                var divergence = new Divergence(i, operation, outcomes[0], outcomes[1], [.. targets.Skip(2).Select((target, n) => (target.Backend.ToString(), outcomes[n + 2]))], null);
                if (divergence.IsHard)
                {
                    divergence = divergence with { KnownAs = KnownDivergences.Explain(emulator.Backend, divergence) };
                    if (divergence.KnownAs is null || !(divergence.IsCapacityOnly || IsRead(operation.Name)))
                    {
                        return new SequenceResult([.. operations.Take(i + 1)], steps, divergence, passed, messages);
                    }

                    passed.Add(divergence);
                    continue;
                }

                if (!outcomes[0].Messages.SequenceEqual(outcomes[1].Messages, StringComparer.Ordinal))
                {
                    messages.Add(divergence);
                }
            }

            return new SequenceResult(operations, steps, null, passed, messages);
        }
        finally
        {
            foreach (var target in targets)
            {
                await target.DeleteTablesAsync(main, other);
            }
        }
    }

    /// <summary>Shortens a failing sequence to the operations it needs to fail with the same signature.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="failed">The failing run.</param>
    /// <returns>The shortest failing run found.</returns>
    public async Task<SequenceResult> MinimizeAsync(Schema schema, SequenceResult failed)
    {
        var signature = failed.Failure!.Signature;
        var best = failed;
        var budget = MinimizationBudget;
        var changed = true;
        while (changed && budget > 0)
        {
            changed = false;
            for (var i = best.Operations.Count - 2; i >= 0 && budget > 0; i--)
            {
                budget--;
                var candidate = best.Operations.Where((_, n) => n != i).ToList();
                var result = await RunAsync(schema, candidate, withWitnesses: false);
                if (result.Failure?.Signature == signature)
                {
                    best = result;
                    changed = true;
                    i = Math.Min(i, best.Operations.Count - 1);
                }
            }
        }

        // One more run with the witnesses, so the report shows what the other backends answer to the minimized sequence.
        var final = witnesses.Count > 0 ? await RunAsync(schema, best.Operations) : best;
        return final.Failure?.Signature == signature ? final : best;
    }

    /// <summary>Describes a run for a report: the schema, each operation with both answers, and the divergence.</summary>
    /// <param name="seed">The seed of the sequence.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="result">The run.</param>
    /// <returns>The text.</returns>
    public string Describe(int seed, Schema schema, SequenceResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Seed {seed} ({fake.Backend} against {emulator.Backend}); schema: sort key {schema.SortKeyType}, index projection {schema.IndexProjection}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Replay: {DifferentialSettings.SeedVariable}={seed} {DifferentialSettings.SequencesVariable}=1 {BackendSelection.VariableName}={BackendName(emulator.Backend)}");
        for (var i = 0; i < result.Operations.Count; i++)
        {
            var operation = result.Operations[i];
            text.AppendLine(CultureInfo.InvariantCulture, $"[{i}] {operation.Name} {Canonical.Shown(operation.Body)}");
            if (i < result.Steps.Count)
            {
                var (fakeOutcome, emulatorOutcome) = result.Steps[i];
                text.AppendLine(CultureInfo.InvariantCulture, $"    fake:     {fakeOutcome.Shown}");
                if (emulatorOutcome.Shown != fakeOutcome.Shown)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"    {emulator.Backend}: {emulatorOutcome.Shown}");
                }
            }
        }

        if (result.Stop is { } stop)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Diverges at [{stop.Index}]: {stop.Signature}{(stop.KnownAs is null ? string.Empty : " (known: " + stop.KnownAs + ")")}");
            foreach (var (backend, outcome) in stop.Witnesses)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"    witness {backend}: {outcome.Shown}");
            }
        }

        return text.ToString();
    }

    private static bool IsRead(string operation) => operation is "GetItem" or "Query" or "Scan" or "BatchGetItem" or "TransactGetItems";

    private static string BackendName(Backend backend) => backend == Backend.DynamoDbLocal ? "dynamodb-local" : "dynoxide";
}
