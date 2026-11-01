namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// Chooses the backends of a run from the environment variable <c>DYNASHARP_BACKENDS</c>: a comma separated list of
/// <c>dynoxide</c>, <c>dynamodb-local</c> and <c>aws</c>, or <c>all</c> for all emulators. Unset or empty means Dynoxide only, which keeps local
/// runs fast. <c>all</c> leaves <c>aws</c> out, because that one costs money and has to be asked for by name.
/// </summary>
public static class BackendSelection
{
    /// <summary>The name of the environment variable.</summary>
    public const string VariableName = "DYNASHARP_BACKENDS";

    private const string All = "all";

    // When adding a backend, add its name here (see Backend).
    private static readonly Dictionary<string, Backend> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dynoxide"] = Backend.Dynoxide,
        ["dynamodb-local"] = Backend.DynamoDbLocal,
        ["aws"] = Backend.RealAws,
    };

    /// <summary>The data source of every test class: the backends the current run selected.</summary>
    /// <returns>The selected backends, in the order they were listed.</returns>
    public static IEnumerable<Backend> Selected() => Parse(Environment.GetEnvironmentVariable(VariableName));

    /// <summary>Parses the value of <c>DYNASHARP_BACKENDS</c>.</summary>
    /// <param name="value">The value, or <see langword="null"/> when the variable is not set.</param>
    /// <returns>The selected backends without duplicates.</returns>
    /// <exception cref="ArgumentException">The value names a backend that does not exist.</exception>
    internal static IReadOnlyList<Backend> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [Backend.Dynoxide];
        }

        var selected = new List<Backend>();
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(entry, All, StringComparison.OrdinalIgnoreCase))
            {
                selected.AddRange(Names.Values.Where(static backend => backend != Backend.RealAws));
            }
            else if (Names.TryGetValue(entry, out var backend))
            {
                selected.Add(backend);
            }
            else
            {
                throw new ArgumentException($"{VariableName} names the unknown backend '{entry}'. Known: {string.Join(", ", Names.Keys)}, {All}.", nameof(value));
            }
        }

        return [.. selected.Distinct()];
    }
}
