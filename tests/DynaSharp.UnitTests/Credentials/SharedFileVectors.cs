using System.Text;
using System.Text.Json;
using DynaSharp.Configuration;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>One data row of a vector test: a case, and which file or profile of it.</summary>
/// <param name="Case">The name of the case.</param>
/// <param name="Detail">The file (<c>credentials_file</c>, <c>config_file</c>) or the profile; empty if the whole case is meant.</param>
public readonly record struct VectorArgument(string Case, string Detail)
{
    /// <summary>Gets the case in the JSON.</summary>
    public JsonElement Vector => SharedFileVectors.Cases[Case];

    /// <summary>Gets the lookup of a profile in the JSON.</summary>
    public JsonElement Lookup => FindLookup(Vector, Detail);

    /// <summary>Returns the text that test runners show for the row.</summary>
    /// <returns>The case, and the detail if there is one.</returns>
    public override string ToString() => Detail.Length == 0 ? Case : $"{Case} / {Detail}";

    private static JsonElement FindLookup(JsonElement vector, string profile) =>
        vector.GetProperty("lookups").EnumerateArray().Single(l => l.GetProperty("profile").GetString() == profile);
}

/// <summary>
/// The expected results of botocore for the shared AWS files, from <c>SharedFileVectors.json</c>. Regenerate the file with
/// <c>generate_shared_file_vectors.py</c>; it is an embedded resource, so no test reads the disk.
/// </summary>
internal static class SharedFileVectors
{
    public const string CredentialsPath = "/vectors/credentials";
    public const string ConfigPath = "/vectors/config";

    private static readonly JsonDocument Document = Load();

    public static IReadOnlyDictionary<string, JsonElement> Cases { get; } = Document.RootElement
        .GetProperty("cases")
        .EnumerateArray()
        .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

    /// <summary>Gets one row per profile that botocore was asked about.</summary>
    public static IEnumerable<VectorArgument> Lookups { get; } = Cases.SelectMany(
        c => c.Value.GetProperty("lookups").EnumerateArray().Select(l => new VectorArgument(c.Key, l.GetProperty("profile").GetString()!)));

    /// <summary>Selects the cases for which <paramref name="predicate"/> holds.</summary>
    /// <param name="predicate">The condition on the case.</param>
    /// <returns>One row per case.</returns>
    public static IEnumerable<VectorArgument> CasesWhere(Func<JsonElement, bool> predicate) =>
        Cases.Where(c => predicate(c.Value)).Select(c => new VectorArgument(c.Key, string.Empty));

    /// <summary>The environment that makes a provider read the files of the case from <see cref="CredentialsPath"/> and <see cref="ConfigPath"/>.</summary>
    /// <param name="profile">The value of <c>AWS_PROFILE</c>, or <see langword="null"/> for none.</param>
    /// <returns>The environment.</returns>
    public static Func<string, string?> Environment(string? profile = null) => name => name switch
    {
        "AWS_SHARED_CREDENTIALS_FILE" => CredentialsPath,
        "AWS_CONFIG_FILE" => ConfigPath,
        "AWS_PROFILE" => profile,
        _ => null,
    };

    /// <summary>The files of the case.</summary>
    /// <param name="vector">The case.</param>
    /// <returns>A file reader that serves <see cref="CredentialsPath"/> and <see cref="ConfigPath"/>.</returns>
    public static ReadTextFile Files(JsonElement vector) => path => path switch
    {
        CredentialsPath => vector.GetProperty("credentials_file").GetString(),
        ConfigPath => vector.GetProperty("config_file").GetString(),
        _ => null,
    };

    /// <summary>Describes the options of a section from botocore's JSON, in the format of <see cref="Describe(IReadOnlyDictionary{string, IniValue})"/>.</summary>
    /// <param name="settings">The JSON object of the section.</param>
    /// <returns>The description.</returns>
    public static string Describe(JsonElement settings)
    {
        var entries = settings.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Value.ValueKind == JsonValueKind.String
                ? $"{p.Name}={Quote(p.Value.GetString()!)}"
                : $"{p.Name}={{{DescribeBlock(p.Value.EnumerateObject().ToDictionary(b => b.Name, b => b.Value.GetString()!))}}}");
        return string.Join("; ", entries);
    }

    /// <summary>Describes the options of a section, with blocks in braces. Sorted, so that the order does not matter.</summary>
    /// <param name="settings">The options.</param>
    /// <returns>The description.</returns>
    public static string Describe(IReadOnlyDictionary<string, IniValue> settings)
    {
        var entries = settings
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value.Block is null
                ? $"{p.Key}={Quote(p.Value.Text)}"
                : $"{p.Key}={{{DescribeBlock(p.Value.Block)}}}");
        return string.Join("; ", entries);
    }

    private static string DescribeBlock(IReadOnlyDictionary<string, string> block) =>
        string.Join(", ", block.OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => $"{b.Key}={Quote(b.Value)}"));

    private static string Quote(string text) => "\"" + text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static JsonDocument Load()
    {
        using var stream = typeof(SharedFileVectors).Assembly.GetManifestResourceStream("SharedFileVectors.json")
            ?? throw new InvalidOperationException("SharedFileVectors.json is not embedded in the test assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
