using System.Text;
using System.Text.Json;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>One data row of a vector test: an entry of a section of <c>sts_vectors.json</c>.</summary>
/// <param name="Section">The section, for example <c>requests</c>.</param>
/// <param name="Index">The position in the section.</param>
public readonly record struct StsVector(string Section, int Index)
{
    /// <summary>Gets the entry in the JSON.</summary>
    public JsonElement Entry => StsVectors.Section(Section)[Index];

    /// <summary>Returns the text that test runners show for the row.</summary>
    /// <returns>The name of the entry, or its region and mode.</returns>
    public override string ToString() => Entry.TryGetProperty("name", out var name)
        ? name.GetString()!
        : $"{Entry.GetProperty("region").GetString()} {Entry.GetProperty("mode").GetString() ?? "unset"}";

    /// <summary>Reads a string of the entry.</summary>
    /// <param name="property">The name of the property.</param>
    /// <returns>The value.</returns>
    public string Text(string property) => Entry.GetProperty(property).GetString()!;
}

/// <summary>
/// The expected results of botocore for <c>AssumeRoleWithWebIdentity</c>, from <c>sts_vectors.json</c>. Regenerate the file with
/// <c>generate_sts_vectors.py</c>; it is an embedded resource, so no test reads the disk.
/// </summary>
internal static class StsVectors
{
    private static readonly JsonDocument Document = Load();

    /// <summary>Gets the error codes the model of botocore lists for the operation.</summary>
    public static IEnumerable<string> ModeledErrors => Document.RootElement.GetProperty("modeled_errors").EnumerateObject().Select(static error => error.Name);

    /// <summary>Gets a section as a list.</summary>
    /// <param name="section">The name of the section.</param>
    /// <returns>The entries.</returns>
    public static IReadOnlyList<JsonElement> Section(string section) => [.. Document.RootElement.GetProperty(section).EnumerateArray()];

    /// <summary>Gets one row per entry of a section.</summary>
    /// <param name="section">The name of the section.</param>
    /// <returns>The rows.</returns>
    public static IEnumerable<StsVector> Rows(string section) => Enumerable.Range(0, Section(section).Count).Select(index => new StsVector(section, index));

    private static JsonDocument Load()
    {
        using var stream = typeof(StsVectors).Assembly.GetManifestResourceStream("sts_vectors.json")
            ?? throw new InvalidOperationException("sts_vectors.json is not embedded in the test assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
