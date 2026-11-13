using System.Diagnostics;
using System.Text.RegularExpressions;
using DynaSharp.UnitTests.Generators;

namespace DynaSharp.UnitTests;

/// <summary>
/// Compiles a source with the compiler of the SDK the tests run on, against the assemblies of the test host, and gives its warnings and errors. The Roslyn package of the tests is the
/// floor of the generator (C# 14), which knows no unions, so what a consumer in C# 15 sees of a union is asked of the compiler a consumer builds with.
/// </summary>
internal static partial class SdkCompiler
{
    /// <summary>The ids of the warnings and errors, with the analyzers and generators of the assemblies given, in order, each with the pattern its message names as not covered, if any: <c>CS8509 'Shop.Beta'</c>.</summary>
    public static async Task<string[]> DiagnoseAsync(string source, params string[] analyzers)
    {
        using var directory = new TempDirectory();
        directory.Write("Source.cs", source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => $"/r:\"{path}\"");
        File.WriteAllLines(directory.Combine("csc.rsp"), [
            "/nostdlib+", "/nologo", "/target:library", "/langversion:latest", "/nullable:enable",
            $"/out:\"{directory.Combine("Source.dll")}\"", .. references, .. analyzers.Select(static path => $"/analyzer:\"{path}\""), $"\"{directory.Combine("Source.cs")}\""]);

        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        var sdk = Directory.EnumerateDirectories(Path.Combine(root, "sdk"))
            .Where(static sdk => File.Exists(CompilerOf(sdk)))
            .MaxBy(static sdk => Version.TryParse(Path.GetFileName(sdk).Split('-')[0], out var version) ? version : new Version())!;
        var start = new ProcessStartInfo(Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), ["exec", CompilerOf(sdk), "/noconfig", "@" + directory.Combine("csc.rsp")])
        {
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return [.. Diagnostic().Matches(output).Select(static match => (match.Groups["id"].Value + " " + match.Groups["pattern"].Value).TrimEnd())];
    }

    private static string CompilerOf(string sdk) => Path.Combine(sdk, "Roslyn", "bincore", "csc.dll");

    [GeneratedRegex(@"(?:warning|error) (?<id>[A-Z]+\d+):[^\r\n]*?(?:the pattern (?<pattern>'[^']*') is not covered[^\r\n]*)?$", RegexOptions.Multiline)]
    private static partial Regex Diagnostic();
}
