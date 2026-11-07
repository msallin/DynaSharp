using System.Globalization;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>
/// The knobs of a differential run, from the environment. The defaults fit a pull request: a fixed first seed, so a run is the same each time and a new divergence
/// comes from a change of the code, not from luck. A long run sets more sequences and <c>random</c> as the seed.
/// </summary>
/// <param name="FirstSeed">The seed of the first sequence; sequence <c>i</c> has seed <c>FirstSeed + i</c>.</param>
/// <param name="Sequences">How many sequences run.</param>
/// <param name="Length">The most operations of a sequence, before its final scans.</param>
/// <param name="Parallelism">How many sequences run at once.</param>
/// <param name="ReportPath">A file that gets every divergence, known ones and those of messages only included; <see langword="null"/> for none.</param>
internal sealed record DifferentialSettings(int FirstSeed, int Sequences, int Length, int Parallelism, string? ReportPath)
{
    /// <summary>The variable of the first seed: a number, or <c>random</c>.</summary>
    public const string SeedVariable = "DYNASHARP_DIFF_SEED";

    /// <summary>The variable of the number of sequences.</summary>
    public const string SequencesVariable = "DYNASHARP_DIFF_SEQUENCES";

    /// <summary>The variable of the length of a sequence.</summary>
    public const string LengthVariable = "DYNASHARP_DIFF_LENGTH";

    /// <summary>The variable of the parallelism.</summary>
    public const string ParallelismVariable = "DYNASHARP_DIFF_PARALLEL";

    /// <summary>The variable of the report file.</summary>
    public const string ReportVariable = "DYNASHARP_DIFF_REPORT";

    /// <summary>Reads the settings of this run.</summary>
    /// <returns>The settings.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "A random first seed only picks other sequences; it is printed with every failure.")]
    public static DifferentialSettings FromEnvironment()
    {
        var seed = Environment.GetEnvironmentVariable(SeedVariable);
        var first = string.Equals(seed, "random", StringComparison.OrdinalIgnoreCase)
            ? Random.Shared.Next(1, int.MaxValue / 2)
            : Number(SeedVariable, 1);
        return new DifferentialSettings(
            first,
            Number(SequencesVariable, 120),
            Number(LengthVariable, 25),
            Number(ParallelismVariable, 4),
            Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } path ? path : null);
    }

    private static int Number(string variable, int defaultValue) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } text ? int.Parse(text, CultureInfo.InvariantCulture) : defaultValue;
}
