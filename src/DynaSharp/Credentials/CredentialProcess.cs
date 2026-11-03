using System.ComponentModel;
using System.Diagnostics;

namespace DynaSharp.Credentials;

/// <summary>Starts a program, waits for it and returns what it wrote to standard output. The seam through which <c>credential_process</c> runs, so that tests need no program.</summary>
/// <param name="fileName">The program, as <see cref="ProcessStartInfo.FileName"/> takes it: a path, or a name that the operating system looks up.</param>
/// <param name="arguments">The arguments, each passed as one argument whatever it contains.</param>
/// <param name="timeout">How long the program may run.</param>
/// <param name="timeProvider">The clock of the timeout.</param>
/// <returns>How the program ended. Never throws for a program that cannot be started, runs too long or writes too much.</returns>
internal delegate Task<ProcessResult> RunProcess(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, TimeProvider timeProvider);

/// <summary>How a run of a program ended.</summary>
internal enum ProcessEnd
{
    /// <summary>It exited. See <see cref="ProcessResult.ExitCode"/> and <see cref="ProcessResult.Output"/>.</summary>
    Exited,

    /// <summary>It could not be started: not found, or not allowed to run.</summary>
    NotStarted,

    /// <summary>It ran past the timeout and was stopped.</summary>
    TimedOut,

    /// <summary>It wrote more than <see cref="CredentialProcess.MaxOutputBytes"/> to standard output and was stopped.</summary>
    TooMuchOutput,
}

/// <summary>The outcome of <see cref="RunProcess"/>.</summary>
/// <param name="End">How it ended.</param>
/// <param name="ExitCode">The exit code, when <paramref name="End"/> is <see cref="ProcessEnd.Exited"/>.</param>
/// <param name="Output">What it wrote to standard output, when <paramref name="End"/> is <see cref="ProcessEnd.Exited"/>.</param>
/// <param name="Error">Why it could not be started, when <paramref name="End"/> is <see cref="ProcessEnd.NotStarted"/>.</param>
internal readonly record struct ProcessResult(ProcessEnd End, int ExitCode, byte[] Output, Exception? Error = null)
{
    /// <summary>Returns the name of the type: the generated text of a record would print the output, which holds the secret.</summary>
    /// <returns>The name of the type.</returns>
    public override string ToString() => nameof(ProcessResult);
}

/// <summary>Runs the program of a <c>credential_process</c> setting: without a shell, without input, and bounded in time and output.</summary>
/// <remarks>
/// Standard input is closed at once, so a program that asks something reads the end of its input instead of waiting until the timeout.
/// Standard error is read and dropped: it is never logged or put into a message, because a program may print a secret there. When the timeout
/// passes or the output grows too large, the program and every process it started are killed.
/// </remarks>
internal static class CredentialProcess
{
    /// <summary>The most a program may write to standard output. A credentials document is a few hundred bytes.</summary>
    public const int MaxOutputBytes = 64 * 1024;

    /// <summary>Gets the runner that starts real processes. One shared delegate, so that no call site converts a method group.</summary>
    public static RunProcess Runner { get; } = RunAsync;

    private static async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, TimeProvider timeProvider)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            // Null only for a process that UseShellExecute reuses.
            process = Process.Start(info)!;
        }
        catch (Win32Exception exception)
        {
            return new ProcessResult(ProcessEnd.NotStarted, 0, [], exception);
        }

        using (process)
        using (var timeoutSource = new CancellationTokenSource(timeout, timeProvider))
        {
            process.StandardInput.Close();

            // The reads take no token: a read of a pipe cannot be cancelled on every platform. The waits for them can, and once the
            // program is killed its pipes close and the reads end by themselves.
            var outputRead = ReadAtMostAsync(process.StandardOutput.BaseStream, MaxOutputBytes);
            var errorDrained = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            try
            {
                var output = await outputRead.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
                if (output is null)
                {
                    Stop(process);
                    return new ProcessResult(ProcessEnd.TooMuchOutput, 0, []);
                }

                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                await errorDrained.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
                return new ProcessResult(ProcessEnd.Exited, process.ExitCode, output);
            }
            catch (OperationCanceledException)
            {
                Stop(process);
                return new ProcessResult(ProcessEnd.TimedOut, 0, []);
            }
        }
    }

    // Returns null when the stream holds more than the limit.
    private static async Task<byte[]?> ReadAtMostAsync(Stream stream, int limit)
    {
        var buffer = new byte[limit + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length)).ConfigureAwait(false)) > 0)
        {
            length += read;
        }

        return length > limit ? null : buffer[..length];
    }

    private static void Stop(Process process) => process.Kill(entireProcessTree: true);
}
