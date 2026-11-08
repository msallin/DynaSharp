namespace DynaSharp.Generators;

/// <summary>How close two names are, for the fixes that offer the names a typo may have meant.</summary>
internal static class Spelling
{
    /// <summary>The candidates in the order of their distance to the name, the closest first, and among equals by ordinal order. A difference of case alone counts as the closest.</summary>
    /// <param name="name">The name as written.</param>
    /// <param name="candidates">The names there are.</param>
    /// <param name="maximum">The largest distance to keep, or null for all.</param>
    /// <returns>The candidates kept, without the name itself.</returns>
    public static List<string> Closest(string name, IEnumerable<string> candidates, int? maximum) =>
        [
            .. candidates
                .Where(candidate => candidate != name)
                .Distinct(StringComparer.Ordinal)
                .Select(candidate => (Name: candidate, Distance: Distance(name, candidate)))
                .Where(pair => maximum is not { } most || pair.Distance <= most)
                .OrderBy(static pair => pair.Distance)
                .ThenBy(static pair => pair.Name, StringComparer.Ordinal)
                .Select(static pair => pair.Name),
        ];

    /// <summary>The largest distance at which a name of this length is still taken for a typo: one edit in four letters, at least one.</summary>
    /// <param name="name">The name as written.</param>
    /// <returns>The distance.</returns>
    public static int TypoDistance(string name) => Math.Max(1, name.Length / 4);

    // The Levenshtein distance, ignoring case, with one row of the table at a time. A name that differs only in case is 0.
    private static int Distance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];
        for (var column = 0; column <= second.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= first.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= second.Length; column++)
            {
                var same = char.ToUpperInvariant(first[row - 1]) == char.ToUpperInvariant(second[column - 1]);
                current[column] = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1), previous[column - 1] + (same ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }
}
