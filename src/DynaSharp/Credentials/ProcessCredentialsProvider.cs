using System.Globalization;
using Microsoft.Extensions.Logging;

namespace DynaSharp.Credentials;

/// <summary>
/// Supplies the credentials that the program of the <c>credential_process</c> setting of a profile writes to standard output, the
/// way botocore's <c>ProcessProvider</c> does. <see cref="ProfileCredentialsProvider"/> creates it.
/// </summary>
/// <remarks>
/// <para>
/// The setting is split into a program and its arguments by the rules of <see cref="CommandLine"/>, and the program starts without a shell, so
/// pipes, redirections and variables of a shell do not work unless the setting starts a shell itself. The program must write one JSON object and exit
/// with code 0:
/// <code>
/// {"Version": 1, "AccessKeyId": "ASIA...", "SecretAccessKey": "...", "SessionToken": "...", "Expiration": "2026-11-03T12:00:00Z"}
/// </code>
/// <c>Version</c> must be 1, <c>AccessKeyId</c> and <c>SecretAccessKey</c> are required, <c>SessionToken</c> and <c>Expiration</c> are optional,
/// and other members, such as <c>AccountId</c>, are ignored. Credentials with an <c>Expiration</c> are refreshed before it, by running the program
/// again, as in <see cref="RefreshingCredentialsCache"/>; credentials without one are kept until DynamoDB rejects them.
/// </para>
/// <para>
/// The program gets <see cref="Timeout"/> to finish, and is killed with what it started after that. Its output is a secret: no message, log or
/// trace contains what it wrote to standard output or standard error, and a failure names the profile and the exit code instead.
/// </para>
/// </remarks>
internal sealed class ProcessCredentialsProvider : IRefreshableCredentialsProvider
{
    /// <summary>
    /// How long the program may run. botocore waits without a limit; this one stays below the default
    /// <see cref="DynamoDbClientOptions.AttemptTimeout"/> of 30 seconds, so that a client reports a program that hangs, not a timeout of its own.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private const string Setting = "credential_process";

    private readonly string _profileName;
    private readonly string _commandLine;
    private readonly bool _windows;
    private readonly TimeSpan _timeout;
    private readonly CredentialsDependencies _dependencies;
    private readonly string _source;
    private readonly RefreshingCredentialsCache _cache;

    /// <summary>Initializes a new instance of the <see cref="ProcessCredentialsProvider"/> class.</summary>
    /// <param name="profileName">The profile that holds the setting, for messages.</param>
    /// <param name="commandLine">The value of the setting.</param>
    /// <param name="windows">Whether to split the command line by the rules of Windows; see <see cref="CommandLine.Split"/>.</param>
    /// <param name="timeout">How long the program may run; <see cref="Timeout"/> outside of tests.</param>
    /// <param name="dependencies">What the provider runs the program with.</param>
    public ProcessCredentialsProvider(string profileName, string commandLine, bool windows, TimeSpan timeout, CredentialsDependencies dependencies)
    {
        _profileName = profileName;
        _commandLine = commandLine;
        _windows = windows;
        _timeout = timeout;
        _dependencies = dependencies;
        var logger = dependencies.LoggerFactory.CreateLogger<ProcessCredentialsProvider>();
        _source = $"{Setting} of the profile '{profileName}'";
        _cache = new RefreshingCredentialsCache(_source, dependencies.TimeProvider, logger, FetchAsync);
    }

    /// <inheritdoc/>
    public async ValueTask<AwsCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var outcome = await _cache.GetAsync(cancellationToken).ConfigureAwait(false);
        return outcome.GetCredentials(_source);
    }

    /// <inheritdoc/>
    public bool TryInvalidate(AwsCredentials rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        _cache.Drop(rejected);
        return true;
    }

    // Input:  {"Version":1,"AccessKeyId":"ASIA...","SecretAccessKey":"...","SessionToken":"...","Expiration":"2026-11-03T12:00:00Z"}
    // Result: the four fields and whether Version is the number 1. A member of another type counts as missing.
    private static bool TryParse(byte[] json, out Document document)
    {
        if (CredentialsJson.TryRead(json) is not { } members)
        {
            document = default;
            return false;
        }

        document = new Document(
            members.TryGetDouble("Version", out var version) && version == 1,
            members.GetString("AccessKeyId"),
            members.GetString("SecretAccessKey"),
            members.GetString("SessionToken"),
            members.GetString("Expiration"));
        return true;
    }

    private CredentialsUnavailableException Failure(string reason, Exception? inner = null) =>
        new($"The {Setting} of the profile '{_profileName}' {reason}.", inner);

    private async ValueTask<CredentialsOutcome> FetchAsync()
    {
        var words = CommandLine.Split(_commandLine, _windows)
            ?? throw Failure("cannot be split into a program and its arguments: a quote is not closed, or it ends with a backslash");
        if (words.Length == 0 || words[0].Length == 0)
        {
            throw Failure("names no program");
        }

        var result = await _dependencies.RunProcess(words[0], words[1..], _timeout, _dependencies.TimeProvider).ConfigureAwait(false);
        switch (result.End)
        {
            case ProcessEnd.NotStarted:
                throw Failure("could not be started: the program was not found, or may not be run", result.Error);
            case ProcessEnd.TimedOut:
                throw Failure(string.Create(CultureInfo.InvariantCulture, $"did not finish in {_timeout.TotalSeconds} seconds and was stopped"));
            case ProcessEnd.TooMuchOutput:
                throw Failure($"wrote more than {CredentialProcess.MaxOutputBytes / 1024} KB to standard output and was stopped");
        }

        if (result.ExitCode != 0)
        {
            throw Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"exited with code {result.ExitCode}. What it wrote is not shown, because it may hold a secret: run the command in a shell to see its error"));
        }

        if (!TryParse(result.Output, out var document))
        {
            throw Failure("did not write a JSON object to standard output");
        }

        if (!document.IsVersion1)
        {
            throw Failure("did not write \"Version\": 1, the only version there is");
        }

        if (string.IsNullOrEmpty(document.AccessKeyId) || string.IsNullOrEmpty(document.SecretAccessKey))
        {
            throw Failure("did not write both AccessKeyId and SecretAccessKey");
        }

        DateTimeOffset? expiration = null;
        if (document.Expiration is { } text)
        {
            expiration = CredentialsJson.ParseTime(text) ?? throw Failure("wrote an Expiration that is not a timestamp");
        }

        return CredentialsOutcome.Found(new AwsCredentials(document.AccessKeyId, document.SecretAccessKey, document.SessionToken, expiration));
    }

    // Never formatted: its generated ToString would print the secret.
    private readonly record struct Document(bool IsVersion1, string? AccessKeyId, string? SecretAccessKey, string? SessionToken, string? Expiration);
}
