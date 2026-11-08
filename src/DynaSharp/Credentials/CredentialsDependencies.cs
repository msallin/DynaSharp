using DynaSharp.Configuration;
using DynaSharp.Retries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynaSharp.Credentials;

/// <summary>
/// What the providers that a profile builds read, start, call and log with, in one place: a profile may chain several of them, and each one
/// takes the same seams, so that a test replaces all of them at once.
/// </summary>
/// <param name="GetEnvironmentVariable">Reads an environment variable.</param>
/// <param name="ReadFile">Reads a file.</param>
/// <param name="WriteFile">Replaces the content of a file, the cached SSO token after a refresh.</param>
/// <param name="RunProcess">Runs the program of a <c>credential_process</c>.</param>
/// <param name="Handler">Answers for the credential endpoints, or <see langword="null"/> to call them.</param>
/// <param name="TimeProvider">The clock of expirations and timeouts.</param>
/// <param name="LoggerFactory">Creates the loggers of the providers.</param>
internal sealed record CredentialsDependencies(
    Func<string, string?> GetEnvironmentVariable,
    ReadTextFile ReadFile,
    WriteTextFile WriteFile,
    RunProcess RunProcess,
    HttpMessageHandler? Handler,
    TimeProvider TimeProvider,
    ILoggerFactory LoggerFactory)
{
    /// <summary>Gets the dependencies of a provider that an application creates: this process, the disk, the network, the system clock and no logging.</summary>
    public static CredentialsDependencies Default { get; } = new(
        ProcessEnvironment.GetVariable,
        SharedConfigFiles.DiskReader,
        TextFiles.DiskWriter,
        CredentialProcess.Runner,
        null,
        new ClockSkewCorrection(TimeProvider.System),
        NullLoggerFactory.Instance);
}
