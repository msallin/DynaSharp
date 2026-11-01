namespace DynaSharp.Configuration;

/// <summary>
/// A named profile of the shared AWS files, with the options of its section in the credentials file and in the config file.
/// </summary>
internal sealed class SharedProfile
{
    public SharedProfile(
        string name,
        IReadOnlyDictionary<string, IniValue>? credentialsFileValues,
        IReadOnlyDictionary<string, IniValue>? configFileValues)
    {
        Name = name;
        CredentialsFileValues = credentialsFileValues;
        ConfigFileValues = configFileValues;

        // The credentials file overrides the config file option by option, as botocore does when it builds its profile map.
        var merged = new Dictionary<string, IniValue>(StringComparer.Ordinal);
        foreach (var values in new[] { configFileValues, credentialsFileValues })
        {
            if (values is null)
            {
                continue;
            }

            foreach (var (option, value) in values)
            {
                merged[option] = value;
            }
        }

        Values = merged;
    }

    /// <summary>Gets the profile name, for example <c>default</c> or <c>dev</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the options of the section in the credentials file, or <see langword="null"/> if the file has none for this profile.</summary>
    public IReadOnlyDictionary<string, IniValue>? CredentialsFileValues { get; }

    /// <summary>Gets the options of the section in the config file, or <see langword="null"/> if the file has none for this profile.</summary>
    public IReadOnlyDictionary<string, IniValue>? ConfigFileValues { get; }

    /// <summary>Gets all options, where the credentials file wins over the config file for the same option.</summary>
    public IReadOnlyDictionary<string, IniValue> Values { get; }

    /// <summary>Gets the text of an option of one section, or <see langword="null"/> if it is not set, empty or a block.</summary>
    /// <param name="values">The options of the section.</param>
    /// <param name="option">The lower-case option name.</param>
    /// <returns>The text.</returns>
    public static string? GetText(IReadOnlyDictionary<string, IniValue> values, string option) =>
        values.TryGetValue(option, out var value) && value.Block is null && value.Text.Length > 0 ? value.Text : null;

    /// <summary>Gets the text of an option of the merged view, or <see langword="null"/> if it is not set, empty or a block.</summary>
    /// <param name="option">The lower-case option name.</param>
    /// <returns>The text.</returns>
    public string? GetText(string option) => GetText(Values, option);
}
