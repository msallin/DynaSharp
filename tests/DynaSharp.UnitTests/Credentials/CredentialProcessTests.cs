using System.Text;
using DynaSharp.Credentials;
using TUnit.Core.Enums;

namespace DynaSharp.UnitTests.Credentials;

/// <summary>
/// Runs real programs: a batch file on Windows and a script of <c>/bin/sh</c> elsewhere, written by each test into a folder of its own.
/// </summary>
public sealed class CredentialProcessTests
{
    [Test]
    public async Task Runner_ProgramWritesAndExits_ReturnsTheOutputAndTheExitCode()
    {
        // Arrange
        using var program = FakeProgram.Create(output: """{"Version":1}""", exitCode: 3);

        // Act
        var result = await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That((result.End, result.ExitCode, Encoding.UTF8.GetString(result.Output).Trim())).IsEqualTo((ProcessEnd.Exited, 3, """{"Version":1}"""));
    }

    [Test]
    public async Task Runner_ProgramWritesToStandardError_DropsIt()
    {
        // Arrange (the error output is drained, so a program that writes much of it does not block, and none of it comes back)
        using var program = FakeProgram.Create(output: "out", exitCode: 0, error: "secret-on-standard-error");

        // Act
        var result = await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(result.Output)).DoesNotContain("secret-on-standard-error", StringComparison.Ordinal);
    }

    [Test]
    public async Task Runner_ArgumentsWithSpacesAndQuotes_ArriveAsOneArgumentEach()
    {
        // Arrange
        using var program = FakeProgram.EchoingFirstArgument();

        // Act
        var result = await CredentialProcess.Runner(program.FileName, [.. program.Arguments, "a b"], TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That(Encoding.UTF8.GetString(result.Output).Trim()).IsEqualTo("[a b]");
    }

    [Test]
    public async Task Runner_ProgramDoesNotExist_ReturnsNotStarted()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "dynasharp-" + Guid.NewGuid().ToString("N"), "no-such-program");

        // Act
        var result = await CredentialProcess.Runner(missing, [], TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That((result.End, result.Error is System.ComponentModel.Win32Exception, result.Output.Length)).IsEqualTo((ProcessEnd.NotStarted, true, 0));
    }

    [Test]
    public async Task Runner_ProgramRunsPastTheTimeout_IsStoppedAndReturnsTimedOut()
    {
        // Arrange
        using var program = FakeProgram.Hanging();

        // Act
        var result = await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMilliseconds(500), TimeProvider.System);

        // Assert
        await Assert.That((result.End, result.Output.Length)).IsEqualTo((ProcessEnd.TimedOut, 0));
    }

    [Test]
    [ExcludeOn(OS.Windows)]
    public async Task Runner_ProgramRunsPastTheTimeout_StopsTheProcessesItStarted()
    {
        // Arrange
        using var program = FakeProgram.WithAChild(string.Empty);

        // Act
        await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMilliseconds(500), TimeProvider.System);

        // Assert
        await Assert.That(await HasEndedAsync(program.ChildIdFile)).IsTrue();
    }

    [Test]
    [ExcludeOn(OS.Windows)]
    public async Task Runner_ProgramWritesMoreThanTheLimit_StopsTheProcessesItStarted()
    {
        // Arrange
        using var program = FakeProgram.WithAChild(new string('x', CredentialProcess.MaxOutputBytes + 1));

        // Act
        await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That(await HasEndedAsync(program.ChildIdFile)).IsTrue();
    }

    [Test]
    public async Task Runner_ProgramReadsItsInput_ReadsTheEndOfItAtOnce()
    {
        // Arrange
        using var program = FakeProgram.EchoingInput();

        // Act
        var result = await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromSeconds(30), TimeProvider.System);

        // Assert
        await Assert.That((result.End, Encoding.UTF8.GetString(result.Output).Trim())).IsEqualTo((ProcessEnd.Exited, "[]"));
    }

    [Test]
    public async Task Runner_ProgramWritesMoreThanTheLimit_IsStoppedAndReturnsTooMuchOutput()
    {
        // Arrange
        using var program = FakeProgram.Create(output: new string('x', CredentialProcess.MaxOutputBytes + 1), exitCode: 0);

        // Act
        var result = await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That((result.End, result.Output.Length)).IsEqualTo((ProcessEnd.TooMuchOutput, 0));
    }

    [Test]
    public async Task Runner_ProgramWritesExactlyTheLimit_ReturnsAllOfIt()
    {
        // Arrange (the output file is written without a line feed, and the program adds none)
        using var program = FakeProgram.Create(output: new string('x', CredentialProcess.MaxOutputBytes), exitCode: 0);

        // Act
        var result = await CredentialProcess.Runner(program.FileName, program.Arguments, TimeSpan.FromMinutes(1), TimeProvider.System);

        // Assert
        await Assert.That((result.End, result.Output.Length)).IsEqualTo((ProcessEnd.Exited, CredentialProcess.MaxOutputBytes));
    }

    [Test]
    public async Task ToString_Result_DoesNotShowTheOutput()
    {
        // Arrange
        var result = new ProcessResult(ProcessEnd.Exited, 0, Encoding.UTF8.GetBytes(CredentialsTestSupport.SecretAccessKey));

        // Act
        var text = result.ToString();

        // Assert
        await Assert.That(text).IsEqualTo(nameof(ProcessResult));
    }

    // Whether the process whose id the file holds ends within ten seconds. A killed process that nobody has reaped yet is a zombie, which counts as ended.
    private static async Task<bool> HasEndedAsync(string idFile)
    {
        var stat = $"/proc/{(await File.ReadAllTextAsync(idFile)).Trim()}/stat";
        var deadline = TimeProvider.System.GetTimestamp() + (10 * TimeProvider.System.TimestampFrequency);
        while (TimeProvider.System.GetTimestamp() < deadline)
        {
            string text;
            try
            {
                text = await File.ReadAllTextAsync(stat);
            }
            catch (IOException)
            {
                return true;
            }

            // The state follows the name, which is in parentheses: "1234 (sleep) Z ...".
            if (text[(text.LastIndexOf(')', StringComparison.Ordinal) + 2)..].StartsWith('Z', StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Yield();
        }

        return false;
    }
}
