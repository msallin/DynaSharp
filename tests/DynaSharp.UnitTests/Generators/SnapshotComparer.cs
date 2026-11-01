using System.Reflection;
using System.Text;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// Compares generated code with the checked-in <c>*.verified.cs</c> next to the tests. On a mismatch it writes <c>*.received.cs</c>
/// beside it and fails with the first differing lines. Set <c>DYNASHARP_ACCEPT_SNAPSHOTS=1</c> to write the verified files instead.
/// Text is compared with LF line endings, and the verified files are kept LF by .gitattributes.
/// </summary>
internal static class SnapshotComparer
{
    public const string AcceptVariable = "DYNASHARP_ACCEPT_SNAPSHOTS";

    private const int DiffLines = 5;

    private static readonly UTF8Encoding Utf8 = new(false);

    // The path is recorded at build time. CallerFilePath would be rewritten by deterministic source paths under CI.
    public static string ProjectDirectory { get; } =
        typeof(SnapshotComparer).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ProjectDirectory").Value!;

    /// <summary>Verifies <paramref name="actual"/> against <c>Snapshots/{name}.verified.cs</c> and fails the running test on a difference.</summary>
    public static void Verify(string name, string actual)
    {
        var failure = Compare(SnapshotDirectory(), name, actual, Environment.GetEnvironmentVariable(AcceptVariable) == "1");
        if (failure is not null)
        {
            Fail.Test(failure);
        }
    }

    /// <summary>Returns null when the snapshot matches (or was accepted), otherwise a message with the difference.</summary>
    public static string? Compare(string directory, string name, string actual, bool accept)
    {
        var text = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
        var verified = Path.Combine(directory, name + ".verified.cs");
        var received = Path.Combine(directory, name + ".received.cs");
        var expected = File.Exists(verified) && !accept ? File.ReadAllText(verified) : null;

        if (accept || expected == text)
        {
            if (accept)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(verified, text, Utf8);
            }

            File.Delete(received);
            return null;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(received, text, Utf8);
        return $"Snapshot '{name}' differs. Received: {received}. To accept it, set {AcceptVariable}=1.\n{Diff(expected, text)}";
    }

    private static string Diff(string? expected, string actual)
    {
        if (expected is null)
        {
            return "There is no verified file yet.";
        }

        var old = expected.Split('\n');
        var now = actual.Split('\n');
        var line = 0;
        while (line < old.Length && line < now.Length && old[line] == now[line])
        {
            line++;
        }

        var shown = new StringBuilder($"First difference at line {line + 1} ({old.Length} lines verified, {now.Length} received):\n");
        foreach (var removed in old.Skip(line).Take(DiffLines))
        {
            shown.Append("- ").Append(removed.Replace("\r", "\r", StringComparison.Ordinal)).Append('\n');
        }

        foreach (var added in now.Skip(line).Take(DiffLines))
        {
            shown.Append("+ ").Append(added).Append('\n');
        }

        return shown.ToString();
    }

    private static string SnapshotDirectory() => Path.Combine(ProjectDirectory, "Generators", "Snapshots");
}
