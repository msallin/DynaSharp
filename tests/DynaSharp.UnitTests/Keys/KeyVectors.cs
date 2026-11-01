using System.Text;
using System.Text.Json;

namespace DynaSharp.UnitTests.Keys;

/// <summary>Loads <c>key_vectors.json</c>, written by <c>generate_key_vectors.py</c>, which is an embedded resource: no test reads the disk.</summary>
internal static class KeyVectors
{
    private static readonly JsonDocument Document = Load();

    public static IEnumerable<KeyVector> Writes(bool error) => Cases("write").Where(v => v.IsError == error);

    public static IEnumerable<KeyVector> WritesOfKind(string kind, bool error) => Writes(error).Where(v => v.Kind == kind);

    public static IEnumerable<KeyVector> Reads(bool accept) => Cases("read").Where(v => v.Accept == accept);

    public static IEnumerable<KeyVector> ReadsOfKind(string kind, bool accept) => Reads(accept).Where(v => v.Kind == kind);

    public static IEnumerable<KeyVector> Composites(bool error) => Cases("composite").Where(v => v.IsError == error);

    public static IEnumerable<KeyVector> CompositeReads(bool accept) => Cases("composite_read").Where(v => v.Accept == accept);

    public static int Count(string section) => Document.RootElement.GetProperty(section).GetArrayLength();

    private static IEnumerable<KeyVector> Cases(string section) =>
        Document.RootElement.GetProperty(section).EnumerateArray().Select(c => new KeyVector(c));

    private static JsonDocument Load()
    {
        using var stream = typeof(KeyVectors).Assembly.GetManifestResourceStream("key_vectors.json")
            ?? throw new InvalidOperationException("key_vectors.json is not embedded in the test assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
