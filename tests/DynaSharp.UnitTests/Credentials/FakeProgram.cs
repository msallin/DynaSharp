using System.Text;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// A program for the tests of <c>credential_process</c> that runs on Windows and on Linux: a batch file run by <c>cmd.exe</c> on Windows, a script run
/// by <c>/bin/sh</c> elsewhere. It lives in a folder of its own, which <see cref="Dispose"/> deletes.
/// </summary>
internal sealed class FakeProgram : IDisposable
{
    private FakeProgram(string directory, string fileName, string[] arguments)
    {
        Directory = directory;
        FileName = fileName;
        Arguments = arguments;
    }

    public string Directory { get; }

    /// <summary>Gets the program to start.</summary>
    public string FileName { get; }

    /// <summary>Gets the arguments that run the script.</summary>
    public string[] Arguments { get; }

    /// <summary>Gets the line of a <c>credential_process</c> setting that runs it: no quotes, because the temporary folders of the tests have no spaces.</summary>
    public string CommandLine => string.Join(' ', [FileName, .. Arguments]);

    /// <summary>A program that writes <paramref name="output"/> to standard output as it is, <paramref name="error"/> to standard error, and exits with <paramref name="exitCode"/>.</summary>
    public static FakeProgram Create(string output, int exitCode, string error = "error")
    {
        var directory = CreateDirectory();
        var outputFile = Path.Combine(directory, "out.txt");
        File.WriteAllText(outputFile, output);
        return OperatingSystem.IsWindows()
            ? Script(directory, $"@echo off\r\ntype \"{outputFile}\"\r\necho {error} 1>&2\r\nexit /b {exitCode}\r\n")
            : Script(directory, $"cat '{outputFile}'\necho {error} 1>&2\nexit {exitCode}\n");
    }

    /// <summary>A program that writes its first argument in brackets.</summary>
    public static FakeProgram EchoingFirstArgument() => OperatingSystem.IsWindows()
        ? Script(CreateDirectory(), "@echo off\r\necho [%~1]\r\n")
        : Script(CreateDirectory(), "echo \"[$1]\"\n");

    /// <summary>A program that runs for a minute without writing anything.</summary>
    public static FakeProgram Hanging() => OperatingSystem.IsWindows()
        ? Script(CreateDirectory(), "@echo off\r\nping -n 60 127.0.0.1 >nul\r\n")
        : Script(CreateDirectory(), "sleep 60\n");

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A program that was killed may still hold its folder for a moment on Windows; the folder is in the temporary folder.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dynasharp-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    private static FakeProgram Script(string directory, string text)
    {
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(directory, "program.cmd");
            File.WriteAllText(batch, text, Encoding.ASCII);
            return new FakeProgram(directory, batch, []);
        }

        var script = Path.Combine(directory, "program.sh");
        File.WriteAllText(script, text, Encoding.ASCII);
        return new FakeProgram(directory, "/bin/sh", [script]);
    }
}
