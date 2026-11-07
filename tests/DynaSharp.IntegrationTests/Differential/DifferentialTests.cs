using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// Differential tests of the fake: generated sequences of data plane operations run on the fake and on an emulator, and every answer must be the same, up to what DynamoDB
/// leaves unordered. The emulator stands in for DynamoDB; where it is the one that differs, <see cref="KnownDivergences"/> says so. A failure names the seed and prints the
/// minimized sequence. <see cref="DifferentialSettings"/> has the knobs for a long run.
/// </summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class DifferentialTests(Backend backend)
{
    [Test]
    public async Task RandomSequences_OnTheFakeAndTheEmulator_AnswerAlike()
    {
        // Arrange
        if (backend is Backend.Fake or Backend.RealAws)
        {
            // Real AWS reads eventually consistent from its indexes, so an answer may differ for no fault of the fake; and the run would cost money.
            Skip.Test($"{backend}: the fake is compared with an emulator, see DifferentialTests.");
        }

        var settings = DifferentialSettings.FromEnvironment();
        var fake = await Emulators.GetAsync(Backend.Fake);
        var emulator = await Emulators.GetAsync(backend);
        var witnesses = new List<Target>();
        if (settings.ReportPath is not null)
        {
            foreach (var other in BackendSelection.Selected().Where(other => other is not (Backend.Fake or Backend.RealAws) && other != backend))
            {
                witnesses.Add(new Target(other, (await Emulators.GetAsync(other)).Raw));
            }
        }

        var runner = new SequenceRunner(new Target(Backend.Fake, fake.Raw), new Target(backend, emulator.Raw), witnesses);

        // Act
        var failures = new ConcurrentBag<string>();
        var report = new ConcurrentQueue<string>();
        var known = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        await Parallel.ForAsync(0, settings.Sequences, new ParallelOptions { MaxDegreeOfParallelism = settings.Parallelism }, async (i, _) =>
        {
            if (settings.ReportPath is null && !failures.IsEmpty)
            {
                return;
            }

            var seed = settings.FirstSeed + i;
            var (schema, operations) = SequenceGenerator.Generate(seed, settings.Length);
            var result = await runner.RunAsync(schema, operations);
            foreach (var reason in result.Passed.Append(result.Stop).Select(static divergence => divergence?.KnownAs).OfType<string>())
            {
                known.AddOrUpdate(reason, 1, static (_, n) => n + 1);
            }

            // A report collects every divergence as a line of JSON and minimizes nothing: it is for a long run, whose divergences are sorted afterwards. A replay of the seed minimizes.
            if (settings.ReportPath is not null)
            {
                foreach (var divergence in result.Messages.Concat(result.Passed).Append(result.Stop).OfType<Divergence>())
                {
                    report.Enqueue(divergence.ToReportLine(seed, backend));
                }
            }

            if (result.Failure is not null)
            {
                failures.Add(runner.Describe(seed, schema, settings.ReportPath is null ? await runner.MinimizeAsync(schema, result) : result));
            }
        });

        if (settings.ReportPath is not null)
        {
            await File.AppendAllLinesAsync(settings.ReportPath, report);
        }

        // Assert
        var summary = new StringBuilder();
        summary.AppendLine(CultureInfo.InvariantCulture, $"{settings.Sequences} sequences from seed {settings.FirstSeed} on {backend}; ended at a known divergence: {known.Values.Sum()}.");
        foreach (var (reason, count) in known)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"  {count} x {reason}");
        }

        Console.WriteLine(summary);
        await Assert.That(failures).IsEmpty().Because(summary + string.Join(Environment.NewLine, failures));
    }
}
