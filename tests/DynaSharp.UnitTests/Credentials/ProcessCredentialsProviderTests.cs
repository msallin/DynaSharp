using System.Text;
using DynaSharp.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.UnitTests.Credentials.CredentialsTestSupport;

namespace DynaSharp.UnitTests.Credentials;

public sealed class ProcessCredentialsProviderTests
{
    private const string Profile = "dev";

    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    [Test]
    public async Task GetCredentialsAsync_AllFields_ReturnsThem()
    {
        // Arrange
        var runner = new FakeRunner(Json(expiration: Now.AddHours(1)));
        var provider = Create(runner);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That($"{credentials.AccessKeyId}|{credentials.SecretAccessKey}|{credentials.SessionToken}|{credentials.Expiration:O}")
            .IsEqualTo($"{AccessKeyId}|{SecretAccessKey}|{SessionToken}|{Now.AddHours(1):O}");
    }

    [Test]
    public async Task GetCredentialsAsync_NoSessionTokenAndNoExpiration_ReturnsLongTermKeys()
    {
        // Arrange (a wrapper of a vault that hands out the keys of a user)
        var runner = new FakeRunner($$"""{"Version":1,"AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}"}""");
        var provider = Create(runner);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.SessionToken, credentials.Expiration)).IsEqualTo((null, null));
    }

    [Test]
    public async Task GetCredentialsAsync_UnknownMembers_AreIgnored()
    {
        // Arrange (AccountId is a member of the format that DynaSharp has no use for)
        var runner = new FakeRunner($$"""{"Version":1,"AccountId":"123456789012","Nested":{"a":[1,2]},"AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}"}""");
        var provider = Create(runner);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    public async Task GetCredentialsAsync_VersionAsDecimalOne_IsAccepted()
    {
        // Arrange (Python compares 1.0 == 1 as true, so botocore accepts it)
        var runner = new FakeRunner($$"""{"Version":1.0,"AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}"}""");
        var provider = Create(runner);

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.AccessKeyId).IsEqualTo(AccessKeyId);
    }

    [Test]
    [Arguments("""{"AccessKeyId":"a","SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":2,"AccessKeyId":"a","SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":0,"AccessKeyId":"a","SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":"1","AccessKeyId":"a","SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":1e400,"AccessKeyId":"a","SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":null,"AccessKeyId":"a","SecretAccessKey":"s"}""")]
    public async Task GetCredentialsAsync_VersionIsNotOne_FailsNamingTheVersion(string json)
    {
        // Arrange
        var provider = Create(new FakeRunner(json));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The credential_process of the profile 'dev' did not write \"Version\": 1, the only version there is.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("""{"Version":1,"SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":1,"AccessKeyId":"a"}""")]
    [Arguments("""{"Version":1,"AccessKeyId":"","SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":1,"AccessKeyId":"a","SecretAccessKey":""}""")]
    [Arguments("""{"Version":1,"AccessKeyId":7,"SecretAccessKey":"s"}""")]
    [Arguments("""{"Version":1,"AccessKeyId":"a","SecretAccessKey":{"x":1}}""")]
    [Arguments("""{"Version":1,"AccessKeyId":null,"SecretAccessKey":"s"}""")]
    public async Task GetCredentialsAsync_KeyMissing_FailsNamingTheMembers(string json)
    {
        // Arrange
        var provider = Create(new FakeRunner(json));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The credential_process of the profile 'dev' did not write both AccessKeyId and SecretAccessKey.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("")]
    [Arguments("not json")]
    [Arguments("[1,2]")]
    [Arguments("\"text\"")]
    [Arguments("""{"Version":1,"AccessKeyId":"a" """)]
    public async Task GetCredentialsAsync_OutputIsNoJsonObject_Fails(string output)
    {
        // Arrange
        var provider = Create(new FakeRunner(output));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The credential_process of the profile 'dev' did not write a JSON object to standard output.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_StringThatIsNotUtf8_FailsAsNoJsonObject()
    {
        // Arrange (the reader notices the bad bytes only when it decodes the string)
        byte[] output = [.. """{"Version":1,"AccessKeyId":" """u8.ToArray()[..^1], 0xC3, 0x28, .. "\"}"u8.ToArray()];
        var provider = Create(new FakeRunner(new ProcessResult(ProcessEnd.Exited, 0, output)));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("did not write a JSON object", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_SecretInOutputOfAFailure_IsNotInTheMessage()
    {
        // Arrange (a document with the secret, but of the wrong version)
        var provider = Create(new FakeRunner($$"""{"Version":2,"AccessKeyId":"{{AccessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}"}"""));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That(exception.Message).DoesNotContain(SecretAccessKey, StringComparison.Ordinal);
    }

    [Test]
    [Arguments("tomorrow")]
    [Arguments("")]
    public async Task GetCredentialsAsync_ExpirationIsNoTimestamp_Fails(string expiration)
    {
        // Arrange
        var provider = Create(new FakeRunner($$"""{"Version":1,"AccessKeyId":"a","SecretAccessKey":"s","Expiration":"{{expiration}}"}"""));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>().WithMessageContaining("wrote an Expiration that is not a timestamp", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_ExpirationWithOffset_IsConvertedToUtc()
    {
        // Arrange
        var provider = Create(new FakeRunner($$"""{"Version":1,"AccessKeyId":"a","SecretAccessKey":"s","Expiration":"2026-10-31T15:34:56+02:00"}"""));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(credentials.Expiration).IsEqualTo(new DateTimeOffset(2026, 10, 31, 13, 34, 56, TimeSpan.Zero));
    }

    [Test]
    public async Task GetCredentialsAsync_ExpirationInThePast_FailsAsAlreadyExpired()
    {
        // Arrange
        var provider = Create(new FakeRunner(Json(expiration: Now.AddMinutes(-1))));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessageContaining("credential_process of the profile 'dev': it returned credentials that are already expired", StringComparison.Ordinal);
    }

    [Test]
    [Arguments(1)]
    [Arguments(255)]
    [Arguments(-1)]
    [Arguments(int.MinValue)]
    public async Task GetCredentialsAsync_NonZeroExitCode_FailsNamingTheProfileAndTheCode(int exitCode)
    {
        // Arrange
        var provider = Create(new FakeRunner(new ProcessResult(ProcessEnd.Exited, exitCode, Encoding.UTF8.GetBytes(Json(Now.AddHours(1))))));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage($"The credential_process of the profile 'dev' exited with code {exitCode}. What it wrote is not shown, because it may hold a secret: run the command in a shell to see its error.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_NotStarted_FailsWithTheReasonAsInnerException()
    {
        // Arrange
        var reason = new System.ComponentModel.Win32Exception(2);
        var provider = Create(new FakeRunner(new ProcessResult(ProcessEnd.NotStarted, 0, [], reason)));

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, exception.InnerException)).IsEqualTo(("The credential_process of the profile 'dev' could not be started: the program was not found, or may not be run.", (Exception?)reason));
    }

    [Test]
    public async Task GetCredentialsAsync_TimedOut_FailsNamingTheTimeout()
    {
        // Arrange
        var provider = Create(new FakeRunner(new ProcessResult(ProcessEnd.TimedOut, 0, [])));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The credential_process of the profile 'dev' did not finish in 20 seconds and was stopped.", StringComparison.Ordinal);
    }

    [Test]
    public async Task GetCredentialsAsync_TooMuchOutput_FailsNamingTheLimit()
    {
        // Arrange
        var provider = Create(new FakeRunner(new ProcessResult(ProcessEnd.TooMuchOutput, 0, [])));

        // Act
        var act = async () => await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(act).ThrowsExactly<CredentialsUnavailableException>()
            .WithMessage("The credential_process of the profile 'dev' wrote more than 64 KB to standard output and was stopped.", StringComparison.Ordinal);
    }

    [Test]
    [Arguments("/usr/bin/creds \"unclosed", false)]
    [Arguments("/usr/bin/creds trailing\\", false)]
    [Arguments("creds.exe \"unclosed", true)]
    public async Task GetCredentialsAsync_CommandLineCannotBeSplit_FailsWithoutRunning(string commandLine, bool windows)
    {
        // Arrange
        var runner = new FakeRunner(Json(Now.AddHours(1)));
        var provider = Create(runner, commandLine, windows);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, runner.Runs.Count)).IsEqualTo(("The credential_process of the profile 'dev' cannot be split into a program and its arguments: a quote is not closed, or it ends with a backslash.", 0));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("\"\" --flag")]
    public async Task GetCredentialsAsync_NoProgram_FailsWithoutRunning(string commandLine)
    {
        // Arrange
        var runner = new FakeRunner(Json(Now.AddHours(1)));
        var provider = Create(runner, commandLine);

        // Act
        var exception = await TestClient.CaptureAsync<CredentialsUnavailableException>(async () => await provider.GetCredentialsAsync(CancellationToken.None));

        // Assert
        await Assert.That((exception.Message, runner.Runs.Count)).IsEqualTo(("The credential_process of the profile 'dev' names no program.", 0));
    }

    [Test]
    [Arguments("/usr/local/bin/creds --profile 'my dev' --json", false, "/usr/local/bin/creds|--profile|my dev|--json")]
    [Arguments("\"C:\\Program Files\\Creds\\creds.exe\" --profile dev", true, "C:\\Program Files\\Creds\\creds.exe|--profile|dev")]
    public async Task GetCredentialsAsync_CommandLine_RunsTheProgramWithItsArguments(string commandLine, bool windows, string expected)
    {
        // Arrange
        var runner = new FakeRunner(Json(Now.AddHours(1)));
        var provider = Create(runner, commandLine, windows);

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(string.Join('|', [runner.Runs[0].FileName, .. runner.Runs[0].Arguments])).IsEqualTo(expected);
    }

    [Test]
    public async Task GetCredentialsAsync_Run_PassesTheTimeoutAndTheClock()
    {
        // Arrange
        var time = new ManualTimeProvider(Now);
        var runner = new FakeRunner(Json(Now.AddHours(1)));
        var provider = new ProcessCredentialsProvider(Profile, "creds", false, TimeSpan.FromSeconds(3), Dependencies(runner, time));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((runner.Runs[0].Timeout, ReferenceEquals(runner.Runs[0].TimeProvider, time))).IsEqualTo((TimeSpan.FromSeconds(3), true));
    }

    [Test]
    public async Task GetCredentialsAsync_CalledTwiceWhileValid_RunsOnceAndReturnsTheSameInstance()
    {
        // Arrange
        var runner = new FakeRunner(Json(Now.AddHours(1)));
        var provider = Create(runner);
        var first = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var second = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((ReferenceEquals(first, second), runner.Runs.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task GetCredentialsAsync_NearExpiration_RunsTheProgramAgain()
    {
        // Arrange (the mandatory refresh period starts 10 minutes before the expiration)
        var time = new ManualTimeProvider(Now);
        var runner = new FakeRunner(Json(Now.AddHours(1), accessKeyId: "FIRST"), Json(Now.AddHours(2), accessKeyId: "SECOND"));
        var provider = new ProcessCredentialsProvider(Profile, "creds", false, ProcessCredentialsProvider.Timeout, Dependencies(runner, time));
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(51));

        // Act
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((credentials.AccessKeyId, runner.Runs.Count)).IsEqualTo(("SECOND", 2));
    }

    [Test]
    public async Task GetCredentialsAsync_WithoutExpiration_IsNeverRunAgainByTheClock()
    {
        // Arrange
        var time = new ManualTimeProvider(Now);
        var runner = new FakeRunner("""{"Version":1,"AccessKeyId":"a","SecretAccessKey":"s"}""");
        var provider = new ProcessCredentialsProvider(Profile, "creds", false, ProcessCredentialsProvider.Timeout, Dependencies(runner, time));
        await provider.GetCredentialsAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromDays(400));

        // Act
        await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That(runner.Runs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TryInvalidate_CurrentCredentials_RunsTheProgramOnTheNextCall()
    {
        // Arrange
        var runner = new FakeRunner(Json(Now.AddHours(1), accessKeyId: "FIRST"), Json(Now.AddHours(1), accessKeyId: "SECOND"));
        var provider = Create(runner);
        var rejected = await provider.GetCredentialsAsync(CancellationToken.None);

        // Act
        var invalidated = provider.TryInvalidate(rejected);
        var credentials = await provider.GetCredentialsAsync(CancellationToken.None);

        // Assert
        await Assert.That((invalidated, credentials.AccessKeyId)).IsEqualTo((true, "SECOND"));
    }

    [Test]
    public async Task TryInvalidate_Null_ThrowsArgumentNullException()
    {
        // Arrange
        var provider = Create(new FakeRunner(Json(Now.AddHours(1))));

        // Act
        var act = () => provider.TryInvalidate(null!);

        // Assert
        await Assert.That(act).ThrowsExactly<ArgumentNullException>().WithParameterName("rejected");
    }

    [Test]
    public async Task GetCredentialsAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var provider = Create(new FakeRunner(Json(Now.AddHours(1))));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = async () => await provider.GetCredentialsAsync(cancelled.Token);

        // Assert
        await Assert.That(act).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Timeout_IsBelowTheDefaultAttemptTimeoutOfAClient()
    {
        // Arrange (so that a client reports why the program failed, and not a timeout of its own)

        // Act
        var timeout = ProcessCredentialsProvider.Timeout;

        // Assert
        await Assert.That(timeout).IsLessThan(new DynamoDbClientOptions().AttemptTimeout);
    }

    internal static string Json(DateTimeOffset expiration, string accessKeyId = AccessKeyId) =>
        $$"""{"Version":1,"AccessKeyId":"{{accessKeyId}}","SecretAccessKey":"{{SecretAccessKey}}","SessionToken":"{{SessionToken}}","Expiration":"{{Timestamp(expiration)}}"}""";

    internal static CredentialsDependencies Dependencies(RunProcess runner, TimeProvider time) =>
        new(Env(), NoFiles, NoWrites, runner, null, time, NullLoggerFactory.Instance);

    private static ProcessCredentialsProvider Create(FakeRunner runner, string commandLine = "creds", bool windows = false) =>
        new(Profile, commandLine, windows, ProcessCredentialsProvider.Timeout, Dependencies(runner.Run, new ManualTimeProvider(Now)));

    /// <summary>A program that answers each run with the next result, and the last one ever after.</summary>
    internal sealed class FakeRunner
    {
        private readonly ProcessResult[] _results;

        public FakeRunner(params string[] outputs)
            : this([.. outputs.Select(output => new ProcessResult(ProcessEnd.Exited, 0, Encoding.UTF8.GetBytes(output)))])
        {
        }

        public FakeRunner(params ProcessResult[] results)
        {
            _results = results;
        }

        public List<(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout, TimeProvider TimeProvider)> Runs { get; } = [];

        public static implicit operator RunProcess(FakeRunner runner) => runner.Run;

        public Task<ProcessResult> Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, TimeProvider timeProvider)
        {
            lock (Runs)
            {
                Runs.Add((fileName, arguments, timeout, timeProvider));
                return Task.FromResult(_results[Math.Min(Runs.Count, _results.Length) - 1]);
            }
        }
    }
}
